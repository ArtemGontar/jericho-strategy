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
            int terrainBonus = cell.Terrain == TerrainKind.Hills || cell.Terrain == TerrainKind.Forest ? 15 : 0;
            int fortifications = 0;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell fort = state.Cells[i];
                if (fort.OwnerId == defenderOwnerId && fort.Improvement == ImprovementKind.Fortification && fort.Damage < 75 &&
                    HexGrid.Distance(fort.Q, fort.R, defender.Q, defender.R) <= 1) fortifications++;
            }
            terrainBonus += Math.Min(30, fortifications * 15);
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || treaty.Kind != TreatyKind.Guarantee) continue;
                int protectedSide = treaty.FromCivilizationId == defenderOwnerId ? treaty.ToCivilizationId :
                    treaty.ToCivilizationId == defenderOwnerId ? treaty.FromCivilizationId : -1;
                if (protectedSide >= 0 && protectedSide != attackerOwnerId) { terrainBonus += 10; break; }
            }
            bool cityTarget = false;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].Q == cell.Q && state.Cities[i].R == cell.R)
                {
                    cityTarget = true;
                    terrainBonus += 10 + state.Cities[i].BuildingLevel("barracks") * 10;
                }
            int attackPower = BattlePower(attacker, true, cell.Terrain, cityTarget);
            int defensePower = BattlePower(defender, false, cell.Terrain, cityTarget);
            defensePower = defensePower * (100 + terrainBonus) / 100;
            int attackLoss = Math.Min(attacker.Strength, Math.Max(8, attacker.Strength * (defensePower >= attackPower ? 35 : 22) / 100));
            int defenseLoss = Math.Min(defender.Strength, Math.Max(8, defender.Strength * (attackPower >= defensePower ? 42 : 26) / 100));
            ApplyBattleCasualties(state, attacker, attackLoss, out int attackerDeaths, out int attackerWounded);
            ApplyBattleCasualties(state, defender, defenseLoss, out int defenderDeaths, out int defenderWounded);
            attacker.Strength = Math.Max(0, attacker.Strength - attackLoss);
            defender.Strength = Math.Max(0, defender.Strength - defenseLoss);
            attacker.Readiness = Math.Max(30, attacker.Readiness - 25);
            defender.Readiness = Math.Max(30, defender.Readiness - 20);
            attacker.MovementLeft = 0;
            defender.MovementLeft = 0;
            CivilizationState attackerCiv = state.Civilization(attacker.OwnerId);
            CivilizationState defenderCiv = state.Civilization(defender.OwnerId);
            bool won = attacker.Strength > 0 && (defender.Strength == 0 || attackPower > defensePower * 12 / 10);
            HexCell retreatCell = won && defender.Strength > 0 ? FindBattleRetreatCell(state, defender, cell) : null;
            bool defenderDestroyed = won && (defender.Strength <= 0 || retreatCell == null);
            int defenderPrisoners = defenderDestroyed ? CaptureMilitarySurvivors(state, defender, attackerOwnerId) : 0;
            int attackerPrisoners = !won && attacker.Strength <= 0 ? CaptureMilitarySurvivors(state, attacker, defenderOwnerId) : 0;
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
                    RetreatQ = retreatCell == null ? cell.Q : retreatCell.Q,
                    RetreatR = retreatCell == null ? cell.R : retreatCell.R,
                    AttackerLoss = attackLoss,
                    DefenderLoss = defenseLoss,
                    AttackerDeathsTenths = attackerDeaths,
                    DefenderDeathsTenths = defenderDeaths,
                    AttackerWoundedTenths = attackerWounded,
                    DefenderWoundedTenths = defenderWounded,
                    AttackerPrisonersTenths = attackerPrisoners,
                    DefenderPrisonersTenths = defenderPrisoners,
                    HumanVisibilityMask = visibilityMask,
                    AttackerWon = won,
                    DefenderRetreated = retreatCell != null,
                    DefenderDestroyed = defenderDestroyed,
                    PlayerFacing = playerFacing
                });
            string result = "";
            if (won)
            {
                attacker.Q = cell.Q;
                attacker.R = cell.R;
                if (defenderDestroyed)
                {
                    RetireMilitarySurvivors(state, defender);
                    RemoveUnit(state, defender.Id);
                }
                else
                {
                    defender.Q = retreatCell.Q;
                    defender.R = retreatCell.R;
                    result = attackerCiv.Name + " одержала дорогую тактическую победу; отряд " + defenderCiv.Name + " отступил на безопасную клетку.";
                }
                if (defenderDestroyed) result = attackerCiv.Name + " одержала дорогую тактическую победу и окружила отряд " + defenderCiv.Name + ".";
                DamageNearbyInfrastructure(state, cell.Q, cell.R, attacker.OwnerId);
            }
            else
            {
                if (attacker.Strength == 0)
                {
                    RetireMilitarySurvivors(state, attacker);
                    RemoveUnit(state, attacker.Id);
                }
                result = defenderCiv.Name + " удержала позицию; обе стороны понесли потери.";
            }
            if (attackerCiv != null) { attackerCiv.WarStrain = Math.Min(100, attackerCiv.WarStrain + 8); attackerCiv.Reputation = Math.Max(0, attackerCiv.Reputation - 1); }
            if (defenderCiv != null) { defenderCiv.WarStrain = Math.Min(100, defenderCiv.WarStrain + 5); defenderCiv.Reputation = Math.Max(0, defenderCiv.Reputation - 1); }
            AppendLog(state, "Сражение: " + result + " Потери сил: " + attackLoss + " / " + defenseLoss +
                "; погибшие: " + FormatPopulation(attackerDeaths + defenderDeaths) + "; раненые: " + FormatPopulation(attackerWounded + defenderWounded) +
                (attackerPrisoners + defenderPrisoners > 0 ? "; взято в плен: " + FormatPopulation(attackerPrisoners + defenderPrisoners) : "") +
                ". Сила и готовность отрядов снижены.");
            GameFactory.RebuildVisibility(state);
        }

        private static void ApplyBattleCasualties(GameState state, UnitState unit, int strengthLoss, out int deathsTenths, out int woundedTenths)
        {
            deathsTenths = 0;
            woundedTenths = 0;
            if (unit == null || strengthLoss <= 0 || unit.Kind == UnitKind.SettlerWagon) return;
            UnitDefinition definition = RulesCatalog.Unit(unit.Kind);
            int initialStrength = Math.Max(1, unit.InitialStrength > 0 ? unit.InitialStrength : (definition == null ? unit.Strength : definition.Strength));
            int initialPopulation = Math.Max(0, unit.InitialPopulationTenths > 0 ? unit.InitialPopulationTenths :
                (definition == null ? unit.PopulationTenths : definition.PopulationTenths));
            int totalLoss = Math.Min(unit.PopulationTenths, (initialPopulation * strengthLoss + initialStrength / 2) / initialStrength);
            if (totalLoss <= 0) return;

            CityState source = state.City(unit.CitySourceId);
            bool medicalSupport = source != null && (source.BuildingLevel("clinic") > 0 || source.BuildingLevel("hospital") > 0);
            int woundPercent = medicalSupport ? 60 : 30;
            woundedTenths = Math.Min(totalLoss, (totalLoss * woundPercent + 50) / 100);
            if (woundedTenths > 0)
            {
                unit.WoundedPopulationTenths += woundedTenths;
                int woundedStrength = (strengthLoss * woundPercent + 50) / 100;
                unit.WoundedStrength += Math.Max(1, woundedStrength);
                unit.WoundedTurns = Math.Max(unit.WoundedTurns, medicalSupport ? 3 : 5);
                unit.IsWounded = true;
            }
            unit.PopulationTenths = Math.Max(0, unit.PopulationTenths - totalLoss);
            deathsTenths = totalLoss - woundedTenths;
        }

        private static void RetireMilitarySurvivors(GameState state, UnitState unit)
        {
            if (unit == null || unit.Kind == UnitKind.SettlerWagon) return;
            int survivors = Math.Max(0, unit.PopulationTenths) + Math.Max(0, unit.WoundedPopulationTenths);
            if (survivors <= 0) return;
            CivilizationState owner = state.Civilization(unit.OwnerId);
            CityState source = state.City(unit.CitySourceId);
            if (owner == null) return;
            if (source != null && source.OwnerId == unit.OwnerId && source.OccupiedById < 0)
            {
                int returned = Math.Min(survivors, Math.Max(0, source.HousingTenths - source.PopulationTenths));
                source.PopulationTenths += returned;
                survivors -= returned;
            }
            if (survivors > 0) owner.RefugeePopulationTenths += survivors;
            unit.PopulationTenths = 0;
            unit.WoundedPopulationTenths = 0;
            unit.WoundedStrength = 0;
            unit.WoundedTurns = 0;
            unit.IsWounded = false;
        }

        private static int CaptureMilitarySurvivors(GameState state, UnitState unit, int captorId)
        {
            if (state == null || unit == null || unit.Kind == UnitKind.SettlerWagon) return 0;
            CivilizationState captor = state.Civilization(captorId);
            if (captor == null || captor.PrisonersHeldByCivilizationTenths == null || unit.OwnerId < 0 ||
                unit.OwnerId >= captor.PrisonersHeldByCivilizationTenths.Length) return 0;
            int survivors = Math.Max(0, unit.PopulationTenths) + Math.Max(0, unit.WoundedPopulationTenths);
            int captured = survivors / 2;
            if (captured <= 0) return 0;
            int healthy = Math.Min(unit.PopulationTenths, captured);
            unit.PopulationTenths -= healthy;
            int remaining = captured - healthy;
            if (remaining > 0) unit.WoundedPopulationTenths = Math.Max(0, unit.WoundedPopulationTenths - remaining);
            captor.PrisonersHeldByCivilizationTenths[unit.OwnerId] += captured;
            return captured;
        }

        private static HexCell FindBattleRetreatCell(GameState state, UnitState retreating, HexCell battleCell)
        {
            HexCell best = null;
            int bestOwnership = int.MaxValue;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell candidate = state.Cells[i];
                if (candidate == null || candidate.Terrain == TerrainKind.Water || candidate.Terrain == TerrainKind.Mountain ||
                    candidate.Q == battleCell.Q && candidate.R == battleCell.R ||
                    candidate.OwnerId != retreating.OwnerId && candidate.OwnerId >= 0) continue;
                bool occupied = false;
                for (int u = 0; u < state.Units.Length; u++)
                {
                    UnitState other = state.Units[u];
                    if (other != null && other.Id != retreating.Id && other.Q == candidate.Q && other.R == candidate.R)
                    { occupied = true; break; }
                }
                if (occupied) continue;
                int ownershipRank = candidate.OwnerId == retreating.OwnerId ? 0 : 1;
                int distance = HexGrid.Distance(candidate.Q, candidate.R, battleCell.Q, battleCell.R);
                if (ownershipRank < bestOwnership || (ownershipRank == bestOwnership &&
                    (distance < bestDistance || (distance == bestDistance && (best == null || candidate.Q < best.Q ||
                     (candidate.Q == best.Q && candidate.R < best.R))))))
                {
                    best = candidate;
                    bestOwnership = ownershipRank;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private int BattlePower(UnitState unit, bool attacking, TerrainKind terrain, bool cityTarget)
        {
            int power = Math.Max(1, unit.Strength) * Math.Max(30, unit.Readiness) * Math.Max(3000, unit.SupplyBasisPoints) / 1000000;
            UnitDefinition definition = RulesCatalog.Unit(unit.Kind);
            int roleBonus = definition == null ? 0 : attacking ? definition.AttackBonusPercent : definition.DefenseBonusPercent;
            if (definition != null && definition.Class == UnitClass.Cavalry)
            {
                if (attacking && (terrain == TerrainKind.Plains || terrain == TerrainKind.Meadow || terrain == TerrainKind.River)) roleBonus += 12;
                if (attacking && (terrain == TerrainKind.Hills || terrain == TerrainKind.Forest)) roleBonus -= 12;
            }
            if (definition != null && definition.Class == UnitClass.Siege && cityTarget)
                roleBonus += attacking ? 18 : -10;
            power = power * Math.Max(25, 100 + roleBonus) / 100;
            return unit.IronEquipped ? power * 125 / 100 : power;
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
                        if ((state.Cities[c].OwnerId == defender.Id && state.Cities[c].OccupiedById == aggressor.Id) ||
                            (state.Cities[c].OwnerId == aggressor.Id && state.Cities[c].OccupiedById == defender.Id)) occupationHold++;
                    if (occupationHold > 0)
                    {
                        int aggressorOccupation = FirstOccupiedCity(state, defender.Id, aggressor.Id);
                        conflict.OccupationCityId = aggressorOccupation >= 0 ? aggressorOccupation : FirstOccupiedCity(state, aggressor.Id, defender.Id);
                    }
                    if (occupationHold > 0 && conflict.WarTurn >= 3)
                    {
                        conflict.Phase = 3;
                        conflict.TruceTurns = 2;
                        aggressor.Reputation = Math.Max(0, aggressor.Reputation - 8);
                        lines.Add("Международное давление вынуждает стороны к перемирию после оккупации города. Города требуют восстановления.");
                    }
                    else if (conflict.WarTurn >= 8 && !conflict.MediationOffered)
                    {
                        conflict.MediationOffered = true;
                        if (aggressor.IsHuman || defender.IsHuman)
                        {
                            int humanId = state.HumanCivilizationId;
                            int otherId = aggressor.IsHuman ? defender.Id : aggressor.Id;
                            Append(ref state.Events, new ActiveEventState
                            {
                                Id = state.NextEntityId++, Kind = EventKind.NeutralMediation,
                                TargetCivilizationId = humanId, OtherCivilizationId = otherId, TurnsRemaining = 1,
                                Severity = 1, Choices = new[] { "AcceptMediation", "RejectMediation" }
                            });
                            lines.Add("Восьмой ход противостояния без захвата городов: нейтральный посредник направил сторонам предложение.");
                        }
                        else if (aggressor.WarStrain >= 40 && defender.WarStrain >= 40)
                        {
                            conflict.Phase = 3;
                            conflict.TruceTurns = 2;
                            ReleaseOccupation(state, conflict);
                            lines.Add("Нейтральное посредничество убедило истощённые стороны принять перемирие.");
                        }
                        else lines.Add("Нейтральный посредник предложил перемирие, но стороны пока отказались.");
                    }
                    else if (conflict.WarTurn >= 10)
                    {
                        conflict.Phase = 3;
                        conflict.TruceTurns = 2;
                        aggressor.Reputation = Math.Max(0, aggressor.Reputation - 8);
                        lines.Add("Истощение и дипломатическое давление остановили затяжную войну. Ни одна сторона не получила надёжной выгоды.");
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
                    if (conflict.AggressorId == civ.Id)
                    {
                        targetCity = state.City(conflict.OccupationCityId);
                        if (targetCity != null && !IsVisibleToCivilization(state.Cell(targetCity.Q, targetCity.R), civ.Id)) targetCity = null;
                    }
                    if (targetCity == null)
                    {
                        for (int j = 0; j < state.Cities.Length; j++)
                            if (state.Cities[j].OwnerId == targetOwner &&
                                IsVisibleToCivilization(state.Cell(state.Cities[j].Q, state.Cities[j].R), civ.Id) &&
                                (targetCity == null || state.Cities[j].AdministrativeLoad > targetCity.AdministrativeLoad)) targetCity = state.Cities[j];
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
                                MovementCost(cell) > unit.MovementLeft ||
                                (cell.OwnerId >= 0 && cell.OwnerId != civ.Id && cell.OwnerId != targetOwner && !HasTreaty(state, TreatyKind.OpenBorders, civ.Id, cell.OwnerId))) continue;
                            int distance = HexGrid.Distance(q, r, targetCity.Q, targetCity.R);
                            if (distance < bestDistance) { bestDistance = distance; best = cell; }
                        }
                        if (best != null) MoveUnit(state, civ, unit.Id, best.Q, best.R, false);
                    }
                }
            }
            PlanAiWarInitiative(state, lines);
        }

        private void PlanAiWarInitiative(GameState state, List<string> lines)
        {
            if (state.Turn < 16) return;
            for (int c = 0; c < state.Civilizations.Length; c++)
            {
                CivilizationState aggressor = state.Civilizations[c];
                if (aggressor.IsHuman || aggressor.IsDefeated || aggressor.WarsStarted > 0 ||
                    (aggressor.Difficulty != Difficulty.Strong && aggressor.Difficulty != Difficulty.Trial) ||
                    aggressor.TreasuryTenths < 120 || aggressor.Reputation < 30 || aggressor.WarStrain >= 40 ||
                    AtWarWithAny(state, aggressor.Id) ||
                    (aggressor.Difficulty == Difficulty.Trial ? state.Turn % 4 != aggressor.Id % 4 : state.Turn % 6 != aggressor.Id % 6)) continue;
                int ownStrength = MilitaryStrength(state, aggressor.Id);
                int minimumStrength = 70;
                if (ownStrength < minimumStrength) continue;

                CivilizationState chosenTarget = null;
                CityState chosenTargetCity = null;
                HexCell chosenClaim = null;
                int bestScore = int.MinValue;
                for (int t = 0; t < state.Civilizations.Length; t++)
                {
                    CivilizationState target = state.Civilizations[t];
                    if (target.Id == aggressor.Id || target.IsDefeated || AtWarWithAny(state, target.Id) ||
                        HasTreaty(state, TreatyKind.NonAggression, aggressor.Id, target.Id) || IsProtectedByGuarantee(state, target.Id, aggressor.Id)) continue;
                    CityState aggressorCity = null;
                    CityState targetCity = null;
                    int borderDistance = int.MaxValue;
                    for (int a = 0; a < state.Cities.Length; a++)
                    {
                        CityState own = state.Cities[a];
                        if (own.OwnerId != aggressor.Id) continue;
                        for (int b = 0; b < state.Cities.Length; b++)
                        {
                            CityState rival = state.Cities[b];
                            if (rival.OwnerId != target.Id) continue;
                            HexCell rivalCityCell = state.Cell(rival.Q, rival.R);
                            if (!IsKnownToCivilization(rivalCityCell, aggressor.Id)) continue;
                            int distance = HexGrid.Distance(own.Q, own.R, rival.Q, rival.R);
                            if (distance < borderDistance) { borderDistance = distance; aggressorCity = own; targetCity = rival; }
                        }
                    }
                    if (aggressorCity == null || targetCity == null || borderDistance > 5) continue;
                    int targetStrength = ObservedMilitaryStrength(state, target.Id, aggressor.Id);
                    int hostility = Math.Max(0, -aggressor.Relations[target.Id]) + aggressor.Threats[target.Id] / 2 + target.Threats[aggressor.Id] / 3;
                    int rivalry = AiMilitaryPosture(state, aggressor.Id * 4 + target.Id);
                    if (rivalry < (aggressor.Difficulty == Difficulty.Trial ? 20 : 12)) hostility += 70;
                    if (hostility < (aggressor.Difficulty == Difficulty.Trial ? 35 : 50)) continue;
                    if (targetStrength > 0 && ownStrength * 100 < targetStrength * (aggressor.Difficulty == Difficulty.Trial ? 100 : 145)) continue;
                    if (targetStrength == 0 && hostility < 65) continue;
                    int score = hostility * 2 + ownStrength - targetStrength - borderDistance * 4;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        chosenTarget = target;
                        chosenTargetCity = targetCity;
                        chosenClaim = FindBorderClaimCell(state, aggressor.Id, target.Id, aggressor.Id);
                    }
                }
                if (chosenTarget == null) continue;
                string legalGoal = aggressor.StrategicGoal == "defense" ? "демилитаризация границы" :
                    HasTreatyRecord(state, TreatyKind.TradeRoute, aggressor.Id, chosenTarget.Id) ? "снятие торговой блокады" :
                    "защита спорной границы";
                int q = chosenClaim == null ? chosenTargetCity.Q : chosenClaim.Q;
                int r = chosenClaim == null ? chosenTargetCity.R : chosenClaim.R;
                CommandResult declaration = DeclareWar(state, aggressor, chosenTarget.Id, q, r, legalGoal, false);
                if (declaration.Accepted)
                {
                    lines.Add(aggressor.Name + " начала кризисную мобилизацию против " + chosenTarget.Name + "; заявленная цель: " + legalGoal + ".");
                    return;
                }
            }
        }

        private static bool IsProtectedByGuarantee(GameState state, int targetId, int aggressorId)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || treaty.Kind != TreatyKind.Guarantee) continue;
                int protectedId = treaty.FromCivilizationId == targetId ? treaty.ToCivilizationId :
                    treaty.ToCivilizationId == targetId ? treaty.FromCivilizationId : -1;
                if (protectedId >= 0 && protectedId != aggressorId) return true;
            }
            return false;
        }

        private static bool HasTreatyRecord(GameState state, TreatyKind kind, int first, int second)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (treaty.Kind == kind && ((treaty.FromCivilizationId == first && treaty.ToCivilizationId == second) ||
                    (treaty.FromCivilizationId == second && treaty.ToCivilizationId == first))) return true;
            }
            return false;
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
            state.Crisis.Threat = Math.Min(120, state.Crisis.Threat + 8);
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
