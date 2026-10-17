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
        public bool IsReady { get { return State != null; } }

        public GameSimulation(GameState state)
        {
            State = state ?? throw new ArgumentNullException("state");
        }

        public static GameSimulation NewCampaign(int seed, Difficulty difficulty)
        {
            return new GameSimulation(GameFactory.NewGame(seed, difficulty));
        }

        public CommandResult Issue(GameCommand command)
        {
            if (State == null) return CommandResult.Reject("no_game", "Партия не загружена.");
            if (State.IsFinished) return CommandResult.Reject("game_finished", "Кампания уже завершена.");
            if (command == null || command.ActorId != State.HumanCivilizationId)
                return CommandResult.Reject("invalid_actor", "Недопустимый игрок.");
            CommandResult result = ApplyCommand(State, command, true);
            LastMessage = result.Message;
            return result;
        }

        public void EndTurn()
        {
            if (State == null || State.IsFinished) return;
            GameState working = State.Copy();
            try
            {
                ResolveTurn(working);
                ValidateInvariants(working);
                State = working;
                LastMessage = "Ход рассчитан.";
            }
            catch (Exception exception)
            {
                LastMessage = "Ход не сохранён: " + exception.Message;
                AppendLog(State, "Внутренняя проверка остановила ход; последнее состояние сохранено.");
            }
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
                if (State.Units[i].OwnerId == civilizationId) total += State.Units[i].PopulationTenths;
            CivilizationState civ = State.Civilization(civilizationId);
            if (civ != null) total += civ.RefugeePopulationTenths;
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
                    return MakePeace(state, actor, command.TargetId, playerFacing);
                case CommandKind.SetTax:
                    actor.TaxRate = (TaxRate)command.Value;
                    AppendLog(state, actor.Name + " изменяет налоговую ставку.");
                    return CommandResult.Ok("Налоговая ставка обновлена.");
                case CommandKind.SetSpending:
                    return SetSpending(city, command.Value, command.TargetId, playerFacing);
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
            if (next > city.WorkerCount) return CommandResult.Reject("too_many_workers", "В городе нет столько свободных работников.");
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
                if (eraKnowledge < 4) return CommandResult.Reject("era_technologies", "Чтобы перейти дальше, изучите минимум четыре технологии текущей эпохи.");
                if (actor.InfluenceTenths < 100) return CommandResult.Reject("era_influence", "Переход требует 10 влияния на общие институты.");
                actor.InfluenceTenths -= 100;
                city.Project = new CityProject { Kind = ProjectKind.EraTransition, TargetId = "era_transition", Cost = 18 + (int)actor.Era * 6 };
                if (feedback) AppendLog(state, actor.Name + ": начат общественный переход в следующую эпоху.");
                return CommandResult.Ok("Переход эпохи начат.");
            }
            BuildingDefinition building = RulesCatalog.Building(targetId);
            if (building == null) return CommandResult.Reject("unknown_building", "Такого здания нет в каталоге.");
            if (building.RequiredTechnology != null && !actor.Knows(building.RequiredTechnology)) return CommandResult.Reject("technology_locked", "Сначала изучите технологию: " + building.RequiredTechnology + ".");
            if (building.UniqueEmpireBuilding)
            {
                for (int i = 0; i < state.Cities.Length; i++)
                    if (state.Cities[i].OwnerId == actor.Id && state.Cities[i].BuildingLevel(targetId) > 0)
                        return CommandResult.Reject("unique_building", "Это уникальное здание уже построено.");
            }
            if (city.BuildingLevel(targetId) > 0 && targetId != "house")
                return CommandResult.Reject("already_built", "Это здание уже построено в городе.");
            if (city.Project != null && city.Project.TargetId == targetId && city.Project.Kind == ProjectKind.Building)
                return CommandResult.Ok("Проект уже выбран.");
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
            city.Project = new CityProject { Kind = ProjectKind.Building, TargetId = targetId, Cost = building.ProductionCost, Progress = carryover };
            if (feedback) AppendLog(state, city.Name + ": начат проект «" + building.NameRu + "».");
            return CommandResult.Ok("Проект начат.");
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
            if (HexGrid.Distance(unit.Q, unit.R, q, r) != 1) return CommandResult.Reject("not_adjacent", "За один приказ отряд движется на соседний гекс.");
            HexCell destination = state.Cell(q, r);
            if (destination == null || destination.Terrain == TerrainKind.Mountain || destination.Terrain == TerrainKind.Water)
                return CommandResult.Reject("impassable", "На эту клетку нельзя пройти.");
            int movementCost = destination.Terrain == TerrainKind.Hills || destination.Terrain == TerrainKind.Swamp ? 2 : 1;
            if (destination.HasRoad && destination.Damage < 75) movementCost = 1;
            if (unit.MovementLeft < movementCost) return CommandResult.Reject("movement_spent", "На этот ход у отряда не осталось очков движения.");
            if (destination.OwnerId >= 0 && destination.OwnerId != actor.Id && !AtWar(state, actor.Id, destination.OwnerId) && !HasTreaty(state, TreatyKind.OpenBorders, actor.Id, destination.OwnerId))
                return CommandResult.Reject("closed_border", "Нужны открытые границы или состояние войны.");
            UnitState defender = FindUnitAt(state, q, r, actor.Id);
            if (defender != null)
            {
                if (!AtWar(state, actor.Id, defender.OwnerId)) return CommandResult.Reject("unit_blocked", "На клетке стоит отряд другой цивилизации.");
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
                if (feedback) AppendLog(state, city.Name + ": открыт набор поселенца; 6 еды внесены в обоз.");
                return CommandResult.Ok("Поселенец поставлен в очередь.");
            }
            if (definition.RequiredTechnology != null && !actor.Knows(definition.RequiredTechnology)) return CommandResult.Reject("unit_technology", "Сначала изучите требуемую технологию.");
            if (definition.RequiredBuilding != null && city.BuildingLevel(definition.RequiredBuilding) <= 0) return CommandResult.Reject("unit_building", "Для этого отряда нужны казармы.");
            if (city.PopulationTenths < definition.PopulationTenths + 40) return CommandResult.Reject("unit_population", "Нужно оставить городу не менее 4 населения.");
            if (actor.TreasuryTenths < definition.TreasuryCostTenths) return CommandResult.Reject("unit_treasury", "Не хватает казны для начала набора.");
            actor.TreasuryTenths -= definition.TreasuryCostTenths;
            city.ReservedPopulationTenths += definition.PopulationTenths;
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
            if (feedback) AppendLog(state, city.Name + ": мобилизованы " + (definition.PopulationTenths / 10.0).ToString("0.0") + " работника. Набор займёт минимум 2 хода.");
            return CommandResult.Ok("Набор начат.");
        }

        private CommandResult DeclareWar(GameState state, CivilizationState actor, int targetId, int q, int r, string legalGoal, bool feedback)
        {
            CivilizationState target = state.Civilization(targetId);
            if (target == null || targetId == actor.Id || target.IsDefeated) return CommandResult.Reject("invalid_target", "Невозможно объявить войну этой стороне.");
            if (AtWar(state, actor.Id, targetId)) return CommandResult.Reject("already_at_war", "Война с этой стороной уже идёт.");
            if (actor.TreasuryTenths < 80) return CommandResult.Reject("war_budget", "Для мобилизации требуется 8 казны.");
            if (string.IsNullOrEmpty(legalGoal)) return CommandResult.Reject("war_goal", "Перед объявлением войны выберите законную цель.");
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
            if (feedback) AppendLog(state, actor.Name + " объявила кризисную мобилизацию против цивилизации «" + target.Name + "». Цель: " + legalGoal + ".");
            return CommandResult.Ok("Кризис начался. У стороны есть один ход на ответ и посредничество.");
        }

        private CommandResult ProposeTreaty(GameState state, CivilizationState actor, int targetId, TreatyKind kind, int cityA, int cityB, bool feedback)
        {
            CivilizationState target = state.Civilization(targetId);
            if (target == null || target.Id == actor.Id || target.IsDefeated) return CommandResult.Reject("invalid_target", "Сторона для договора недоступна.");
            if (AtWar(state, actor.Id, targetId) && kind != TreatyKind.Peace) return CommandResult.Reject("war_treaty_only", "Во время войны можно предложить только перемирие.");
            int cost = TreatyCost(kind);
            if (actor.InfluenceTenths < cost) return CommandResult.Reject("insufficient_influence", "Не хватает влияния.");
            if (kind == TreatyKind.TradeRoute)
            {
                CityState first = state.City(cityA);
                CityState second = state.City(cityB);
                if (first == null || second == null || first.OwnerId != actor.Id || second.OwnerId != targetId)
                    return CommandResult.Reject("route_cities", "Маршруту нужны ваши города и город партнёра.");
                if (!RouteExists(state, first, second)) return CommandResult.Reject("route_blocked", "Между городами нет безопасного пути.");
                if (CountRoutes(state, actor.Id) >= TradeSlots(state, actor.Id)) return CommandResult.Reject("route_slots", "Нет свободного торгового слота.");
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
                TreasuryPerTurnTenths = kind == TreatyKind.TradeRoute ? 20 : 0,
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
                treaty.Active = true;
                treaty.Pending = false;
                ApplyTreatyEffect(state, treaty);
                if (feedback) AppendLog(state, actor.Name + " принимает договор «" + TreatyName(treaty.Kind) + "».");
                return CommandResult.Ok("Договор принят.");
            }
            return CommandResult.Reject("treaty_missing", "Нет такого предложения.");
        }

        private CommandResult MakePeace(GameState state, CivilizationState actor, int targetId, bool feedback)
        {
            ConflictState conflict = FindConflict(state, actor.Id, targetId);
            if (conflict == null || conflict.Phase < 0 || conflict.Phase > 2)
                return CommandResult.Reject("no_active_conflict", "Активного конфликта нет.");
            if (actor.TreasuryTenths >= 50) actor.TreasuryTenths -= 50;
            else actor.Reputation = Math.Max(0, actor.Reputation - 8);
            conflict.Phase = 3;
            conflict.TruceTurns = 2;
            actor.Relations[targetId] = Math.Min(100, actor.Relations[targetId] + 5);
            if (feedback) AppendLog(state, "Подписано двухходовое перемирие. Затем начнутся пять ходов восстановления.");
            return CommandResult.Ok("Перемирие заключено.");
        }

        private CommandResult SetSpending(CityState city, int service, int funding, bool feedback)
        {
            if (city == null || service < 0 || service >= city.ServiceFunding.Length || funding < 0 || funding > 2)
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
            switch (activeEvent.Kind)
            {
                case EventKind.Drought:
                    if (choice == "BuyFood" && actor.TreasuryTenths >= 50) { actor.TreasuryTenths -= 50; if (city != null) city.FoodTenths += 40; }
                    else if (choice == "ReassignWorkers" && city != null) { Array.Clear(city.Workers, 0, city.Workers.Length); city.Workers[(int)WorkerRole.Farmer] = Math.Min(4, city.WorkerCount); }
                    else if (choice == "Rationing" && city != null) { city.Trust = Math.Max(0, city.Trust - 5); city.FoodShortageTurns = Math.Max(0, city.FoodShortageTurns - 1); }
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.Epidemic:
                    if (choice == "OpenClinic" && actor.TreasuryTenths >= 100) { actor.TreasuryTenths -= 100; if (city != null) city.Health = Math.Min(100, city.Health + 10); }
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
                    else if (choice == "ExchangeLand" && actor.InfluenceTenths >= 20) { actor.InfluenceTenths -= 20; actor.Relations[activeEvent.OtherCivilizationId] = Math.Min(100, actor.Relations[activeEvent.OtherCivilizationId] + 5); }
                    else if (choice == "Ultimatum") actor.Relations[activeEvent.OtherCivilizationId] = Math.Max(-100, actor.Relations[activeEvent.OtherCivilizationId] - 10);
                    else return CommandResult.Reject("event_choice_invalid", "Этот ответ сейчас недоступен.");
                    break;
                case EventKind.TradeShortage:
                    if (choice == "EmergencyStock" && actor.TreasuryTenths >= 60) { actor.TreasuryTenths -= 60; if (city != null) city.FoodTenths += 30; }
                    else if (choice == "NewRoute") actor.Reputation = Math.Min(100, actor.Reputation + 1);
                    else if (choice == "DomesticSupply" && city != null) city.Project = new CityProject { Kind = ProjectKind.Building, TargetId = "granary", Cost = 10 };
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
                    else if (choice == "LocalTruce") { for (int i = 0; i < state.Conflicts.Length; i++) if (state.Conflicts[i].Active && state.Conflicts[i].AggressorId == actor.Id) state.Conflicts[i].Phase = 3; }
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
            return CommandResult.Ok("Решение события применено.");
        }

        private CommandResult ContributeToCrisis(GameState state, CivilizationState actor, string resource, int amount, bool feedback)
        {
            if (state.Crisis == null || state.Crisis.Kind == CrisisKind.None || state.Crisis.Resolved)
                return CommandResult.Reject("crisis_missing", "Сейчас нет активного глобального кризиса.");
            int contribution = Math.Max(1, amount);
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
            return CommandResult.Ok("Вклад принят. Угроза кризиса: " + state.Crisis.Threat + ".");
        }

        private CommandResult BeginVictoryProject(GameState state, CivilizationState actor, CityState city, VictoryPath path, bool feedback)
        {
            if (city == null || city.OwnerId != actor.Id) return CommandResult.Reject("city_not_owned", "Проекту нужен свой город.");
            if (city.Project != null && city.Project.Kind != ProjectKind.None) return CommandResult.Reject("project_busy", "Сначала завершите или отмените текущий проект города.");
            if (path == VictoryPath.Scientific)
            {
                if (!actor.Knows("computing_networks") || !actor.Knows("open_science")) return CommandResult.Reject("science_prerequisites", "Изучите «Вычислительные сети» и «Открытую науку».");
                if (CountOwnedBuilding(state, actor.Id, "university") < 2 || CountOwnedBuilding(state, actor.Id, "academy") < 1) return CommandResult.Reject("science_buildings", "Нужны два университета и одна академия.");
                if (actor.KnowledgeTenths < 300 || actor.InfluenceTenths < 80) return CommandResult.Reject("science_resources", "Нужно 30 знаний и 8 влияния.");
                actor.KnowledgeTenths -= 300;
                actor.InfluenceTenths -= 80;
                actor.ScienceProjectTurns = 0;
                city.Project = new CityProject { Kind = ProjectKind.ScientificProject, TargetId = "common_knowledge", Cost = 20, Progress = 0 };
            }
            else if (path == VictoryPath.Cultural)
            {
                if (actor.CulturePrestige < 100) return CommandResult.Reject("culture_prestige", "Нужно накопить 100 культурного престижа.");
                if (actor.InfluenceTenths < 200 || actor.TreasuryTenths < 100) return CommandResult.Reject("festival_resources", "Фестиваль стоит 20 влияния и 10 казны.");
                actor.InfluenceTenths -= 200;
                actor.TreasuryTenths -= 100;
                actor.CultureFestivalTurns = 2;
            }
            else if (path == VictoryPath.Diplomatic)
            {
                if (!actor.Knows("international_assembly") || city.BuildingLevel("assembly") < 1) return CommandResult.Reject("assembly_required", "Нужны технология и здание Международной ассамблеи.");
                if (!state.Crisis.Resolved || CountRoutes(state, actor.Id) < 2) return CommandResult.Reject("diplomacy_requirements", "Нужны два торговых контакта и разрешённый глобальный кризис.");
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
