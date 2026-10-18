using System;
using System.Collections.Generic;

namespace Nasledie.Core
{
    public sealed partial class GameSimulation
    {
        private void ResolveBattle(GameState state, UnitState attacker, UnitState defender, HexCell cell, bool playerFacing)
        {
            if (attacker == null || defender == null) return;
            state.BattlesThisTurn++;
            int attackerOwnerId = attacker.OwnerId;
            int defenderOwnerId = defender.OwnerId;
            UnitKind attackerKind = attacker.Kind;
            UnitKind defenderKind = defender.Kind;
            int visibilityMask = (cell.VisibleToMask & (1 << state.HumanCivilizationId)) != 0
                ? 1 << state.HumanCivilizationId : 0;
            int attackPower = BattlePower(attacker);
            int defensePower = BattlePower(defender);
            int terrainBonus = cell.Terrain == TerrainKind.Hills || cell.Terrain == TerrainKind.Forest ? 15 : 0;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].Q == cell.Q && state.Cities[i].R == cell.R) terrainBonus += 10 + state.Cities[i].BuildingLevel("barracks") * 10;
            defensePower = defensePower * (100 + terrainBonus) / 100;
            int attackLoss = Math.Max(8, attacker.Strength * (defensePower >= attackPower ? 35 : 22) / 100);
            int defenseLoss = Math.Max(8, defender.Strength * (attackPower >= defensePower ? 42 : 26) / 100);
            attacker.Strength = Math.Max(0, attacker.Strength - attackLoss);
            defender.Strength = Math.Max(0, defender.Strength - defenseLoss);
            attacker.Readiness = Math.Max(30, attacker.Readiness - 25);
            defender.Readiness = Math.Max(30, defender.Readiness - 20);
            attacker.MovementLeft = 0;
            defender.MovementLeft = 0;
            CivilizationState attackerCiv = state.Civilization(attacker.OwnerId);
            CivilizationState defenderCiv = state.Civilization(defender.OwnerId);
            bool won = attacker.Strength > 0 && (defender.Strength == 0 || attackPower > defensePower * 12 / 10);
            if (battleCapture != null)
                battleCapture.Add(new BattleResolutionEvent
                {
                    AttackerId = attacker.Id,
                    DefenderId = defender.Id,
                    AttackerOwnerId = attackerOwnerId,
                    DefenderOwnerId = defenderOwnerId,
                    AttackerKind = attackerKind,
                    DefenderKind = defenderKind,
                    CellQ = cell.Q,
                    CellR = cell.R,
                    AttackerLoss = attackLoss,
                    DefenderLoss = defenseLoss,
                    HumanVisibilityMask = visibilityMask,
                    AttackerWon = won,
                    PlayerFacing = playerFacing
                });
            string result;
            if (won)
            {
                attacker.Q = cell.Q;
                attacker.R = cell.R;
                RemoveUnit(state, defender.Id);
                result = attackerCiv.Name + " одержала дорогую тактическую победу над отрядом " + defenderCiv.Name + ".";
                DamageNearbyInfrastructure(state, cell.Q, cell.R, attacker.OwnerId);
            }
            else
            {
                if (attacker.Strength == 0) RemoveUnit(state, attacker.Id);
                result = defenderCiv.Name + " удержала позицию; обе стороны понесли потери.";
            }
            if (attackerCiv != null) { attackerCiv.WarStrain = Math.Min(100, attackerCiv.WarStrain + 8); attackerCiv.Reputation = Math.Max(0, attackerCiv.Reputation - 1); }
            if (defenderCiv != null) { defenderCiv.WarStrain = Math.Min(100, defenderCiv.WarStrain + 5); defenderCiv.Reputation = Math.Max(0, defenderCiv.Reputation - 1); }
            AppendLog(state, "Сражение: " + result + " Потери сил: " + attackLoss + " / " + defenseLoss + ". Боеспособность и готовность снижены.");
            GameFactory.RebuildVisibility(state);
        }

        private int BattlePower(UnitState unit)
        {
            return Math.Max(1, unit.Strength) * Math.Max(30, unit.Readiness) * Math.Max(3000, unit.SupplyBasisPoints) / 1000000;
        }

        private static void RemoveUnit(GameState state, int id)
        {
            for (int i = 0; i < state.Units.Length; i++)
            {
                if (state.Units[i].Id != id) continue;
                var remaining = new UnitState[state.Units.Length - 1];
                if (i > 0) Array.Copy(state.Units, 0, remaining, 0, i);
                if (i < state.Units.Length - 1) Array.Copy(state.Units, i + 1, remaining, i, state.Units.Length - i - 1);
                state.Units = remaining;
                return;
            }
        }

        private static void DamageNearbyInfrastructure(GameState state, int q, int r, int attackerOwner)
        {
            HexCell cell = state.Cell(q, r);
            if (cell != null && cell.OwnerId != attackerOwner && cell.Improvement != ImprovementKind.None)
            {
                cell.Damage = Math.Min(100, cell.Damage + 25);
                if (cell.Damage >= 75)
                {
                    cell.Improvement = ImprovementKind.None;
                    cell.ImprovementLevel = 0;
                }
            }
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != attackerOwner && HexGrid.Distance(q, r, city.Q, city.R) <= 1)
                {
                    city.WarDamageTurns = Math.Max(city.WarDamageTurns, 3);
                    city.Trust = Math.Max(0, city.Trust - 2);
                }
            }
        }

        private void ResolveConflictsAndDiplomacy(GameState state, List<string> lines)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                CivilizationState from = state.Civilization(treaty.FromCivilizationId);
                CivilizationState to = state.Civilization(treaty.ToCivilizationId);
                if (from == null || to == null) { MarkTreatyBroken(state, treaty); treaty.Active = false; treaty.Pending = false; continue; }
                if (treaty.Pending && !to.IsHuman)
                {
                    int acceptance = TradeAcceptance(from, to, treaty.Kind);
                    if (acceptance >= 25) { treaty.Pending = false; treaty.Active = true; ApplyTreatyEffect(state, treaty); lines.Add(to.Name + " одобрила договор «" + TreatyName(treaty.Kind) + "»."); }
                    else { treaty.Pending = false; lines.Add(to.Name + " отклонила предложение «" + TreatyName(treaty.Kind) + "»."); }
                }
                if (treaty.Active && treaty.TurnsRemaining > 0)
                {
                    treaty.TurnsRemaining--;
                    if (treaty.TurnsRemaining == 0)
                    {
                        treaty.Active = false;
                        treaty.Fulfilled = true;
                        lines.Add("Истёк срок договора «" + TreatyName(treaty.Kind) + "» между " + from.Name + " и " + to.Name + ".");
                    }
                }
            }

            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (!conflict.Active) continue;
                CivilizationState aggressor = state.Civilization(conflict.AggressorId);
                CivilizationState defender = state.Civilization(conflict.DefenderId);
                if (aggressor == null || defender == null || aggressor.IsDefeated || defender.IsDefeated) { conflict.Active = false; continue; }
                if (conflict.Phase == 0)
                {
                    conflict.Phase = 1;
                    if (aggressor.Id >= 0 && aggressor.Id < aggressor.Trusts.Length)
                    {
                        aggressor.Trusts[defender.Id] = Math.Max(0, aggressor.Trusts[defender.Id] - 5);
                        aggressor.WarStrain = Math.Min(100, aggressor.WarStrain + 10);
                    }
                    lines.Add("Начался международный кризис мобилизации. Посредничество ещё возможно до начала боёв.");
                    continue;
                }
                if (conflict.Phase == 1)
                {
                    int globalReaction = 0;
                    for (int c = 0; c < state.Civilizations.Length; c++)
                        if (c != aggressor.Id && c != defender.Id) globalReaction += Math.Max(0, state.Civilizations[c].Reputation - aggressor.Reputation) / 2;
                    aggressor.Reputation = Math.Max(0, aggressor.Reputation - 10);
                    aggressor.WarStrain = Math.Min(100, aggressor.WarStrain + 15);
                    for (int c = 0; c < state.Civilizations.Length; c++)
                    {
                        if (c == aggressor.Id || c == defender.Id) continue;
                        aggressor.Relations[c] = Math.Max(-100, aggressor.Relations[c] - 12 - globalReaction / 4);
                        state.Civilizations[c].Threats[aggressor.Id] = Math.Min(100, state.Civilizations[c].Threats[aggressor.Id] + 15);
                    }
                    conflict.Phase = 2;
                    conflict.WarTurn = 0;
                    CloseTradeRoutes(state, aggressor.Id, defender.Id);
                    CloseTradeRoutes(state, defender.Id, aggressor.Id);
                    ApplyWarShock(state, aggressor.Id);
                    ApplyWarShock(state, defender.Id);
                    lines.Add("Переговоры провалились: началась война. Торговые связи разорваны, мобилизация бьёт по доверию и экономике.");
                    continue;
                }
                if (conflict.Phase == 2)
                {
                    conflict.WarTurn++;
                    aggressor.WarStrain = Math.Min(100, aggressor.WarStrain + 5);
                    defender.WarStrain = Math.Min(100, defender.WarStrain + 4);
                    ApplyWarTurnDamage(state, conflict);
                    int occupationHold = 0;
                    for (int c = 0; c < state.Cities.Length; c++)
                        if (state.Cities[c].OwnerId == defender.Id && state.Cities[c].OccupiedById == aggressor.Id) occupationHold++;
                    if (occupationHold > 0) conflict.OccupationCityId = FirstOccupiedCity(state, defender.Id, aggressor.Id);
                    if (occupationHold > 0 && conflict.WarTurn >= 3)
                    {
                        conflict.Phase = 3;
                        conflict.TruceTurns = 2;
                        aggressor.Reputation = Math.Max(0, aggressor.Reputation - 8);
                        lines.Add("Международное давление вынуждает стороны к перемирию после оккупации. Города требуют восстановления.");
                    }
                    else if (conflict.WarTurn >= 6)
                    {
                        conflict.Phase = 3;
                        conflict.TruceTurns = 2;
                        aggressor.Reputation = Math.Max(0, aggressor.Reputation - 8);
                        lines.Add("Истощение и дипломатическое давление остановили войну. Ни одна сторона не получила надёжной выгоды.");
                    }
                    continue;
                }
                if (conflict.Phase == 3)
                {
                    conflict.TruceTurns--;
                    if (conflict.TruceTurns <= 0)
                    {
                        conflict.Phase = 4;
                        conflict.RecoveryTurns = 5;
                        ReleaseOccupation(state, conflict);
                        lines.Add("Перемирие завершилось. Начались пять ходов обязательного восстановления.");
                    }
                    continue;
                }
                if (conflict.Phase == 4)
                {
                    conflict.RecoveryTurns--;
                    if (conflict.RecoveryTurns <= 0)
                    {
                        conflict.Active = false;
                        aggressor.WarStrain = Math.Max(0, aggressor.WarStrain - 25);
                        defender.WarStrain = Math.Max(0, defender.WarStrain - 20);
                        lines.Add("Восстановление после войны завершено. Дипломатические отношения можно начинать заново.");
                    }
                }
            }

            PlanAiWarOrders(state, lines);
        }

        private static void ApplyWarShock(GameState state, int civilizationId)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civilizationId) continue;
                city.WarDamageTurns = Math.Max(city.WarDamageTurns, 2);
                city.Trust = Math.Max(0, city.Trust - 5);
                city.Attraction = Math.Max(0, city.Attraction - 5);
            }
        }

        private static void ApplyWarTurnDamage(GameState state, ConflictState conflict)
        {
            int[] sides = { conflict.AggressorId, conflict.DefenderId };
            for (int s = 0; s < sides.Length; s++)
            {
                int owner = sides[s];
                for (int c = 0; c < state.Cities.Length; c++)
                {
                    CityState city = state.Cities[c];
                    if (city.OwnerId != owner) continue;
                    city.WarStrain = Math.Min(100, city.WarStrain + 3);
                    if (conflict.WarTurn % 2 == 0)
                    {
                        city.Trust = Math.Max(0, city.Trust - 1);
                        city.Attraction = Math.Max(0, city.Attraction - 1);
                    }
                }
            }
        }

        private static int FirstOccupiedCity(GameState state, int owner, int occupier)
        {
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == owner && state.Cities[i].OccupiedById == occupier) return state.Cities[i].Id;
            return -1;
        }

        private static void ReleaseOccupation(GameState state, ConflictState conflict)
        {
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OccupiedById == conflict.AggressorId || state.Cities[i].OccupiedById == conflict.DefenderId)
                {
                    state.Cities[i].OccupiedById = -1;
                    state.Cities[i].IntegrationTurns = 0;
                }
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.OwnerId != conflict.AggressorId && unit.OwnerId != conflict.DefenderId) continue;
                HexCell occupied = state.Cell(unit.Q, unit.R);
                if (occupied == null || occupied.OwnerId < 0 || occupied.OwnerId == unit.OwnerId) continue;
                HexCell retreat = null;
                int bestDistance = int.MaxValue;
                for (int c = 0; c < state.Cells.Length; c++)
                {
                    HexCell candidate = state.Cells[c];
                    if (candidate.OwnerId != unit.OwnerId || candidate.Terrain == TerrainKind.Water || candidate.Terrain == TerrainKind.Mountain) continue;
                    int distance = HexGrid.Distance(unit.Q, unit.R, candidate.Q, candidate.R);
                    if (distance < bestDistance) { bestDistance = distance; retreat = candidate; }
                }
                if (retreat != null) { unit.Q = retreat.Q; unit.R = retreat.R; unit.MovementLeft = 0; }
            }
        }

        private void PlanAiWarOrders(GameState state, List<string> lines)
        {
            for (int c = 0; c < state.Civilizations.Length; c++)
            {
                CivilizationState civ = state.Civilizations[c];
                if (civ.IsHuman || civ.IsDefeated) continue;
                for (int f = 0; f < state.Conflicts.Length; f++)
                {
                    ConflictState conflict = state.Conflicts[f];
                    if (!conflict.Active || conflict.Phase != 2 || (conflict.AggressorId != civ.Id && conflict.DefenderId != civ.Id)) continue;
                    int targetOwner = conflict.AggressorId == civ.Id ? conflict.DefenderId : conflict.AggressorId;
                    CityState targetCity = null;
                    if (conflict.AggressorId == civ.Id) targetCity = state.City(conflict.OccupationCityId);
                    if (targetCity == null)
                    {
                        for (int j = 0; j < state.Cities.Length; j++)
                            if (state.Cities[j].OwnerId == targetOwner && (targetCity == null || state.Cities[j].AdministrativeLoad > targetCity.AdministrativeLoad)) targetCity = state.Cities[j];
                    }
                    if (targetCity == null) continue;
                    for (int u = 0; u < state.Units.Length; u++)
                    {
                        UnitState unit = state.Units[u];
                        if (unit.OwnerId != civ.Id || unit.Kind == UnitKind.SettlerWagon || unit.Strength < 20) continue;
                        int before = HexGrid.Distance(unit.Q, unit.R, targetCity.Q, targetCity.R);
                        if (before <= 1) continue;
                        HexCell best = null;
                        int bestDistance = before;
                        for (int d = 0; d < 6; d++)
                        {
                            int q = unit.Q + HexGrid.Directions[d, 0];
                            int r = unit.R + HexGrid.Directions[d, 1];
                            HexCell cell = state.Cell(q, r);
                            if (cell == null || cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Water ||
                                (cell.OwnerId >= 0 && cell.OwnerId != civ.Id && cell.OwnerId != targetOwner && !HasTreaty(state, TreatyKind.OpenBorders, civ.Id, cell.OwnerId))) continue;
                            int distance = HexGrid.Distance(q, r, targetCity.Q, targetCity.R);
                            if (distance < bestDistance) { bestDistance = distance; best = cell; }
                        }
                        if (best != null) MoveUnit(state, civ, unit.Id, best.Q, best.R, false);
                    }
                }
            }
        }

        private void ResolveEventsAndCrisis(GameState state, List<string> lines)
        {
            ResolveGlobalCrisis(state, lines);
            QueuePlayerEvent(state, lines);
        }

        private void ResolveGlobalCrisis(GameState state, List<string> lines)
        {
            if ((state.Crisis.Kind == CrisisKind.None || state.Crisis.Resolved) && GamePacing.IsGlobalCrisisTurn(state.Pace, state.Turn))
            {
                CrisisKind kind = CrisisKind.FoodShortage;
                int lowFood = 0;
                for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].FoodTenths < state.Cities[i].PopulationTenths * 2) lowFood++;
                if (lowFood >= 2) kind = CrisisKind.FoodShortage;
                else if (state.Turn % 24 == 4) kind = CrisisKind.Epidemic;
                else kind = CrisisKind.TradeCollapse;
                state.Crisis = new CrisisState { Kind = kind, Threat = 100, TurnsActive = 0, ContributorsMask = 0, Resolved = false,
                    PublicText = kind == CrisisKind.FoodShortage ? "Запасы продовольствия во многих городах сокращаются." : kind == CrisisKind.Epidemic ? "Эпидемическая волна переходит между регионами." : "Сбой связей грозит остановить торговые маршруты." };
                lines.Add("Мировой кризис: " + state.Crisis.PublicText + " Любая цивилизация может внести вклад.");
            }
            if (state.Crisis.Kind == CrisisKind.None || state.Crisis.Resolved) return;
            state.Crisis.TurnsActive++;
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                if (civ.IsHuman || civ.IsDefeated || state.Crisis.Threat <= 0 || state.Crisis.TurnsActive < 2 ||
                    (state.Crisis.ContributorsMask & (1 << civ.Id)) != 0) continue;
                if (civ.InfluenceTenths >= 100)
                {
                    civ.InfluenceTenths -= 50;
                    state.Crisis.ContributorsMask |= 1 << civ.Id;
                    state.Crisis.Threat = Math.Max(0, state.Crisis.Threat - 20);
                    civ.Reputation = Math.Min(100, civ.Reputation + 2);
                    lines.Add(civ.Name + " внесла вклад влиянием в решение мирового кризиса.");
                }
            }
            if (state.Crisis.Threat <= 0)
            {
                state.Crisis.Resolved = true;
                state.HasResolvedGlobalCrisis = true;
                state.ResolvedGlobalCrisisContributorMask |= state.Crisis.ContributorsMask;
                lines.Add("Мировой кризис разрешён благодаря международному сотрудничеству.");
                for (int i = 0; i < state.Civilizations.Length; i++) state.Civilizations[i].Reputation = Math.Min(100, state.Civilizations[i].Reputation + 2);
                return;
            }
            state.Crisis.Threat = Math.Min(120, state.Crisis.Threat + 4);
            if (state.Crisis.TurnsActive >= 4)
            {
                for (int i = 0; i < state.Cities.Length; i++)
                {
                    CityState city = state.Cities[i];
                    if (state.Crisis.Kind == CrisisKind.FoodShortage) { city.FoodTenths = Math.Max(0, city.FoodTenths - 10); city.FoodShortageTurns++; }
                    else if (state.Crisis.Kind == CrisisKind.Epidemic) city.Health = Math.Max(10, city.Health - 4);
                    else city.EfficiencyBasisPoints = Math.Max(7000, city.EfficiencyBasisPoints - 500);
                }
                lines.Add("Кризис не удалось остановить вовремя: города понесли потери в запасах, здоровье или выпуске.");
                state.Crisis.Threat = Math.Max(35, state.Crisis.Threat - 45);
                state.Crisis.TurnsActive = 0;
            }
        }

        private void QueuePlayerEvent(GameState state, List<string> lines)
        {
            CivilizationState player = state.Civilization(state.HumanCivilizationId);
            if (player == null || player.IsDefeated || state.Turn < 3 || state.Turn % 2 != 0) return;
            for (int i = 0; i < state.Events.Length; i++) if (!state.Events[i].IsResolved && state.Events[i].TargetCivilizationId == player.Id) return;
            DeterministicRandom rng = new DeterministicRandom(unchecked((uint)(state.Seed * 1103515245 + state.Turn * 12345 + 97)));
            if (rng.Range(0, 100) >= 42) return;
            CityState city = null;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == player.Id && state.Turn - state.Cities[i].LastNegativeEventTurn >= 4) { city = state.Cities[i]; break; }
            if (city == null) return;
            int nearestRival = -1;
            int rivalDistance = int.MaxValue;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState otherCity = state.Cities[i];
                if (otherCity.OwnerId == player.Id) continue;
                int distance = HexGrid.Distance(city.Q, city.R, otherCity.Q, otherCity.R);
                if (distance < rivalDistance) { rivalDistance = distance; nearestRival = otherCity.OwnerId; }
            }
            int roadCount = 0;
            for (int i = 0; i < state.Cells.Length; i++)
                if (state.Cells[i].OwnerId == player.Id && state.Cells[i].HasRoad && HexGrid.Distance(city.Q, city.R, state.Cells[i].Q, state.Cells[i].R) <= 3) roadCount++;
            bool nearbyWar = AtWarWithAny(state, player.Id);
            int otherCivilizationId = -1;
            EventKind kind;
            if (player.WarStrain >= 60 && nearbyWar) kind = EventKind.MobilizationFatigue;
            else if (city.FoodShortageTurns > 0 || city.FoodTenths < city.PopulationTenths * 2) kind = EventKind.Drought;
            else if (city.Health < 55) kind = EventKind.Epidemic;
            else if (city.Environment < 70) kind = EventKind.Smog;
            else if (nearestRival >= 0 && rivalDistance <= 4 && player.Relations[nearestRival] < 20)
            {
                kind = EventKind.BorderDispute;
                otherCivilizationId = nearestRival;
            }
            else if (city.Trust < 45 && player.TaxRate == TaxRate.High) kind = EventKind.ProcurementCorruption;
            else if (player.CompletedTechnologies >= 5 && city.Education > 35) kind = EventKind.EducationReform;
            else if (CountRoutes(state, player.Id) > 0 && player.CulturePrestige >= 20) kind = EventKind.CulturalExchange;
            else if (player.KnowledgeTenths >= 80 && CountRoutes(state, player.Id) > 0) kind = EventKind.SharedResearch;
            else if (roadCount >= 3) kind = EventKind.RoadCongestion;
            else if (CountRoutes(state, player.Id) == 0 && HasTradeDependency(state, player.Id)) kind = EventKind.TradeShortage;
            else if (nearbyWar || AnyNeighborAtWar(state, player.Id)) kind = EventKind.RefugeeFlow;
            else kind = rng.Range(0, 3) == 0 ? EventKind.RoadCongestion : EventKind.EducationReform;
            EventDefinition definition = null;
            for (int i = 0; i < RulesCatalog.Events.Length; i++) if (RulesCatalog.Events[i].Kind == kind) definition = RulesCatalog.Events[i];
            if (definition == null) return;
            var activeEvent = new ActiveEventState
            {
                Id = state.NextEntityId++, Kind = kind, TargetCityId = city.Id, TargetCivilizationId = player.Id,
                OtherCivilizationId = otherCivilizationId, TurnsRemaining = 1, Severity = 1, Choices = (string[])definition.ChoiceIds.Clone()
            };
            Append(ref state.Events, activeEvent);
            city.LastNegativeEventTurn = state.Turn;
            lines.Add("Событие: «" + definition.NameRu + "». Выберите ответ в панели событий.");
        }

        private static bool HasTradeDependency(GameState state, int civilizationId)
        {
            CivilizationState civ = state.Civilization(civilizationId);
            if (civ == null) return false;
            for (int i = 0; i < civ.TradeDependency.Length; i++) if (civ.TradeDependency[i] > 0) return true;
            return false;
        }

        private static bool AnyNeighborAtWar(GameState state, int civilizationId)
        {
            for (int i = 0; i < state.Conflicts.Length; i++)
                if (state.Conflicts[i].Active && state.Conflicts[i].Phase == 2 &&
                    (state.Conflicts[i].AggressorId == civilizationId || state.Conflicts[i].DefenderId == civilizationId)) return true;
            return false;
        }
    }
}
