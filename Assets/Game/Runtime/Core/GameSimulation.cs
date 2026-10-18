using System;
using System.Collections.Generic;
using System.Linq;

namespace Nasledie.Core
{
    /// <summary>
    /// Deterministic, view-independent campaign simulation. Unity code sends commands and renders results;
    /// it never changes GameState fields directly.
    /// </summary>
    public sealed partial class GameSimulation
    {
        public GameState State { get; private set; }
        public string LastMessage { get; private set; }
        public BattleResolutionEvent[] LastResolvedBattles { get; private set; } = Array.Empty<BattleResolutionEvent>();
        public bool IsReady { get { return State != null; } }
        private List<BattleResolutionEvent> battleCapture;

        public GameSimulation(GameState state)
        {
            State = state ?? throw new ArgumentNullException("state");
            RestoreNationAndDistrictData(State);
            NormalizeTreaties(State);
            if (State.Civilizations != null)
                for (int i = 0; i < State.Civilizations.Length; i++)
                {
                    CivilizationState civilization = State.Civilizations[i];
                    if (civilization.PrisonersHeldByCivilizationTenths == null) civilization.PrisonersHeldByCivilizationTenths = new int[State.Civilizations.Length];
                    else if (civilization.PrisonersHeldByCivilizationTenths.Length != State.Civilizations.Length)
                        Array.Resize(ref civilization.PrisonersHeldByCivilizationTenths, State.Civilizations.Length);
                }
            if (State.Cities != null)
                for (int i = 0; i < State.Cities.Length; i++)
                    EconomyCalculator.NormalizeWorkerAssignments(State, State.Cities[i]);
            NormalizeUnitPopulationData(State);
        }

        private static void NormalizeTreaties(GameState state)
        {
            if (state.Treaties == null) return;
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (treaty == null || (!treaty.Active && !treaty.Pending)) continue;
                int duration = TreatyDuration(treaty.Kind);
                if (treaty.Kind == TreatyKind.TradeRoute) treaty.TurnsRemaining = 0;
                else if (duration > 0 && treaty.TurnsRemaining > duration) treaty.TurnsRemaining = duration;
                else if (duration == 0 && treaty.Kind != TreatyKind.TradeRoute)
                {
                    treaty.Active = false;
                    treaty.Pending = false;
                    treaty.Fulfilled = true;
                }
            }
        }

        public static GameSimulation NewCampaign(int seed, Difficulty difficulty)
        {
            return new GameSimulation(GameFactory.NewGame(seed, difficulty));
        }

        public static GameSimulation NewCampaign(int seed, Difficulty difficulty, GamePace pace)
        {
            return new GameSimulation(GameFactory.NewGame(seed, difficulty, pace));
        }

        public static GameSimulation NewCampaign(int seed, Difficulty difficulty, GamePace pace, string playerNationId)
        {
            return new GameSimulation(GameFactory.NewGame(seed, difficulty, pace, playerNationId));
        }

        public CommandResult Issue(GameCommand command)
        {
            LastResolvedBattles = Array.Empty<BattleResolutionEvent>();
            if (State == null) return CommandResult.Reject("no_game", "Партия не загружена.");
            if (State.IsFinished) return CommandResult.Reject("game_finished", "Кампания уже завершена.");
            if (command == null || command.ActorId != State.HumanCivilizationId)
                return CommandResult.Reject("invalid_actor", "Недопустимый игрок.");
            battleCapture = new List<BattleResolutionEvent>();
            try
            {
                CommandResult result = ApplyCommand(State, command, true);
                LastMessage = result.Message;
                LastResolvedBattles = battleCapture.ToArray();
                if (result.Accepted) RecordPlayerCommand(State, command);
                return result;
            }
            finally { battleCapture = null; }
        }

        public void EndTurn()
        {
            LastResolvedBattles = Array.Empty<BattleResolutionEvent>();
            if (State == null || State.IsFinished) return;
            GameState working = State.Copy();
            battleCapture = new List<BattleResolutionEvent>();
            try
            {
                ResolveTurn(working);
                ValidateInvariants(working);
                State = working;
                LastResolvedBattles = battleCapture.ToArray();
                LastMessage = "Ход рассчитан.";
            }
            catch (Exception exception)
            {
                LastResolvedBattles = Array.Empty<BattleResolutionEvent>();
                LastMessage = "Ход не сохранён: " + exception.Message;
                AppendLog(State, "Внутренняя проверка остановила ход; последнее состояние сохранено.");
            }
            finally { battleCapture = null; }
        }

        public CityYield PreviewCity(int cityId)
        {
            CityState city = State.City(cityId);
            return city == null ? new CityYield() : EconomyCalculator.Calculate(State, city);
        }

        public int CivilizationPopulationTenths(int civilizationId)
        {
            int total = 0;
            for (int i = 0; i < State.Cities.Length; i++)
                if (State.Cities[i].OwnerId == civilizationId) total += State.Cities[i].PopulationTenths;
            for (int i = 0; i < State.Units.Length; i++)
                if (State.Units[i].OwnerId == civilizationId)
                    total += State.Units[i].PopulationTenths + Math.Max(0, State.Units[i].WoundedPopulationTenths);
            CivilizationState civ = State.Civilization(civilizationId);
            if (civ != null) total += civ.RefugeePopulationTenths;
            for (int i = 0; i < State.Civilizations.Length; i++)
            {
                int[] held = State.Civilizations[i].PrisonersHeldByCivilizationTenths;
                if (held != null && civilizationId >= 0 && civilizationId < held.Length) total += Math.Max(0, held[civilizationId]);
            }
            return total;
        }

        public int FindCapital(int civilizationId)
        {
            for (int i = 0; i < State.Cities.Length; i++)
                if (State.Cities[i].OwnerId == civilizationId && State.Cities[i].AdministrativeLoad > 0) return State.Cities[i].Id;
            for (int i = 0; i < State.Cities.Length; i++)
                if (State.Cities[i].OwnerId == civilizationId) return State.Cities[i].Id;
            return -1;
        }

        public CommandResult ApplyCommand(GameState state, GameCommand command, bool playerFacing)
        {
            CivilizationState actor = state.Civilization(command.ActorId);
            if (actor == null || actor.IsDefeated) return CommandResult.Reject("actor_defeated", "Цивилизация больше не действует.");
            CityState city = state.City(command.EntityId);
            switch (command.Kind)
            {
                case CommandKind.SetWorkers:
                    return SetWorkers(state, actor, city, command.Value, command.TargetId, playerFacing);
                case CommandKind.AutoAssignWorkers:
                    return AutoAssignWorkers(state, actor, city, command.Text, playerFacing);
                case CommandKind.SetProject:
                    return SetProject(state, actor, city, command.Text, playerFacing);
                case CommandKind.ChooseTechnology:
                    return ChooseTechnology(actor, command.Text, playerFacing);
                case CommandKind.BuildImprovement:
                    return BeginImprovement(state, actor, city, command.Q, command.R, (ImprovementKind)command.Value, playerFacing);
                case CommandKind.FoundCity:
                    return FoundCity(state, actor, command.EntityId, playerFacing);
                case CommandKind.MoveUnit:
                    return MoveUnit(state, actor, command.EntityId, command.Q, command.R, playerFacing);
                case CommandKind.Recruit:
                    return Recruit(state, actor, city, (UnitKind)command.Value, playerFacing);
                case CommandKind.DeclareWar:
                    return DeclareWar(state, actor, command.TargetId, command.Q, command.R, command.Text, playerFacing);
                case CommandKind.ProposeTreaty:
                    return ProposeTreaty(state, actor, command.TargetId, (TreatyKind)command.Value, command.EntityId, command.Sequence, playerFacing);
                case CommandKind.AcceptTreaty:
                    return AcceptTreaty(state, actor, command.EntityId, playerFacing);
                case CommandKind.MakePeace:
                    return MakePeace(state, actor, command.TargetId, (PeaceTermKind)command.Value, playerFacing);
                case CommandKind.SetTax:
                    actor.TaxRate = (TaxRate)command.Value;
                    AppendLog(state, actor.Name + " изменяет налоговую ставку.");
                    return CommandResult.Ok("Налоговая ставка обновлена.");
                case CommandKind.SetSpending:
                    return SetSpending(actor, city, command.Value, command.TargetId, playerFacing);
                case CommandKind.EnactLaw:
                    return EnactLaw(state, actor, command.Text, playerFacing);
                case CommandKind.EventChoice:
                    return ResolveEvent(state, actor, command.EntityId, command.Text, playerFacing);
                case CommandKind.ContributeToCrisis:
                    return ContributeToCrisis(state, actor, command.Text, command.Value, playerFacing);
                case CommandKind.BeginVictoryProject:
                    return BeginVictoryProject(state, actor, city, (VictoryPath)command.Value, playerFacing);
                case CommandKind.Repair:
                    return Repair(state, actor, city, command.Q, command.R, playerFacing);
                case CommandKind.UpgradeUnit:
                    return UpgradeUnit(state, actor, command.EntityId, playerFacing);
                default:
                    return CommandResult.Reject("unknown_command", "Неизвестное действие.");
            }
        }

        private CommandResult SetWorkers(GameState state, CivilizationState actor, CityState city, int roleValue, int count, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Город не принадлежит вашей цивилизации.");
            if (roleValue < 0 || roleValue >= city.Workers.Length || count < 0) return CommandResult.Reject("invalid_workers", "Недопустимое назначение.");
            int current = 0;
            for (int i = 0; i < city.Workers.Length; i++) current += city.Workers[i];
            int next = current - city.Workers[roleValue] + count;
            if (count > EconomyCalculator.WorkerRoleCapacity(state, city, (WorkerRole)roleValue))
                return CommandResult.Reject("worker_role_capacity", "Для этой роли в городе нет столько рабочих мест. Откройте подходящие клетки или постройте профильный район.");
            if (next > city.WorkerCount || next > EconomyCalculator.WorkerCapacity(state, city))
                return CommandResult.Reject("too_many_workers", "Нет столько жителей или доступных рабочих мест. Расширьте город или уменьшите назначение.");
            city.Workers[roleValue] = count;
            if (feedback) AppendLog(state, city.Name + ": назначение работников изменено.");
            return CommandResult.Ok("Назначение работников сохранено.");
        }

        private CommandResult AutoAssignWorkers(GameState state, CivilizationState actor, CityState city, string focus, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Город не принадлежит вашей цивилизации.");
            string[] supported = { "balanced", "food", "industry", "science", "trade", "culture" };
            bool valid = false;
            for (int i = 0; i < supported.Length; i++) if (supported[i] == focus) valid = true;
            if (!valid) return CommandResult.Reject("worker_focus", "Неизвестный приоритет распределения работников.");
            EconomyCalculator.AutoAssign(state, city, focus);
            if (feedback) AppendLog(state, city.Name + ": работники распределены автоматически («" + focus + "»).");
            return CommandResult.Ok("Работники распределены.");
        }

        private CommandResult SetProject(GameState state, CivilizationState actor, CityState city, string targetId, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Город не принадлежит вашей цивилизации.");
            if (string.Equals(targetId, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (city.Project == null || city.Project.Kind == ProjectKind.None) return CommandResult.Reject("no_project", "В городе нет активного проекта.");
                city.ReservedPopulationTenths = Math.Max(0, city.ReservedPopulationTenths - city.Project.ReservedPopulationTenths);
                if (city.Project.ReservedTreasuryTenths > 0) actor.TreasuryTenths += city.Project.ReservedTreasuryTenths / 2;
                if (city.Project.Kind == ProjectKind.ScientificProject) actor.ScienceProjectTurns = 0;
                city.Project = new CityProject { Kind = ProjectKind.None };
                if (feedback) AppendLog(state, city.Name + ": проект отменён; половина зарезервированной казны возвращена.");
                return CommandResult.Ok("Проект отменён.");
            }
            if (string.Equals(targetId, "era_transition", StringComparison.OrdinalIgnoreCase))
            {
                if (city.Project != null && city.Project.Kind != ProjectKind.None) return CommandResult.Reject("project_busy", "Сначала завершите или отмените текущий проект.");
                if ((int)actor.Era >= 4) return CommandResult.Reject("era_max", "Вы уже достигли последней эпохи.");
                int eraKnowledge = 0;
                for (int i = 0; i < actor.KnownTechnologies.Length; i++)
                {
                    TechnologyDefinition known = RulesCatalog.Technology(actor.KnownTechnologies[i]);
                    if (known != null && known.Era == actor.Era) eraKnowledge++;
                }
                if (eraKnowledge < 3) return CommandResult.Reject("era_technologies", "Чтобы перейти дальше, изучите минимум три технологии текущей эпохи.");
                if (CountOwnedBuilding(state, actor.Id, "council") == 0)
                    return CommandResult.Reject("era_institution", "Для общественного перехода постройте Совет хотя бы в одном городе.");
                for (int i = 0; i < state.Cities.Length; i++)
                    if (state.Cities[i].OwnerId == actor.Id && state.Cities[i].Project != null && state.Cities[i].Project.Kind == ProjectKind.EraTransition)
                        return CommandResult.Reject("era_transition_busy", "В другой город уже вложено производство в переход эпохи.");
                if (actor.InfluenceTenths < 100) return CommandResult.Reject("era_influence", "Переход требует 10 влияния на общие институты.");
                actor.InfluenceTenths -= 100;
                city.Project = new CityProject { Kind = ProjectKind.EraTransition, TargetId = "era_transition", Cost = EraTransitionProductionCost(actor.Era) };
                if (feedback) AppendLog(state, actor.Name + ": начат общественный переход в следующую эпоху.");
                return CommandResult.Ok("Переход эпохи начат.");
            }
            BuildingDefinition building = RulesCatalog.Building(targetId);
            if (building == null) return CommandResult.Reject("unknown_building", "Такого здания нет в каталоге.");
            if (!string.IsNullOrEmpty(building.RequiredNationId) && building.RequiredNationId != actor.NationId)
                return CommandResult.Reject("nation_building", "Этот уникальный район принадлежит другой культуре.");
            if (building.RequiredTechnology != null && !actor.Knows(building.RequiredTechnology)) return CommandResult.Reject("technology_locked", "Сначала изучите технологию: " + building.RequiredTechnology + ".");
            if (city.Project != null && city.Project.TargetId == targetId && city.Project.Kind == ProjectKind.Building)
                return CommandResult.Ok("Проект уже выбран.");
            if (building.UniqueEmpireBuilding)
            {
                for (int i = 0; i < state.Cities.Length; i++)
                {
                    CityState otherCity = state.Cities[i];
                    if (otherCity.OwnerId != actor.Id) continue;
                    if (otherCity.BuildingLevel(targetId) > 0)
                        return CommandResult.Reject("unique_building", "Это уникальное здание уже построено.");
                    if (otherCity.Id != city.Id && otherCity.Project != null && otherCity.Project.Kind == ProjectKind.Building && otherCity.Project.TargetId == targetId)
                        return CommandResult.Reject("unique_building", "Это уникальное здание уже стоит в очереди другого города.");
                }
            }
            if (city.BuildingLevel(targetId) > 0 && targetId != "house")
                return CommandResult.Reject("already_built", "Это здание уже построено в городе.");
            HexCell districtPlot = FindDistrictCell(state, city, targetId);
            if (districtPlot == null) districtPlot = FindDistrictCell(state, city, null);
            if (districtPlot == null)
                return CommandResult.Reject("district_space", "В радиусе четырёх гексов нет свободного места под район. Постройте его в другом городе или освободите подходящую клетку.");
            int carryover = 0;
            if (city.Project != null && city.Project.Kind != ProjectKind.None)
            {
                int loss = Math.Max(1, city.Project.Progress / 10);
                carryover = Math.Max(0, city.Project.Progress - loss);
                if (city.Project.Kind == ProjectKind.ScientificProject) actor.ScienceProjectTurns = 0;
                city.ReservedPopulationTenths = Math.Max(0, city.ReservedPopulationTenths - city.Project.ReservedPopulationTenths);
                if (city.Project.ReservedTreasuryTenths > 0) actor.TreasuryTenths += city.Project.ReservedTreasuryTenths / 2;
                if (feedback) AppendLog(state, city.Name + ": при смене проекта потеряно " + loss + " ПП (10%).");
            }
            if (districtPlot.OwnerId < 0) districtPlot.OwnerId = actor.Id;
            RevealDistrictCell(state, city, districtPlot);
            city.Project = new CityProject
            {
                Kind = ProjectKind.Building, TargetId = targetId, Cost = building.ProductionCost, Progress = carryover,
                TargetQ = districtPlot.Q, TargetR = districtPlot.R, DistrictPlotReserved = true
            };
            if (feedback) AppendLog(state, city.Name + ": начат проект «" + building.NameRu + "».");
            return CommandResult.Ok("Проект начат.");
        }

        public static int EraTransitionProductionCost(Era era)
        {
            // Era I -> II is the 6 point base cost; each later transition rises by 50%.
            double multiplier = Math.Pow(1.5d, Math.Max(0, (int)era));
            return (int)Math.Round(6d * multiplier, MidpointRounding.AwayFromZero);
        }

        private CommandResult ChooseTechnology(CivilizationState civilization, string technologyId, bool feedback)
        {
            int index = RulesCatalog.TechnologyIndex(technologyId);
            if (index < 0) return CommandResult.Reject("unknown_technology", "Технология не найдена.");
            TechnologyDefinition technology = RulesCatalog.Technologies[index];
            if (civilization.Knows(technologyId)) return CommandResult.Reject("technology_known", "Технология уже изучена.");
            if ((int)technology.Era > (int)civilization.Era) return CommandResult.Reject("era_locked", "Технология откроется в более поздней эпохе.");
            for (int i = 0; i < technology.Prerequisites.Length; i++)
            {
                string prerequisite = technology.Prerequisites[i];
                if (prerequisite.Contains("|"))
                {
                    string[] choices = prerequisite.Split('|');
                    if (!civilization.Knows(choices[0]) && !civilization.Knows(choices[1]))
                        return CommandResult.Reject("missing_prerequisite", "Нужна одна из технологий: " + prerequisite.Replace('|', ' '));
                }
                else if (!civilization.Knows(prerequisite)) return CommandResult.Reject("missing_prerequisite", "Сначала изучите «" + prerequisite + "».");
            }
            civilization.ActiveResearchIndex = index;
            if (feedback) LastMessage = "Исследование выбрано: " + technology.NameRu + ".";
            return CommandResult.Ok("Активное исследование изменено.");
        }

        private CommandResult BeginImprovement(GameState state, CivilizationState actor, CityState city, int q, int r, ImprovementKind kind, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Нужен город-владелец клетки.");
            if (city.Project != null && city.Project.Kind != ProjectKind.None) return CommandResult.Reject("project_busy", "Сначала завершите или отмените текущий проект города.");
            HexCell cell = state.Cell(q, r);
            if (cell == null || cell.OwnerId != actor.Id) return CommandResult.Reject("cell_not_owned", "Клетка не принадлежит вашей цивилизации.");
            if (!string.IsNullOrEmpty(cell.DistrictBuildingId)) return CommandResult.Reject("district_occupied", "На этой клетке уже расположен городской район.");
            ImprovementDefinition definition = null;
            for (int i = 0; i < RulesCatalog.Improvements.Length; i++)
                if (RulesCatalog.Improvements[i].Kind == kind && actor.Knows(RulesCatalog.Improvements[i].RequiredTechnology)) definition = RulesCatalog.Improvements[i];
            if (definition == null) return CommandResult.Reject("improvement_locked", "Технология не открыла это улучшение.");
            bool validTerrain = false;
            for (int i = 0; i < definition.AllowedTerrain.Length; i++) if (definition.AllowedTerrain[i] == cell.Terrain) validTerrain = true;
            if (!validTerrain) return CommandResult.Reject("wrong_terrain", "Это улучшение нельзя построить на такой местности.");
            if (cell.Improvement == kind) return CommandResult.Reject("already_improved", "На клетке уже есть такое улучшение.");
            city.Project = new CityProject
            {
                Kind = ProjectKind.Improvement,
                TargetId = kind.ToString(),
                TargetQ = q,
                TargetR = r,
                Cost = definition.ProductionCost
            };
            if (feedback) AppendLog(state, city.Name + ": начато улучшение клетки.");
            return CommandResult.Ok("Улучшение поставлено в очередь.");
        }

        private CommandResult FoundCity(GameState state, CivilizationState actor, int unitId, bool feedback)
        {
            UnitState wagon = FindUnit(state, unitId);
            if (wagon == null || wagon.OwnerId != actor.Id || wagon.Kind != UnitKind.SettlerWagon)
                return CommandResult.Reject("settler_missing", "Нужен гражданский обоз.");
            HexCell cell = state.Cell(wagon.Q, wagon.R);
            if (cell == null || cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Swamp || cell.OwnerId >= 0)
                return CommandResult.Reject("site_unavailable", "На этой клетке нельзя основать город.");
            for (int i = 0; i < state.Cities.Length; i++)
                if (HexGrid.Distance(wagon.Q, wagon.R, state.Cities[i].Q, state.Cities[i].R) < 3)
                    return CommandResult.Reject("too_close", "Новый город должен быть не ближе трёх гексов к существующему.");
            var newCity = new CityState
            {
                Id = state.NextEntityId++, OwnerId = actor.Id, Name = NextCityName(state, actor.Id), Q = wagon.Q, R = wagon.R,
                PopulationTenths = 20, FoodTenths = Math.Max(20, wagon.FoodCargoTenths), FoodStoreCapacityTenths = 80,
                HousingTenths = 60, Health = 55, Safety = 60, Education = 10, Trust = 45, Environment = 100,
                Attraction = 40, EfficiencyBasisPoints = 9000, AdministrativeLoad = 0,
                Workers = new[] { 1, 0, 0, 0, 0, 0 }, ServiceFunding = new[] { 1, 0, 0 },
                Project = new CityProject { Kind = ProjectKind.None, TargetId = "" },
                BuildingIds = Array.Empty<string>(), BuildingLevels = Array.Empty<int>(), CompletedCulturalIds = Array.Empty<string>()
            };
            Array.Resize(ref state.Cities, state.Cities.Length + 1);
            state.Cities[state.Cities.Length - 1] = newCity;
            Array.Resize(ref state.Units, state.Units.Length - 1);
            cell.OwnerId = actor.Id;
            for (int i = 0; i < state.Cells.Length; i++)
                if (HexGrid.Distance(state.Cells[i].Q, state.Cells[i].R, wagon.Q, wagon.R) == 1 && state.Cells[i].OwnerId < 0)
                    state.Cells[i].OwnerId = actor.Id;
            if (feedback) AppendLog(state, actor.Name + " основала город «" + newCity.Name + "».");
            GameFactory.RebuildVisibility(state);
            return CommandResult.Ok("Новый город основан.");
        }

        private CommandResult MoveUnit(GameState state, CivilizationState actor, int unitId, int q, int r, bool feedback)
        {
            UnitState unit = FindUnit(state, unitId);
            if (unit == null || unit.OwnerId != actor.Id) return CommandResult.Reject("unit_not_owned", "Отряд не найден или принадлежит другой стороне.");
            if (unit.Kind == UnitKind.SettlerWagon)
                for (int i = 0; i < state.Events.Length; i++)
                    if (!state.Events[i].IsResolved && state.Events[i].Kind == EventKind.SettlerInterception && state.Events[i].TargetUnitId == unit.Id)
                        return CommandResult.Reject("settler_decision_pending", "Сначала решите судьбу перехваченного обоза в панели событий.");
            if (HexGrid.Distance(unit.Q, unit.R, q, r) != 1) return CommandResult.Reject("not_adjacent", "За один приказ отряд движется на соседний гекс.");
            HexCell destination = state.Cell(q, r);
            if (destination == null || destination.Terrain == TerrainKind.Mountain || destination.Terrain == TerrainKind.Water)
                return CommandResult.Reject("impassable", "На эту клетку нельзя пройти.");
            int movementCost = MovementCost(destination);
            if (unit.MovementLeft < movementCost) return CommandResult.Reject("movement_spent", "На этот ход у отряда не осталось очков движения.");
            if (destination.OwnerId >= 0 && destination.OwnerId != actor.Id && !AtWar(state, actor.Id, destination.OwnerId) && !HasTreaty(state, TreatyKind.OpenBorders, actor.Id, destination.OwnerId))
                return CommandResult.Reject("closed_border", "Нужны открытые границы или состояние войны.");
            if (unit.Kind == UnitKind.SettlerWagon && destination.OwnerId >= 0 && destination.OwnerId != actor.Id)
                return CommandResult.Reject("settler_enemy_territory", "Гражданский обоз не входит на территорию другой цивилизации.");
            UnitState defender = FindUnitAt(state, q, r, actor.Id);
            if (defender != null)
            {
                if (!AtWar(state, actor.Id, defender.OwnerId)) return CommandResult.Reject("unit_blocked", "На клетке стоит отряд другой цивилизации.");
                if (unit.Kind == UnitKind.SettlerWagon)
                    return CommandResult.Reject("settler_cannot_attack", "Гражданский обоз не атакует военные отряды.");
                if (defender.Kind == UnitKind.SettlerWagon)
                {
                    unit.MovementLeft = 0;
                    if (defender.OwnerId == state.HumanCivilizationId)
                        QueueSettlerInterception(state, defender, unit, feedback);
                    else
                        EvacuateSettlerWagon(state, defender, feedback);
                    GameFactory.RebuildVisibility(state);
                    return CommandResult.Ok(defender.OwnerId == state.HumanCivilizationId
                        ? "Обоз перехвачен. Выберите его дальнейшую судьбу в панели событий."
                        : "Обоз противника предупредил угрозу и отступил в безопасное место.");
                }
                ResolveBattle(state, unit, defender, destination, feedback);
                return CommandResult.Ok("Сражение разрешено.");
            }
            unit.Q = q;
            unit.R = r;
            unit.MovementLeft -= movementCost;
            if (feedback) AppendLog(state, RulesCatalog.Unit(unit.Kind).NameRu + " перемещается на клетку " + q + "," + r + ".");
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId == actor.Id && city.OccupiedById >= 0) city.OccupiedById = -1;
                if (city.OwnerId != actor.Id && city.Q == q && city.R == r && AtWar(state, actor.Id, city.OwnerId))
                {
                    city.OccupiedById = actor.Id;
                    city.IntegrationTurns = 0;
                    city.SiegeTurns = 1;
                    if (feedback) AppendLog(state, city.Name + " блокирован; выпуск и безопасность начинают снижаться.");
                }
            }
            GameFactory.RebuildVisibility(state);
            return CommandResult.Ok("Отряд перемещён.");
        }

        private static void QueueSettlerInterception(GameState state, UnitState wagon, UnitState attacker, bool feedback)
        {
            if (wagon == null || attacker == null) return;
            for (int i = 0; i < state.Events.Length; i++)
                if (!state.Events[i].IsResolved && state.Events[i].Kind == EventKind.SettlerInterception &&
                    state.Events[i].TargetUnitId == wagon.Id) return;
            wagon.MovementLeft = 0;
            Append(ref state.Events, new ActiveEventState
            {
                Id = state.NextEntityId++, Kind = EventKind.SettlerInterception, TargetUnitId = wagon.Id,
                TargetCivilizationId = wagon.OwnerId, OtherCivilizationId = attacker.OwnerId,
                TurnsRemaining = 1, Severity = 1, Choices = new[] { "Retreat", "Internment", "ContinueJourney" }
            });
            AppendLog(state, "Военный отряд остановил гражданский обоз. Жители ждут решения: отступить, переждать интернирование или рискнуть прорывом.");
        }

        private static void EvacuateSettlerWagon(GameState state, UnitState wagon, bool feedback)
        {
            HexCell safestCell = null;
            int safestDistance = int.MaxValue;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell candidate = state.Cells[i];
                if (candidate.OwnerId != wagon.OwnerId || candidate.Terrain == TerrainKind.Mountain || candidate.Terrain == TerrainKind.Water ||
                    (candidate.Q == wagon.Q && candidate.R == wagon.R)) continue;
                bool occupied = false;
                for (int unitIndex = 0; unitIndex < state.Units.Length; unitIndex++)
                {
                    UnitState other = state.Units[unitIndex];
                    if (other.Id == wagon.Id || other.Q != candidate.Q || other.R != candidate.R) continue;
                    occupied = true;
                    break;
                }
                if (occupied) continue;
                int distance = HexGrid.Distance(wagon.Q, wagon.R, candidate.Q, candidate.R);
                if (distance < safestDistance)
                {
                    safestDistance = distance;
                    safestCell = candidate;
                }
            }
            if (safestCell != null)
            {
                wagon.Q = safestCell.Q;
                wagon.R = safestCell.R;
                wagon.MovementLeft = 0;
                if (feedback) AppendLog(state, "Гражданский обоз отступил на ближайшую безопасную клетку своей цивилизации.");
                return;
            }

            CivilizationState owner = state.Civilization(wagon.OwnerId);
            if (owner != null) owner.RefugeePopulationTenths += Math.Max(0, wagon.PopulationTenths);
            RemoveUnit(state, wagon.Id);
            if (feedback) AppendLog(state, "Для обоза не нашлось безопасного участка; его жители эвакуированы в поток беженцев.");
        }

        private CommandResult Recruit(GameState state, CivilizationState actor, CityState city, UnitKind kind, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Отряд нужно набирать в своём городе.");
            UnitDefinition definition = RulesCatalog.Unit(kind);
            if (definition == null) return CommandResult.Reject("unit_unknown", "Такого отряда нет.");
            if (city.Project != null && city.Project.Kind != ProjectKind.None) return CommandResult.Reject("project_busy", "Сначала завершите или отмените проект города.");
            if (kind == UnitKind.SettlerWagon)
            {
                if (city.PopulationTenths < 60 || city.FoodTenths < 60) return CommandResult.Reject("settler_requirements", "Поселенцу нужны 6 населения и 6 еды.");
                if (city.PopulationTenths < 80) return CommandResult.Reject("settler_reserve", "После основания у города должно остаться минимум 4 населения.");
                city.FoodTenths -= 60;
                city.Project = new CityProject { Kind = ProjectKind.Recruitment, TargetId = kind.ToString(), Cost = 4, ReservedFoodTenths = 0, ReservedPopulationTenths = 20, TurnsActive = 1 };
                city.ReservedPopulationTenths += 20;
                EconomyCalculator.NormalizeWorkerAssignments(state, city);
                if (feedback) AppendLog(state, city.Name + ": открыт набор поселенца; 6 еды внесены в обоз.");
                return CommandResult.Ok("Поселенец поставлен в очередь.");
            }
            if (!definition.LegacyOnly && (int)definition.Era > (int)actor.Era)
                return CommandResult.Reject("unit_era", "Этот отряд доступен только в более поздней эпохе.");
            if (definition.RequiredTechnology != null && !actor.Knows(definition.RequiredTechnology)) return CommandResult.Reject("unit_technology", "Сначала изучите требуемую технологию.");
            if (definition.RequiredBuilding != null && city.BuildingLevel(definition.RequiredBuilding) <= 0) return CommandResult.Reject("unit_building", "Для этого отряда нужны казармы.");
            if (city.PopulationTenths < definition.PopulationTenths + 40) return CommandResult.Reject("unit_population", "Нужно оставить городу не менее 4 населения.");
            if (actor.TreasuryTenths < definition.TreasuryCostTenths) return CommandResult.Reject("unit_treasury", "Не хватает казны для начала набора.");
            actor.TreasuryTenths -= definition.TreasuryCostTenths;
            city.ReservedPopulationTenths += definition.PopulationTenths;
            EconomyCalculator.NormalizeWorkerAssignments(state, city);
            if (actor.HasLaw("limited_conscription") && AtWarWithAny(state, actor.Id)) city.Trust = Math.Max(0, city.Trust - 10);
            city.Project = new CityProject
            {
                Kind = ProjectKind.Recruitment,
                TargetId = kind.ToString(),
                Cost = definition.ProductionCost,
                ReservedTreasuryTenths = definition.TreasuryCostTenths,
                ReservedPopulationTenths = definition.PopulationTenths,
                TurnsActive = 0
            };
            if (feedback) AppendLog(state, city.Name + ": мобилизованы " + (definition.PopulationTenths / 10.0).ToString("0.0") + " работника. Набор займёт минимум " + definition.RecruitTurns + " хода.");
            return CommandResult.Ok("Набор начат.");
        }

        private CommandResult UpgradeUnit(GameState state, CivilizationState actor, int unitId, bool feedback)
        {
            UnitState unit = FindUnit(state, unitId);
            UnitDefinition definition = unit == null ? null : RulesCatalog.Unit(unit.Kind);
            if (unit == null || unit.OwnerId != actor.Id || definition == null || definition.Class == UnitClass.Civilian)
                return CommandResult.Reject("unit_upgrade_target", "Железное снаряжение доступно только вашим боевым отрядам.");
            if (unit.IronEquipped) return CommandResult.Reject("unit_already_upgraded", "У этого отряда уже есть железное снаряжение.");
            if (!actor.Knows("engineering")) return CommandResult.Reject("unit_upgrade_technology", "Для усиления нужна технология «Инженерия».");
            CityState source = state.City(unit.CitySourceId);
            bool ironAccess = false;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell.OwnerId == actor.Id && cell.Resource == ResourceKind.Iron && cell.Improvement != ImprovementKind.None &&
                    cell.Damage < 75 && (source == null || HexGrid.Distance(source.Q, source.R, cell.Q, cell.R) <= 3))
                {
                    ironAccess = true;
                    break;
                }
            }
            if (!ironAccess) return CommandResult.Reject("unit_upgrade_iron", "Нужно улучшенное месторождение железа рядом с городом набора.");
            if (actor.TreasuryTenths < 50) return CommandResult.Reject("unit_upgrade_cost", "Для снаряжения нужны 5 казны.");
            actor.TreasuryTenths -= 50;
            unit.IronEquipped = true;
            if (feedback) AppendLog(state, definition.NameRu + " получили железное снаряжение и усилили удар.");
            return CommandResult.Ok("Отряд усилен железом.");
        }

        private CommandResult DeclareWar(GameState state, CivilizationState actor, int targetId, int q, int r, string legalGoal, bool feedback)
        {
            CivilizationState target = state.Civilization(targetId);
            if (target == null || targetId == actor.Id || target.IsDefeated) return CommandResult.Reject("invalid_target", "Невозможно объявить войну этой стороне.");
            if (AtWar(state, actor.Id, targetId)) return CommandResult.Reject("already_at_war", "Война с этой стороной уже идёт.");
            ConflictState priorConflict = FindConflict(state, actor.Id, targetId);
            if (priorConflict != null && priorConflict.Phase <= 1) return CommandResult.Reject("crisis_already_active", "Между сторонами уже идёт кризис или мобилизация.");
            if (actor.TreasuryTenths < 80) return CommandResult.Reject("war_budget", "Для мобилизации требуется 8 казны.");
            if (string.IsNullOrEmpty(legalGoal)) return CommandResult.Reject("war_goal", "Перед объявлением войны выберите законную цель.");
            bool breaksNonAggression = false;
            bool breaksTruce = priorConflict != null &&
                ((priorConflict.Phase == 3 && priorConflict.TruceTurns > 0) ||
                 (priorConflict.Phase == 4 && HasTreaty(state, TreatyKind.NonAggression, actor.Id, targetId)));
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || treaty.Kind != TreatyKind.NonAggression ||
                    !((treaty.FromCivilizationId == actor.Id && treaty.ToCivilizationId == targetId) ||
                      (treaty.FromCivilizationId == targetId && treaty.ToCivilizationId == actor.Id))) continue;
                MarkTreatyBroken(state, treaty);
                treaty.Active = false;
                breaksNonAggression = true;
            }
            var guarantors = new List<int>();
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState guarantee = state.Treaties[i];
                if (!guarantee.Active || guarantee.Kind != TreatyKind.Guarantee) continue;
                int guarantorId = guarantee.FromCivilizationId == targetId ? guarantee.ToCivilizationId :
                    guarantee.ToCivilizationId == targetId ? guarantee.FromCivilizationId : -1;
                if (guarantorId < 0 || guarantorId == actor.Id) continue;
                MarkTreatyBroken(state, guarantee);
                guarantee.Active = false;
                if (!guarantors.Contains(guarantorId)) guarantors.Add(guarantorId);
            }
            if (priorConflict != null) priorConflict.Active = false;
            actor.TreasuryTenths -= 80;
            var conflict = new ConflictState
            {
                Id = state.NextEntityId++, AggressorId = actor.Id, DefenderId = targetId,
                TargetQ = q, TargetR = r, LegalGoal = legalGoal, Phase = 0, WarTurn = 0, Active = true
            };
            Append(ref state.Conflicts, conflict);
            actor.WarsStarted++;
            actor.Reputation = Math.Max(0, actor.Reputation - 12);
            actor.Relations[targetId] = Math.Max(-100, actor.Relations[targetId] - 20);
            actor.Trusts[targetId] = Math.Max(0, actor.Trusts[targetId] - 20);
            actor.CulturalInfluence[targetId] = Math.Max(0, actor.CulturalInfluence[targetId] - 15);
            target.CulturalInfluence[actor.Id] = Math.Max(0, target.CulturalInfluence[actor.Id] - 15);
            if (breaksNonAggression)
            {
                actor.Reputation = Math.Max(0, actor.Reputation - 20);
                actor.Relations[targetId] = Math.Max(-100, actor.Relations[targetId] - 15);
                actor.Trusts[targetId] = Math.Max(0, actor.Trusts[targetId] - 15);
                target.Relations[actor.Id] = Math.Max(-100, target.Relations[actor.Id] - 20);
                target.Trusts[actor.Id] = Math.Max(0, target.Trusts[actor.Id] - 20);
                if (feedback) AppendLog(state, "Нарушение пакта о ненападении: договор отмечен нарушенным, а репутационные и дипломатические потери увеличены.");
            }
            if (breaksTruce && !breaksNonAggression)
            {
                actor.Reputation = Math.Max(0, actor.Reputation - 20);
                if (feedback) AppendLog(state, "Повторное нападение во время перемирия: нарушен пятитуровый послевоенный пакт.");
            }
            if (guarantors.Count > 0)
            {
                actor.Reputation = Math.Max(0, actor.Reputation - 10);
                for (int i = 0; i < guarantors.Count; i++)
                {
                    CivilizationState guarantor = state.Civilization(guarantors[i]);
                    if (guarantor == null) continue;
                    guarantor.Threats[actor.Id] = Math.Min(100, guarantor.Threats[actor.Id] + 25);
                    guarantor.Relations[actor.Id] = Math.Max(-100, guarantor.Relations[actor.Id] - 20);
                    guarantor.Trusts[actor.Id] = Math.Max(0, guarantor.Trusts[actor.Id] - 25);
                    actor.Threats[guarantor.Id] = Math.Min(100, actor.Threats[guarantor.Id] + 15);
                }
                for (int i = 0; i < state.Civilizations.Length; i++)
                    if (state.Civilizations[i].Id != actor.Id && state.Civilizations[i].Id != targetId &&
                        !guarantors.Contains(state.Civilizations[i].Id))
                        state.Civilizations[i].Threats[actor.Id] = Math.Min(100, state.Civilizations[i].Threats[actor.Id] + 10);
                if (feedback) AppendLog(state, "Война нарушила гарантию безопасности: гарант повысил угрозу агрессору и снизил доверие.");
            }
            if (feedback) AppendLog(state, actor.Name + " объявила кризисную мобилизацию против цивилизации «" + target.Name + "». Цель: " + legalGoal + ".");
            return CommandResult.Ok("Кризис начался. У стороны есть один ход на ответ и посредничество.");
        }

        private CommandResult ProposeTreaty(GameState state, CivilizationState actor, int targetId, TreatyKind kind, int cityA, int cityB, bool feedback)
        {
            CivilizationState target = state.Civilization(targetId);
            if (target == null || target.Id == actor.Id || target.IsDefeated) return CommandResult.Reject("invalid_target", "Сторона для договора недоступна.");
            if (AtWar(state, actor.Id, targetId) && kind != TreatyKind.Peace && kind != TreatyKind.Mediation)
                return CommandResult.Reject("war_treaty_only", "Во время войны можно предложить только мир или посредничество третьей стороны.");
            int cost = TreatyCost(kind);
            if (actor.InfluenceTenths < cost) return CommandResult.Reject("insufficient_influence", "Не хватает влияния.");
            if (kind == TreatyKind.TradeRoute || kind == TreatyKind.HumanitarianAid)
            {
                CityState first = state.City(cityA);
                CityState second = state.City(cityB);
                if (first == null || second == null || first.OwnerId != actor.Id || second.OwnerId != targetId)
                    return CommandResult.Reject("route_cities", "Договору нужны ваш город и город партнёра.");
                if (!RouteExists(state, first, second)) return CommandResult.Reject("route_blocked", "Между городами нет безопасного пути.");
                if (HasActiveEmbargo(state, first.Id, second.Id)) return CommandResult.Reject("route_embargoed", "Выбранный маршрут закрыт действующим эмбарго.");
                if (kind == TreatyKind.TradeRoute && CountRoutes(state, actor.Id) >= TradeSlots(state, actor.Id)) return CommandResult.Reject("route_slots", "Нет свободного торгового слота.");
                if (kind == TreatyKind.HumanitarianAid && (actor.TreasuryTenths < 50 || first.FoodTenths < first.PopulationTenths * 2 + 20))
                    return CommandResult.Reject("aid_resources", "Для помощи нужны 5 казны и 2 еды сверх городского запаса.");
            }
            ResourceKind exportedResource = ResourceKind.None;
            if (kind == TreatyKind.ResourceExport)
            {
                CityState first = state.City(cityA);
                CityState second = state.City(cityB);
                if (first == null || second == null || first.OwnerId != actor.Id || second.OwnerId != targetId || !RouteExists(state, first, second))
                    return CommandResult.Reject("export_route", "Экспорт требует безопасный путь между городами обеих сторон.");
                if (HasActiveEmbargo(state, first.Id, second.Id)) return CommandResult.Reject("route_embargoed", "Выбранный маршрут закрыт действующим эмбарго.");
                bool hasSalt = false;
                for (int i = 0; i < state.Cells.Length; i++)
                    if (state.Cells[i].OwnerId == actor.Id && state.Cells[i].Resource == ResourceKind.Salt) { hasSalt = true; break; }
                if (!hasSalt) return CommandResult.Reject("export_resource", "У вашей цивилизации нет соляного месторождения для экспорта.");
                exportedResource = ResourceKind.Salt;
            }
            if (kind == TreatyKind.Embargo)
            {
                CityState first = state.City(cityA);
                CityState second = state.City(cityB);
                if (first == null || second == null || first.OwnerId != actor.Id || second.OwnerId != targetId)
                    return CommandResult.Reject("embargo_route", "Выберите свой город и город партнёра, соединённые торговым маршрутом.");
                if (!HasRecognizedEmbargoCrisis(state, actor.Id, targetId))
                    return CommandResult.Reject("embargo_crisis", "Эмбарго требует признанного кризиса: открытой мобилизации или угрозы не ниже 60 при существующем договоре.");
                bool routeExists = false;
                for (int i = 0; i < state.Treaties.Length; i++)
                    if (state.Treaties[i].Active && (state.Treaties[i].Kind == TreatyKind.TradeRoute || state.Treaties[i].Kind == TreatyKind.ResourceExport) &&
                        RouteCitiesMatch(state.Treaties[i], first.Id, second.Id)) { routeExists = true; break; }
                if (!routeExists) return CommandResult.Reject("embargo_route_missing", "На выбранной паре городов нет действующего маршрута для эмбарго.");
            }
            if (kind == TreatyKind.Mediation)
            {
                bool mediableConflict = false;
                for (int i = 0; i < state.Conflicts.Length; i++)
                {
                    ConflictState conflict = state.Conflicts[i];
                    if (conflict.Active && conflict.Phase <= 2 &&
                        (conflict.AggressorId == targetId || conflict.DefenderId == targetId) &&
                        conflict.AggressorId != actor.Id && conflict.DefenderId != actor.Id)
                    { mediableConflict = true; break; }
                }
                if (!mediableConflict) return CommandResult.Reject("mediation_unavailable", "Нужно активное противостояние между двумя другими сторонами.");
            }
            actor.InfluenceTenths -= cost;
            int acceptance = target.Trusts[actor.Id] + target.Relations[actor.Id] / 2 - target.Threats[actor.Id] / 2;
            if (target.Archetype == "Торговец" && kind == TreatyKind.TradeRoute) acceptance += 25;
            if (target.Archetype == "Попечитель" && (kind == TreatyKind.OpenBorders || kind == TreatyKind.NonAggression)) acceptance += 15;
            if (kind == TreatyKind.TradeRoute && !AtWar(state, actor.Id, targetId)) acceptance += 15;
            bool accepted = acceptance >= 25;
            var treaty = new TreatyState
            {
                Id = state.NextEntityId++, Kind = kind, FromCivilizationId = actor.Id, ToCivilizationId = targetId,
                TurnsRemaining = TreatyDuration(kind), RouteCityA = cityA, RouteCityB = cityB,
                TreasuryPerTurnTenths = kind == TreatyKind.TradeRoute ? 20 : kind == TreatyKind.ResourceExport ? 10 : 0,
                ExportedResource = exportedResource,
                Active = accepted, Pending = !accepted
            };
            Append(ref state.Treaties, treaty);
            if (accepted) ApplyTreatyEffect(state, treaty);
            if (feedback)
                AppendLog(state, accepted ? target.Name + " принимает предложение «" + TreatyName(kind) + "».": target.Name + " отклоняет предложение; оно сохранено для ответа.");
            return CommandResult.Ok(accepted ? "Договор заключён." : "Предложение отправлено; согласие не получено.");
        }

        private CommandResult AcceptTreaty(GameState state, CivilizationState actor, int treatyId, bool feedback)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (treaty.Id != treatyId || !treaty.Pending || treaty.ToCivilizationId != actor.Id) continue;
                if (treaty.Kind == TreatyKind.Embargo && !HasRecognizedEmbargoCrisis(state, treaty.FromCivilizationId, treaty.ToCivilizationId))
                    return CommandResult.Reject("embargo_crisis", "Кризис для эмбарго завершился; предложение больше нельзя принять.");
                if (treaty.Kind == TreatyKind.TradeRoute || treaty.Kind == TreatyKind.ResourceExport || treaty.Kind == TreatyKind.HumanitarianAid)
                {
                    CityState first = state.City(treaty.RouteCityA);
                    CityState second = state.City(treaty.RouteCityB);
                    if (first == null || second == null || !RouteExists(state, first, second) || HasActiveEmbargo(state, first.Id, second.Id))
                        return CommandResult.Reject("route_blocked", "Путь договора больше не доступен или закрыт эмбарго.");
                }
                treaty.Active = true;
                treaty.Pending = false;
                ApplyTreatyEffect(state, treaty);
                if (feedback) AppendLog(state, actor.Name + " принимает договор «" + TreatyName(treaty.Kind) + "».");
                return CommandResult.Ok("Договор принят.");
            }
            return CommandResult.Reject("treaty_missing", "Нет такого предложения.");
        }

        private CommandResult MakePeace(GameState state, CivilizationState actor, int targetId, PeaceTermKind terms, bool feedback)
        {
            ConflictState conflict = FindConflict(state, actor.Id, targetId);
            if (conflict == null || conflict.Phase < 0 || conflict.Phase > 2)
                return CommandResult.Reject("no_active_conflict", "Активного конфликта нет.");
            if (actor.Id != conflict.AggressorId && actor.Id != conflict.DefenderId)
                return CommandResult.Reject("peace_not_party", "Мир может предложить только участник конфликта.");
            CivilizationState target = state.Civilization(targetId);
            if (terms < PeaceTermKind.StatusQuo || terms > PeaceTermKind.PrisonerExchange)
                return CommandResult.Reject("peace_term_invalid", "Недопустимые условия мира.");
            int actorStrength = MilitaryStrength(state, actor.Id);
            int targetStrength = MilitaryStrength(state, targetId);
            CityState heldCity = state.City(conflict.OccupationCityId);
            bool leverage = actorStrength >= targetStrength || target.WarStrain >= 50 || conflict.WarTurn >= 3 ||
                            (heldCity != null && heldCity.OccupiedById == actor.Id);
            UnitState disarmTarget = WeakestMilitaryUnit(state, targetId);
            HexCell claimedCell = state.Cell(conflict.TargetQ, conflict.TargetR);
            string declaredGoal = (conflict.LegalGoal ?? "").ToLowerInvariant();
            bool isCityCell = false;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].Q == conflict.TargetQ && state.Cities[i].R == conflict.TargetR) { isCityCell = true; break; }
            CityState cityToAnnex = null;
            if (terms == PeaceTermKind.AnnexOccupiedCity)
                for (int i = 0; i < state.Cities.Length; i++)
                    if (state.Cities[i].OwnerId == targetId && state.Cities[i].OccupiedById == actor.Id) { cityToAnnex = state.Cities[i]; break; }

            if (terms == PeaceTermKind.Reparations && (!leverage || target.TreasuryTenths < 50))
                return CommandResult.Reject("peace_reparations_rejected", "Сторона противника отвергла компенсацию: у неё достаточно сил или нет средств для выплаты.");
            if (terms == PeaceTermKind.Demobilization && (!leverage || disarmTarget == null))
                return CommandResult.Reject("peace_demobilization_rejected", "Противник отверг демобилизацию: требование превышает вашу переговорную силу.");
            if (terms == PeaceTermKind.BorderAdjustment &&
                (!leverage || claimedCell == null || claimedCell.OwnerId != targetId || isCityCell ||
                 (!declaredGoal.Contains("границ") && !declaredGoal.Contains("спор"))))
                return CommandResult.Reject("peace_border_rejected", "Нет спорной пограничной клетки, которую можно передать по заявленной цели войны.");
            if (terms == PeaceTermKind.AnnexOccupiedCity &&
                (!leverage || cityToAnnex == null || (!declaredGoal.Contains("город") && !declaredGoal.Contains("территор"))))
                return CommandResult.Reject("peace_annex_rejected", "Для аннексии нужен занятый вами город и соответствующая территориальная цель войны.");
            if (terms == PeaceTermKind.AnnexOccupiedCity && CountOwnedCities(state, actor.Id) >= actor.AdministrativeCapacity)
                return CommandResult.Reject("peace_admin_capacity", "Нет свободного места в административной ёмкости для аннексии.");
            if (terms == PeaceTermKind.AnnexOccupiedCity && (actor.InfluenceTenths < 150 || actor.TreasuryTenths < 50))
                return CommandResult.Reject("peace_annex_cost", "Аннексия требует 15 влияния и 5 казны на мирное соглашение.");
            if (terms == PeaceTermKind.PrisonerExchange && !HasPrisonersHeld(state, actor.Id, targetId))
                return CommandResult.Reject("peace_prisoners_missing", "Стороны не удерживают военнопленных для обмена.");

            if (actor.TreasuryTenths >= 50) actor.TreasuryTenths -= 50;
            else actor.Reputation = Math.Max(0, actor.Reputation - 8);
            if (terms == PeaceTermKind.Reparations)
            {
                int payment = Math.Min(100, target.TreasuryTenths);
                target.TreasuryTenths -= payment;
                actor.TreasuryTenths += payment;
            }
            else if (terms == PeaceTermKind.Demobilization)
            {
                RetireMilitarySurvivors(state, disarmTarget);
                RemoveUnit(state, disarmTarget.Id);
            }
            else if (terms == PeaceTermKind.BorderAdjustment)
            {
                claimedCell.OwnerId = actor.Id;
                claimedCell.VisibleToMask |= (1 << actor.Id) | (1 << targetId);
                claimedCell.ExploredByMask |= (1 << actor.Id) | (1 << targetId);
            }
            else if (terms == PeaceTermKind.AnnexOccupiedCity)
            {
                actor.InfluenceTenths -= 150;
                cityToAnnex.OwnerId = actor.Id;
                cityToAnnex.OccupiedById = -1;
                cityToAnnex.IntegrationTurns = 5;
                cityToAnnex.Trust = actor.HasLaw("local_councils") ? 45 : 35;
                cityToAnnex.Attraction = Math.Max(0, cityToAnnex.Attraction - 15);
                cityToAnnex.WarDamageTurns = Math.Max(cityToAnnex.WarDamageTurns, 5);
                for (int i = 0; i < state.Cells.Length; i++)
                {
                    HexCell cityCell = state.Cells[i];
                    if ((cityCell.Q == cityToAnnex.Q && cityCell.R == cityToAnnex.R) || cityCell.DistrictCityId == cityToAnnex.Id)
                    {
                        cityCell.OwnerId = actor.Id;
                        cityCell.VisibleToMask |= (1 << actor.Id) | (1 << targetId);
                        cityCell.ExploredByMask |= (1 << actor.Id) | (1 << targetId);
                    }
                }
                target.Reputation = Math.Max(0, target.Reputation - 8);
                actor.Reputation = Math.Max(0, actor.Reputation - 4);
            }
            else if (terms == PeaceTermKind.PrisonerExchange)
            {
                ReleasePrisoners(state, actor.Id, targetId);
            }

            // Peace stops occupation at signature time. The recovery timer governs
            // economic healing; it must not leave soldiers in foreign territory.
            ReleaseOccupation(state, conflict);
            conflict.Phase = 3;
            conflict.TruceTurns = 3;
            conflict.AgreedPeaceTerm = terms;
            conflict.PeaceRequesterId = actor.Id;
            Append(ref state.Treaties, new TreatyState
            {
                Id = state.NextEntityId++, Kind = TreatyKind.NonAggression,
                FromCivilizationId = actor.Id, ToCivilizationId = targetId, TurnsRemaining = 5, Active = true
            });
            actor.Relations[targetId] = Math.Min(100, actor.Relations[targetId] + 5);
            target.Relations[actor.Id] = Math.Min(100, target.Relations[actor.Id] + 3);
            string goal = (conflict.LegalGoal ?? "").ToLowerInvariant();
            if (goal.Contains("блокад") || goal.Contains("маршрут")) RestoreRouteAfterPeace(state, conflict.AggressorId, conflict.DefenderId);
            if (goal.Contains("демилитар") && terms != PeaceTermKind.Demobilization && disarmTarget != null)
            {
                RetireMilitarySurvivors(state, disarmTarget);
                RemoveUnit(state, disarmTarget.Id);
            }
            if (goal.Contains("защит") || goal.Contains("граждан"))
            {
                actor.Reputation = Math.Min(100, actor.Reputation + 4);
                target.WarStrain = Math.Max(0, target.WarStrain - 5);
            }
            string termName = terms == PeaceTermKind.Reparations ? "мир с компенсацией" :
                terms == PeaceTermKind.Demobilization ? "мир с демобилизацией" :
                terms == PeaceTermKind.BorderAdjustment ? "мир с передачей пограничной клетки" :
                terms == PeaceTermKind.AnnexOccupiedCity ? "мир с аннексией занятого города" :
                terms == PeaceTermKind.PrisonerExchange ? "мир с обменом пленными" : "мир без территориальных уступок";
            if (feedback) AppendLog(state, "Стороны приняли условия: " + termName + ". Действуют два хода перемирия и пять ходов восстановления.");
            return CommandResult.Ok("Заключён " + termName + ".");
        }

        private CommandResult SetSpending(CivilizationState actor, CityState city, int service, int funding, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Город не принадлежит вашей цивилизации.");
            if (service < 0 || service >= city.ServiceFunding.Length || funding < 0 || funding > 2)
                return CommandResult.Reject("invalid_service", "Недопустимое финансирование службы.");
            city.ServiceFunding[service] = funding;
            return CommandResult.Ok("Финансирование службы обновлено.");
        }

        private CommandResult EnactLaw(GameState state, CivilizationState actor, string lawId, bool feedback)
        {
            LawDefinition law = null;
            for (int i = 0; i < RulesCatalog.Laws.Length; i++) if (RulesCatalog.Laws[i].Id == lawId) law = RulesCatalog.Laws[i];
            if (law == null) return CommandResult.Reject("law_missing", "Закон не найден.");
            if ((int)law.Era > (int)actor.Era) return CommandResult.Reject("law_era", "Закон недоступен в текущей эпохе.");
            if (actor.LawCooldownTurns > 0) return CommandResult.Reject("law_cooldown", "Реформу можно принять после кулдауна.");
            if (actor.InfluenceTenths < 50) return CommandResult.Reject("law_influence", "Для реформы нужно 5 влияния.");
            if (actor.HasLaw(lawId)) return CommandResult.Reject("law_active", "Этот закон уже действует.");
            actor.InfluenceTenths -= 50;
            var active = new List<string>(actor.Laws);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                LawDefinition existing = null;
                for (int j = 0; j < RulesCatalog.Laws.Length; j++) if (RulesCatalog.Laws[j].Id == active[i]) existing = RulesCatalog.Laws[j];
                if (existing != null && existing.Category == law.Category)
                {
                    actor.AdministrativeCapacity -= AdministrativeBonus(existing.Id);
                    active.RemoveAt(i);
                }
            }
            active.Add(lawId);
            actor.Laws = active.ToArray();
            actor.AdministrativeCapacity += AdministrativeBonus(lawId);
            actor.LawCooldownTurns = 3;
            if (feedback) AppendLog(state, "Принят закон «" + law.NameRu + "».");
            return CommandResult.Ok("Реформа принята.");
        }

        private static int AdministrativeBonus(string lawId)
        {
            return lawId == "centralized_service" ? 2 : lawId == "local_councils" ? 1 : 0;
        }

        private CommandResult ResolveEvent(GameState state, CivilizationState actor, int eventId, string choiceId, bool feedback)
        {
            ActiveEventState activeEvent = null;
            for (int i = 0; i < state.Events.Length; i++)
                if (state.Events[i].Id == eventId && !state.Events[i].IsResolved) activeEvent = state.Events[i];
            if (activeEvent == null) return CommandResult.Reject("event_missing", "Событие уже закрыто.");
            if (activeEvent.TargetCivilizationId != actor.Id) return CommandResult.Reject("event_not_owned", "Событие адресовано другой цивилизации.");
            CityState city = state.City(activeEvent.TargetCityId);
            if (city != null && city.OwnerId != actor.Id) return CommandResult.Reject("event_not_owned", "Событие относится к другой цивилизации.");
            string choice = choiceId ?? "";
            string resolutionMessage = "Решение события применено.";
            switch (activeEvent.Kind)
            {
                case EventKind.NeutralMediation:
                    ConflictState mediationConflict = FindConflict(state, actor.Id, activeEvent.OtherCivilizationId);
                    if (mediationConflict == null || mediationConflict.Phase != 2)
                        return CommandResult.Reject("mediation_conflict_missing", "На фронте больше нет активной войны для посредничества.");
                    CivilizationState mediationOther = state.Civilization(activeEvent.OtherCivilizationId);
                    if (choice == "AcceptMediation")
                    {
                        mediationConflict.Phase = 3;
                        mediationConflict.TruceTurns = 3;
                        CivilizationState mediationAggressor = state.Civilization(mediationConflict.AggressorId);
                        CivilizationState mediationDefender = state.Civilization(mediationConflict.DefenderId);
                        if (mediationAggressor != null) mediationAggressor.WarStrain = Math.Max(0, mediationAggressor.WarStrain - 10);
                        if (mediationDefender != null) mediationDefender.WarStrain = Math.Max(0, mediationDefender.WarStrain - 10);
                        ReleaseOccupation(state, mediationConflict);
                        resolutionMessage = "Предложение принято. Стороны заключили двухходовое перемирие и начали восстановление.";
                    }
                    else if (choice == "RejectMediation")
                    {
                        actor.WarStrain = Math.Min(100, actor.WarStrain + 8);
                        if (mediationOther != null) mediationOther.WarStrain = Math.Min(100, mediationOther.WarStrain + 5);
                        actor.Reputation = Math.Max(0, actor.Reputation - 2);
                        resolutionMessage = "Предложение отклонено. Война продолжается, напряжение сторон выросло.";
                    }
                    else return CommandResult.Reject("event_choice_invalid", "Выберите принятие или отказ от посредничества.");
                    break;
                case EventKind.SettlerInterception:
                    UnitState interceptedWagon = FindUnit(state, activeEvent.TargetUnitId);
                    if (interceptedWagon == null || interceptedWagon.Kind != UnitKind.SettlerWagon || interceptedWagon.OwnerId != actor.Id)
                        return CommandResult.Reject("settler_event_target", "Гражданский обоз уже не находится под угрозой.");
                    if (choice == "Retreat")
                    {
                        EvacuateSettlerWagon(state, interceptedWagon, feedback);
                        resolutionMessage = "Обоз отступил к ближайшей безопасной территории.";
                    }
                    else if (choice == "Internment")
                    {
                        interceptedWagon.MovementLeft = 0;
                        resolutionMessage = "Обоз интернирован до конца текущего хода; со следующего сможет продолжить путь.";
                    }
                    else if (choice == "ContinueJourney")
                    {
                        int loss = Math.Max(1, interceptedWagon.PopulationTenths / 5);
                        interceptedWagon.PopulationTenths = Math.Max(0, interceptedWagon.PopulationTenths - loss);
                        interceptedWagon.InitialPopulationTenths = Math.Max(interceptedWagon.PopulationTenths, interceptedWagon.InitialPopulationTenths - loss);
                        interceptedWagon.MovementLeft = 0;
                        if (interceptedWagon.PopulationTenths <= 0) RemoveUnit(state, interceptedWagon.Id);
                        resolutionMessage = interceptedWagon.PopulationTenths <= 0
                            ? "Попытка прорыва сорвалась: обоз потерян вместе с его жителями."
                            : "Обоз продолжит путь со следующего хода, потеряв часть жителей при прорыве.";
                    }
                    else return CommandResult.Reject("event_choice_invalid", "Выберите отступление, интернирование или прорыв.");
                    break;
                case EventKind.Drought:
                    if (choice == "BuyFood" && actor.TreasuryTenths >= 50) { actor.TreasuryTenths -= 50; if (city != null) city.FoodTenths += 40; }
                    else if (choice == "ReassignWorkers" && city != null) { Array.Clear(city.Workers, 0, city.Workers.Length); city.Workers[(int)WorkerRole.Farmer] = Math.Min(4, city.WorkerCount); }
                    else if (choice == "Rationing" && city != null) { city.Trust = Math.Max(0, city.Trust - 5); city.FoodShortageTurns = Math.Max(0, city.FoodShortageTurns - 1); }
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.Epidemic:
                    if (choice == "OpenClinic" && city != null && actor.TreasuryTenths >= (EconomyCalculator.HasNearbyResource(state, city, ResourceKind.Herbs) ? 90 : 100))
                    {
                        actor.TreasuryTenths -= EconomyCalculator.HasNearbyResource(state, city, ResourceKind.Herbs) ? 90 : 100;
                        city.Health = Math.Min(100, city.Health + 10);
                    }
                    else if (choice == "Quarantine" && city != null) { city.Health = Math.Min(100, city.Health + 12); city.Attraction = Math.Max(0, city.Attraction - 10); CloseTradeRoutes(state, actor.Id, activeEvent.TargetCivilizationId); }
                    else if (choice == "RequestAid") { actor.Reputation = Math.Min(100, actor.Reputation + 3); if (city != null) city.Health = Math.Min(100, city.Health + 5); }
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.RoadCongestion:
                    if (choice == "BuildHub" && city != null && actor.TreasuryTenths >= 60) { actor.TreasuryTenths -= 60; city.EfficiencyBasisPoints = Math.Min(11500, city.EfficiencyBasisPoints + 500); }
                    else if (choice == "RestrictTraffic" && city != null) city.Attraction = Math.Max(0, city.Attraction - 5);
                    else if (choice == "AcceptDelay" && city != null) city.EfficiencyBasisPoints = Math.Max(5000, city.EfficiencyBasisPoints - 500);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.RefugeeFlow:
                    if (choice == "Accept" && city != null && actor.TreasuryTenths >= 40) { actor.TreasuryTenths -= 40; city.PopulationTenths += 10; city.Trust = Math.Min(100, city.Trust + 3); }
                    else if (choice == "OpenCorridor" && actor.InfluenceTenths >= 20) { actor.InfluenceTenths -= 20; actor.Reputation = Math.Min(100, actor.Reputation + 5); }
                    else if (choice == "Refuse") actor.Reputation = Math.Max(0, actor.Reputation - 3);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.ProcurementCorruption:
                    if (choice == "Investigate" && actor.TreasuryTenths >= 50) { actor.TreasuryTenths -= 50; if (city != null) city.Trust = Math.Min(100, city.Trust + 5); }
                    else if (choice == "Audit" && actor.InfluenceTenths >= 20) { actor.InfluenceTenths -= 20; actor.Reputation = Math.Min(100, actor.Reputation + 4); }
                    else if (choice == "Ignore" && city != null) city.Trust = Math.Max(0, city.Trust - 6);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.Smog:
                    if (choice == "Filters" && actor.TreasuryTenths >= 60) { actor.TreasuryTenths -= 60; if (city != null) city.Environment = Math.Min(100, city.Environment + 10); }
                    else if (choice == "ReduceOutput" && city != null) city.EfficiencyBasisPoints = Math.Max(5000, city.EfficiencyBasisPoints - 500);
                    else if (choice == "Ignore" && city != null) city.Health = Math.Max(0, city.Health - 4);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.EducationReform:
                    if (choice == "FundSchools" && actor.TreasuryTenths >= 80) { actor.TreasuryTenths -= 80; if (city != null) city.Education = Math.Min(100, city.Education + 8); }
                    else if (choice == "Experiment" && actor.InfluenceTenths >= 20) { actor.InfluenceTenths -= 20; if (city != null) city.Education = Math.Min(100, city.Education + 5); }
                    else if (choice == "KeepBudget" && city != null) city.Trust = Math.Max(0, city.Trust - 2);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.BorderDispute:
                    if (activeEvent.OtherCivilizationId < 0 || activeEvent.OtherCivilizationId >= actor.Relations.Length) return CommandResult.Reject("event_target", "У спора нет дипломатической стороны.");
                    if (choice == "Negotiate") { actor.InfluenceTenths = Math.Max(0, actor.InfluenceTenths - 10); actor.Reputation = Math.Min(100, actor.Reputation + 2); }
                    else if (choice == "ExchangeLand" && actor.InfluenceTenths >= 20)
                    {
                        HexCell actorCell;
                        HexCell otherCell;
                        if (!TryFindLandExchange(state, city, actor.Id, activeEvent.OtherCivilizationId, out actorCell, out otherCell))
                            return CommandResult.Reject("exchange_unavailable", "На общей границе нет двух свободных участков для обмена.");
                        actor.InfluenceTenths -= 20;
                        actorCell.OwnerId = activeEvent.OtherCivilizationId;
                        otherCell.OwnerId = actor.Id;
                        actor.Relations[activeEvent.OtherCivilizationId] = Math.Min(100, actor.Relations[activeEvent.OtherCivilizationId] + 5);
                        CivilizationState other = state.Civilization(activeEvent.OtherCivilizationId);
                        if (other != null) other.Relations[actor.Id] = Math.Min(100, other.Relations[actor.Id] + 5);
                        int visibilityMask = (1 << actor.Id) | (1 << activeEvent.OtherCivilizationId);
                        actorCell.VisibleToMask |= visibilityMask;
                        actorCell.ExploredByMask |= visibilityMask;
                        otherCell.VisibleToMask |= visibilityMask;
                        otherCell.ExploredByMask |= visibilityMask;
                        resolutionMessage = "Два свободных участка на общей границе обменены; отношения улучшились.";
                    }
                    else if (choice == "Ultimatum") actor.Relations[activeEvent.OtherCivilizationId] = Math.Max(-100, actor.Relations[activeEvent.OtherCivilizationId] - 10);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.TradeShortage:
                    if (choice == "EmergencyStock" && actor.TreasuryTenths >= 60) { actor.TreasuryTenths -= 60; if (city != null) city.FoodTenths += 30; }
                    else if (choice == "NewRoute")
                    {
                        CityState origin;
                        CityState destination;
                        int partnerId;
                        if (!TryFindEventTradeRoute(state, actor.Id, out origin, out destination, out partnerId))
                            return CommandResult.Reject("route_unavailable", "Нет доступного партнёра с безопасным маршрутом и свободной торговой ёмкостью.");
                        CommandResult routeResult = ProposeTreaty(state, actor, partnerId, TreatyKind.TradeRoute, origin.Id, destination.Id, false);
                        if (!routeResult.Accepted) return routeResult;
                        resolutionMessage = "Торговый маршрут открыт.";
                    }
                    else if (choice == "DomesticSupply" && city != null)
                    {
                        if (!CanStartDomesticSupply(state, city))
                            return CommandResult.Reject("domestic_supply_unavailable", "Нужны свободная очередь и участок для амбара; в городе не должно быть действующего амбара.");
                        HexCell districtPlot = FindDistrictCell(state, city, null);
                        if (districtPlot == null) return CommandResult.Reject("district_plot_missing", "Для амбара не осталось подходящего участка.");
                        if (districtPlot.OwnerId < 0) districtPlot.OwnerId = city.OwnerId;
                        RevealDistrictCell(state, city, districtPlot);
                        city.Project = new CityProject { Kind = ProjectKind.Building, TargetId = "granary", Cost = 10,
                            TargetQ = districtPlot.Q, TargetR = districtPlot.R, DistrictPlotReserved = true };
                        resolutionMessage = "В очереди города начато строительство амбара.";
                    }
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.CulturalExchange:
                    if (choice == "Festival" && actor.TreasuryTenths >= 40) { actor.TreasuryTenths -= 40; actor.CulturePrestige += 5; }
                    else if (choice == "JointMuseum" && actor.InfluenceTenths >= 20) { actor.InfluenceTenths -= 20; actor.CulturePrestige += 3; actor.Reputation = Math.Min(100, actor.Reputation + 2); }
                    else if (choice == "CloseExchange" && city != null) city.Attraction = Math.Max(0, city.Attraction - 5);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.MobilizationFatigue:
                    if (choice == "PaySupport" && actor.TreasuryTenths >= 80) { actor.TreasuryTenths -= 80; actor.WarStrain = Math.Max(0, actor.WarStrain - 10); }
                    else if (choice == "LocalTruce")
                    {
                        bool truceDeclared = false;
                        for (int i = 0; i < state.Conflicts.Length; i++)
                        {
                            ConflictState conflict = state.Conflicts[i];
                            if (conflict == null || !conflict.Active || conflict.Phase < 1 || conflict.Phase > 2 ||
                                (conflict.AggressorId != actor.Id && conflict.DefenderId != actor.Id)) continue;
                            int otherId = conflict.AggressorId == actor.Id ? conflict.DefenderId : conflict.AggressorId;
                            conflict.Phase = 3;
                            conflict.TruceTurns = 2;
                            if (otherId >= 0 && otherId < actor.Relations.Length)
                                actor.Relations[otherId] = Math.Min(100, actor.Relations[otherId] + 3);
                            truceDeclared = true;
                        }
                        if (!truceDeclared) return CommandResult.Reject("truce_unavailable", "Нет активного конфликта, в котором можно объявить перемирие.");
                        resolutionMessage = "На всех ваших активных фронтах объявлено двухходовое перемирие.";
                    }
                    else if (choice == "Continue") actor.Reputation = Math.Max(0, actor.Reputation - 2);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.SharedResearch:
                    if (choice == "JointProject" && actor.KnowledgeTenths >= 100) { actor.KnowledgeTenths -= 100; actor.CulturePrestige += 2; }
                    else if (choice == "NationalProject") actor.CulturePrestige += 1;
                    else if (choice == "ShareKnowledge" && actor.KnowledgeTenths >= 50) { actor.KnowledgeTenths -= 50; actor.Reputation = Math.Min(100, actor.Reputation + 3); }
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
            }
            activeEvent.IsResolved = true;
            if (feedback) AppendLog(state, "Событие «" + EventName(activeEvent.Kind) + "» разрешено: " + choice + ".");
            return CommandResult.Ok(resolutionMessage);
        }

        private CommandResult ContributeToCrisis(GameState state, CivilizationState actor, string resource, int amount, bool feedback)
        {
            if (state.Crisis == null || state.Crisis.Kind == CrisisKind.None || state.Crisis.Resolved)
                return CommandResult.Reject("crisis_missing", "Сейчас нет активного глобального кризиса.");
            if (amount < 1 || state.Crisis.Threat <= 0) return CommandResult.Reject("crisis_amount", "Размер вклада должен быть положительным, а кризис ещё должен угрожать миру.");
            int maximumUsefulContribution = Math.Max(1, (state.Crisis.Threat + 3) / 4);
            int contribution = Math.Min(amount, maximumUsefulContribution);
            if (resource == "food")
            {
                CityState city = null;
                for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == actor.Id && (city == null || state.Cities[i].FoodTenths > city.FoodTenths)) city = state.Cities[i];
                if (city == null || city.FoodTenths < contribution * 10) return CommandResult.Reject("crisis_food", "Не хватает свободной еды.");
                city.FoodTenths -= contribution * 10;
            }
            else if (resource == "knowledge")
            {
                if (actor.KnowledgeTenths < contribution * 10) return CommandResult.Reject("crisis_knowledge", "Не хватает знаний.");
                actor.KnowledgeTenths -= contribution * 10;
            }
            else if (resource == "influence")
            {
                if (actor.InfluenceTenths < contribution * 10) return CommandResult.Reject("crisis_influence", "Не хватает влияния.");
                actor.InfluenceTenths -= contribution * 10;
            }
            else return CommandResult.Reject("crisis_resource", "Кризис принимает еду, знания или влияние.");
            state.Crisis.Threat = Math.Max(0, state.Crisis.Threat - contribution * 4);
            state.Crisis.ContributorsMask |= 1 << actor.Id;
            if (state.Crisis.Threat == 0) { state.Crisis.Resolved = true; state.HasResolvedGlobalCrisis = true; }
            actor.Reputation = Math.Min(100, actor.Reputation + 2);
            if (state.Crisis.Threat == 0) state.ResolvedGlobalCrisisContributorMask |= state.Crisis.ContributorsMask;
            if (feedback) AppendLog(state, actor.Name + " внёс вклад " + contribution + " в глобальный кризис.");
            return CommandResult.Ok("Внесено " + contribution + " ед. ресурса. Угроза кризиса: " + state.Crisis.Threat + ".");
        }

        private CommandResult BeginVictoryProject(GameState state, CivilizationState actor, CityState city, VictoryPath path, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Проекту нужен свой город.");
            if (city.Project != null && city.Project.Kind != ProjectKind.None) return CommandResult.Reject("project_busy", "Сначала завершите или отмените текущий проект города.");
            if (path == VictoryPath.Scientific)
            {
                if (!VictoryEvaluator.CanStartScientificProgram(state, actor.Id, city.Id)) return CommandResult.Reject("science_prerequisites", "Нужны финальные научные технологии, университеты в двух городах, академия и свободная очередь.");
                if (actor.KnowledgeTenths < 300 || actor.InfluenceTenths < 80) return CommandResult.Reject("science_resources", "Нужно 30 знаний и 8 влияния.");
                actor.KnowledgeTenths -= 300;
                actor.InfluenceTenths -= 80;
                actor.ScienceProjectTurns = 0;
                city.Project = new CityProject { Kind = ProjectKind.ScientificProject, TargetId = "common_knowledge", Cost = 20, Progress = 0 };
            }
            else if (path == VictoryPath.Cultural)
            {
                if (!VictoryEvaluator.CanStartCulturalFestival(state, actor.Id))
                    return CommandResult.Reject("culture_requirements", "Нужны 100 престижа, три разных культурных здания в трёх городах и влияние 70/70/40 у соседей.");
                if (actor.InfluenceTenths < 200 || actor.TreasuryTenths < 100) return CommandResult.Reject("festival_resources", "Фестиваль стоит 20 влияния и 10 казны.");
                actor.InfluenceTenths -= 200;
                actor.TreasuryTenths -= 100;
                actor.CultureFestivalTurns = 2;
            }
            else if (path == VictoryPath.Diplomatic)
            {
                if (!VictoryEvaluator.CanStartDiplomaticCoalition(state, actor.Id, city.Id))
                    return CommandResult.Reject("diplomacy_requirements", "Нужны Международная ассамблея на узле двух торговых маршрутов, разрешённый кризис с тремя участниками и три голоса.");
                if (actor.InfluenceTenths < 200) return CommandResult.Reject("diplomacy_fund", "Международный фонд требует 20 влияния.");
                actor.InfluenceTenths -= 200;
                actor.DiplomaticCoalitionTurns = 2;
            }
            else return CommandResult.Reject("victory_path", "Этот путь не использует отдельный проект.");
            if (feedback) AppendLog(state, "Запущен этап победного пути: " + path + ".");
            return CommandResult.Ok("Проект победы начат.");
        }

        private CommandResult Repair(GameState state, CivilizationState actor, CityState city, int q, int r, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("repair_city", "Ремонт выполняется в своём городе.");
            HexCell cell = state.Cell(q, r);
            if (cell == null || cell.OwnerId != actor.Id || (!cell.HasRoad && cell.Damage <= 0)) return CommandResult.Reject("repair_road", "На выбранной клетке нет повреждённой инфраструктуры.");
            if (city.Project != null && city.Project.Kind != ProjectKind.None) return CommandResult.Reject("project_busy", "У города уже есть активный проект.");
            city.Project = new CityProject { Kind = ProjectKind.Improvement, TargetId = cell.HasRoad ? "repair_road" : "repair_cell", TargetQ = q, TargetR = r, Cost = 4 };
            if (feedback) AppendLog(state, city.Name + ": начат ремонт дороги.");
            return CommandResult.Ok("Ремонт добавлен в очередь.");
        }
    }
}
