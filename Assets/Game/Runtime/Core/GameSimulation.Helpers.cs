using System;
using System.Collections.Generic;

namespace Nasledie.Core
{
    public sealed partial class GameSimulation
    {
        private static void NormalizeUnitPopulationData(GameState state)
        {
            if (state == null || state.Units == null) return;
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit == null) continue;
                UnitDefinition definition = RulesCatalog.Unit(unit.Kind);
                if (definition == null) continue;
                if (unit.InitialStrength <= 0) unit.InitialStrength = Math.Max(0, definition.Strength);
                if (unit.InitialPopulationTenths <= 0)
                    unit.InitialPopulationTenths = Math.Max(0, Math.Max(unit.PopulationTenths + unit.WoundedPopulationTenths, definition.PopulationTenths));
                unit.WoundedPopulationTenths = Math.Max(0, Math.Min(unit.InitialPopulationTenths, unit.WoundedPopulationTenths));
                unit.WoundedStrength = Math.Max(0, Math.Min(unit.InitialStrength, unit.WoundedStrength));
            }
        }

        private static void RecordPlayerCommand(GameState state, GameCommand command)
        {
            if (state.CommandJournal == null) state.CommandJournal = Array.Empty<string>();
            string text = command.Text ?? string.Empty;
            string secondary = command.SecondaryText ?? string.Empty;
            string entry = "turn=" + state.Turn + ";actor=" + command.ActorId + ";kind=" + command.Kind +
                ";entity=" + command.EntityId + ";target=" + command.TargetId + ";cell=" + command.Q + "," + command.R +
                ";value=" + command.Value + ";sequence=" + command.Sequence + ";text=" + text.Replace('\n', ' ').Replace(';', ',') +
                ";secondary=" + secondary.Replace('\n', ' ').Replace(';', ',');
            Array.Resize(ref state.CommandJournal, state.CommandJournal.Length + 1);
            state.CommandJournal[state.CommandJournal.Length - 1] = entry;
            if (state.CommandJournal.Length > 2000)
            {
                state.CommandJournalTruncated = true;
                string[] retained = new string[2000];
                Array.Copy(state.CommandJournal, state.CommandJournal.Length - retained.Length, retained, 0, retained.Length);
                state.CommandJournal = retained;
            }
        }

        private static T[] Append<T>(ref T[] values, T value)
        {
            if (values == null) values = Array.Empty<T>();
            Array.Resize(ref values, values.Length + 1);
            values[values.Length - 1] = value;
            return values;
        }

        private static void AppendLog(GameState state, string entry)
        {
            if (state.EventLog == null) state.EventLog = Array.Empty<string>();
            Array.Resize(ref state.EventLog, state.EventLog.Length + 1);
            state.EventLog[state.EventLog.Length - 1] = "Ход " + state.Turn + " · " + entry;
            if (state.EventLog.Length > 80)
            {
                string[] trimmed = new string[80];
                Array.Copy(state.EventLog, state.EventLog.Length - trimmed.Length, trimmed, 0, trimmed.Length);
                state.EventLog = trimmed;
            }
        }

        private static int CivilizationPopulationTenths(GameState state, int civilizationId)
        {
            int total = 0;
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == civilizationId) total += state.Cities[i].PopulationTenths;
            for (int i = 0; i < state.Units.Length; i++)
                if (state.Units[i].OwnerId == civilizationId)
                    total += state.Units[i].PopulationTenths + Math.Max(0, state.Units[i].WoundedPopulationTenths);
            CivilizationState civ = state.Civilization(civilizationId);
            if (civ != null) total += civ.RefugeePopulationTenths;
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                int[] held = state.Civilizations[i].PrisonersHeldByCivilizationTenths;
                if (held != null && civilizationId >= 0 && civilizationId < held.Length) total += Math.Max(0, held[civilizationId]);
            }
            return total;
        }

        private static void ValidateInvariants(GameState state)
        {
            if (state.Civilizations == null || state.Civilizations.Length == 0) throw new InvalidOperationException("Нет цивилизаций.");
            if (state.Cities == null || state.Cells == null || state.Units == null) throw new InvalidOperationException("Повреждена карта или список сущностей.");
            if (state.HumanCivilizationId < 0 || state.HumanCivilizationId >= state.Civilizations.Length) throw new InvalidOperationException("Игрок не найден.");
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell == null) throw new InvalidOperationException("На карте есть пустая клетка.");
                cell.DistrictBuildingId = cell.DistrictBuildingId ?? "";
                if (string.IsNullOrEmpty(cell.DistrictBuildingId))
                {
                    cell.DistrictCityId = -1;
                    cell.DistrictLevel = 0;
                    continue;
                }
                CityState districtCity = state.City(cell.DistrictCityId);
                if (districtCity == null || RulesCatalog.Building(cell.DistrictBuildingId) == null)
                    throw new InvalidOperationException("У городского района нет действующего города или здания.");
                cell.DistrictLevel = Math.Max(1, districtCity.BuildingLevel(cell.DistrictBuildingId));
            }
            var entityIds = new HashSet<int>();
            int maximumEntityId = 0;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city == null || city.Id <= 0 || !entityIds.Add(city.Id)) throw new InvalidOperationException("Повторяется идентификатор города.");
                maximumEntityId = Math.Max(maximumEntityId, city.Id);
                if (city.Project == null) city.Project = new CityProject();
                city.Workers = city.Workers ?? new int[6];
                if (city.Workers.Length != 6) Array.Resize(ref city.Workers, 6);
                city.ServiceFunding = city.ServiceFunding ?? new int[3];
                city.BuildingIds = city.BuildingIds ?? Array.Empty<string>();
                city.BuildingLevels = city.BuildingLevels ?? Array.Empty<int>();
                city.CompletedCulturalIds = city.CompletedCulturalIds ?? Array.Empty<string>();
                city.PopulationTenths = Math.Max(0, Math.Min(city.HousingTenths, city.PopulationTenths));
                city.ReservedPopulationTenths = Math.Max(0, Math.Min(city.PopulationTenths, city.ReservedPopulationTenths));
                city.FoodTenths = Math.Max(0, Math.Min(Math.Max(city.FoodStoreCapacityTenths, city.PopulationTenths * 4), city.FoodTenths));
                for (int w = 0; w < city.ServiceFunding.Length; w++) city.ServiceFunding[w] = Math.Max(0, Math.Min(2, city.ServiceFunding[w]));
                if (city.OwnerId < 0 || city.OwnerId >= state.Civilizations.Length) throw new InvalidOperationException("У города нет владельца.");
                EconomyCalculator.NormalizeWorkerAssignments(state, city);
            }
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                civ.KnownTechnologies = civ.KnownTechnologies ?? Array.Empty<string>();
                civ.Laws = civ.Laws ?? Array.Empty<string>();
                civ.TechnologyProgressTenths = civ.TechnologyProgressTenths ?? new int[RulesCatalog.Technologies.Length];
                civ.Relations = civ.Relations ?? new int[state.Civilizations.Length];
                civ.Trusts = civ.Trusts ?? new int[state.Civilizations.Length];
                civ.Threats = civ.Threats ?? new int[state.Civilizations.Length];
                civ.CulturalInfluence = civ.CulturalInfluence ?? new int[state.Civilizations.Length];
                civ.TradeDependency = civ.TradeDependency ?? new int[state.Civilizations.Length];
                civ.PrisonersHeldByCivilizationTenths = civ.PrisonersHeldByCivilizationTenths ?? new int[state.Civilizations.Length];
                if (civ.PrisonersHeldByCivilizationTenths.Length != state.Civilizations.Length)
                    Array.Resize(ref civ.PrisonersHeldByCivilizationTenths, state.Civilizations.Length);
                if (civ.TechnologyProgressTenths.Length != RulesCatalog.Technologies.Length)
                    Array.Resize(ref civ.TechnologyProgressTenths, RulesCatalog.Technologies.Length);
            }
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit == null || unit.Id <= 0 || !entityIds.Add(unit.Id)) throw new InvalidOperationException("Повторяется идентификатор отряда.");
                maximumEntityId = Math.Max(maximumEntityId, unit.Id);
                if (state.Civilization(unit.OwnerId) == null || RulesCatalog.Unit(unit.Kind) == null) throw new InvalidOperationException("Повреждён отряд.");
                unit.Strength = Math.Max(0, Math.Min(150, unit.Strength));
                unit.Readiness = Math.Max(0, Math.Min(120, unit.Readiness));
            }
            NormalizeUnitPopulationData(state);
            if (state.Treaties == null) state.Treaties = Array.Empty<TreatyState>();
            if (state.Conflicts == null) state.Conflicts = Array.Empty<ConflictState>();
            if (state.Events == null) state.Events = Array.Empty<ActiveEventState>();
            if (state.Reports == null) state.Reports = Array.Empty<TurnReport>();
            if (state.EventLog == null) state.EventLog = Array.Empty<string>();
            if (state.Crisis == null) state.Crisis = new CrisisState();
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                if (state.Treaties[i] == null || state.Treaties[i].Id <= 0 || !entityIds.Add(state.Treaties[i].Id)) throw new InvalidOperationException("Повторяется идентификатор договора.");
                maximumEntityId = Math.Max(maximumEntityId, state.Treaties[i].Id);
            }
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                if (state.Conflicts[i] == null || state.Conflicts[i].Id <= 0 || !entityIds.Add(state.Conflicts[i].Id)) throw new InvalidOperationException("Повторяется идентификатор конфликта.");
                maximumEntityId = Math.Max(maximumEntityId, state.Conflicts[i].Id);
            }
            for (int i = 0; i < state.Events.Length; i++)
            {
                if (state.Events[i] == null || state.Events[i].Id <= 0 || !entityIds.Add(state.Events[i].Id)) throw new InvalidOperationException("Повторяется идентификатор события.");
                maximumEntityId = Math.Max(maximumEntityId, state.Events[i].Id);
            }
            if (state.NextEntityId <= maximumEntityId) state.NextEntityId = maximumEntityId + 1;
        }

        private static UnitState FindUnit(GameState state, int id)
        {
            for (int i = 0; i < state.Units.Length; i++) if (state.Units[i].Id == id) return state.Units[i];
            return null;
        }

        private static UnitState FindUnitAt(GameState state, int q, int r, int excludeCivilizationId)
        {
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.Q == q && unit.R == r && unit.OwnerId != excludeCivilizationId) return unit;
            }
            return null;
        }

        private static CityState FindCity(GameState state, int id) { return state.City(id); }

        private static int CountOwnedCities(GameState state, int civilizationId)
        {
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == civilizationId) count++;
            return count;
        }

        private static void RestoreNationAndDistrictData(GameState state)
        {
            if (state.Civilizations == null) return;
            var usedNations = new HashSet<string>();
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civilization = state.Civilizations[i];
                if (civilization == null) continue;
                NationDefinition nation = NationCatalog.Get(civilization.NationId);
                if (nation != null && civilization.NationId != nation.Id)
                {
                    civilization.NationId = nation.Id;
                    civilization.Name = nation.NameRu;
                    CityState capital = null;
                    if (state.Cities != null)
                        for (int city = 0; city < state.Cities.Length; city++)
                        {
                            CityState candidate = state.Cities[city];
                            if (candidate != null && candidate.OwnerId == civilization.Id && (capital == null || candidate.Id < capital.Id)) capital = candidate;
                        }
                    if (capital != null) capital.Name = nation.CapitalName;
                }
                if (nation == null || usedNations.Contains(nation.Id))
                {
                    nation = NationCatalog.GetAt(i);
                    civilization.NationId = nation.Id;
                    civilization.Name = nation.NameRu;
                }
                usedNations.Add(nation.Id);
            }

            if (state.Cells == null || state.Cities == null) return;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell == null) continue;
                cell.DistrictBuildingId = cell.DistrictBuildingId ?? "";
                if (string.IsNullOrEmpty(cell.DistrictBuildingId))
                {
                    cell.DistrictCityId = -1;
                    cell.DistrictLevel = 0;
                }
            }
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city == null || city.BuildingIds == null) continue;
                for (int building = 0; building < city.BuildingIds.Length; building++)
                    if (city.BuildingLevel(city.BuildingIds[building]) > 0)
                        PlaceDistrict(state, city, city.BuildingIds[building]);
            }
        }

        private static bool HasDistrictPlot(GameState state, CityState city, string buildingId)
        {
            if (FindDistrictCell(state, city, buildingId) != null) return true;
            return FindDistrictCell(state, city, null) != null;
        }

        private static bool PlaceDistrict(GameState state, CityState city, string buildingId)
        {
            HexCell cell = FindDistrictCell(state, city, buildingId);
            if (cell == null) cell = FindDistrictCell(state, city, null);
            if (cell == null) return false;
            if (cell.OwnerId < 0) cell.OwnerId = city.OwnerId;
            cell.DistrictCityId = city.Id;
            cell.DistrictBuildingId = buildingId;
            cell.DistrictLevel = Math.Max(1, city.BuildingLevel(buildingId));
            RevealDistrictCell(state, city, cell);
            return true;
        }

        private static bool PlaceDistrict(GameState state, CityState city, string buildingId, int preferredQ, int preferredR)
        {
            HexCell cell = state.Cell(preferredQ, preferredR);
            if (cell == null || !DistrictPlotAvailable(state, city, cell, buildingId))
                return PlaceDistrict(state, city, buildingId);
            if (cell.OwnerId < 0) cell.OwnerId = city.OwnerId;
            cell.DistrictCityId = city.Id;
            cell.DistrictBuildingId = buildingId;
            cell.DistrictLevel = Math.Max(1, city.BuildingLevel(buildingId));
            RevealDistrictCell(state, city, cell);
            return true;
        }

        private static void RevealDistrictCell(GameState state, CityState city, HexCell cell)
        {
            int mask = 1 << city.OwnerId;
            cell.VisibleToMask |= mask;
            cell.ExploredByMask |= mask;
            cell.LastSeenTurn = state.Turn;
        }

        private static HexCell FindDistrictCell(GameState state, CityState city, string buildingId)
        {
            HexCell best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell == null) continue;
                if (!string.IsNullOrEmpty(buildingId) && cell.DistrictCityId == city.Id && cell.DistrictBuildingId == buildingId)
                {
                    cell.DistrictLevel = Math.Max(1, city.BuildingLevel(buildingId));
                    return cell;
                }
                if (!DistrictPlotAvailable(state, city, cell, buildingId)) continue;
                int distance = HexGrid.Distance(cell.Q, cell.R, city.Q, city.R);

                if (distance < bestDistance || (distance == bestDistance && (best == null || cell.Q < best.Q || (cell.Q == best.Q && cell.R < best.R))))
                {
                    best = cell;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static bool DistrictPlotAvailable(GameState state, CityState city, HexCell cell, string buildingId)
        {
            if (cell == null || city == null) return false;
            if (cell.DistrictCityId == city.Id && cell.DistrictBuildingId == buildingId && !string.IsNullOrEmpty(buildingId)) return true;
            if (cell.DistrictCityId >= 0 || !string.IsNullOrEmpty(cell.DistrictBuildingId)) return false;
            if (cell.OwnerId >= 0 && cell.OwnerId != city.OwnerId) return false;
            int distance = HexGrid.Distance(cell.Q, cell.R, city.Q, city.R);
            if (distance < 1 || distance > 4) return false;
            if (cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Swamp) return false;
            if (cell.Improvement != ImprovementKind.None || cell.Resource != ResourceKind.None) return false;
            for (int c = 0; c < state.Cities.Length; c++)
                if (state.Cities[c].Q == cell.Q && state.Cities[c].R == cell.R) return false;
            for (int c = 0; c < state.Cities.Length; c++)
            {
                CityState other = state.Cities[c];
                if (other.Id == city.Id || other.OwnerId != city.OwnerId || other.Project == null ||
                    other.Project.Kind != ProjectKind.Building || !other.Project.DistrictPlotReserved) continue;
                if (other.Project.TargetQ == cell.Q && other.Project.TargetR == cell.R) return false;
            }
            return true;
        }

        private static int CountOwnedBuilding(GameState state, int civilizationId, string buildingId)
        {
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civilizationId) count += state.Cities[i].BuildingLevel(buildingId);
            return count;
        }

        private static int CountCitiesWithBuilding(GameState state, int civilizationId, string buildingId)
        {
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civilizationId && state.Cities[i].BuildingLevel(buildingId) > 0) count++;
            return count;
        }

        private static int CountRoutes(GameState state, int civilizationId)
        {
            int count = 0;
            for (int i = 0; i < state.Treaties.Length; i++)
                if (state.Treaties[i].Active && state.Treaties[i].Kind == TreatyKind.TradeRoute &&
                    (state.Treaties[i].FromCivilizationId == civilizationId || state.Treaties[i].ToCivilizationId == civilizationId)) count++;
            return count;
        }

        private static int MovementCost(HexCell destination)
        {
            if (destination == null) return int.MaxValue;
            bool difficultTerrain = destination.Terrain == TerrainKind.Hills || destination.Terrain == TerrainKind.Swamp ||
                                    (destination.Terrain == TerrainKind.River && destination.Improvement != ImprovementKind.Bridge);
            int cost = difficultTerrain ? 2 : 1;
            if (destination.HasRoad && destination.Damage < 75) cost = 1;
            return cost;
        }

        private static int TradeSlots(GameState state, int civilizationId)
        {
            int slots = 1;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civilizationId) slots += state.Cities[i].BuildingLevel("market");
            if (state.Civilization(civilizationId).Knows("trade_league")) slots++;
            return slots;
        }

        public static bool CanStartTradeRouteFromEvent(GameState state, int civilizationId)
        {
            CityState origin;
            CityState destination;
            int partnerId;
            return TryFindEventTradeRoute(state, civilizationId, out origin, out destination, out partnerId);
        }

        private static bool TryFindEventTradeRoute(GameState state, int civilizationId, out CityState origin, out CityState destination, out int partnerId)
        {
            origin = null;
            destination = null;
            partnerId = -1;
            CivilizationState actor = state == null ? null : state.Civilization(civilizationId);
            int cost = TreatyCost(TreatyKind.TradeRoute);
            if (actor == null || actor.IsDefeated || actor.InfluenceTenths < cost || CountRoutes(state, civilizationId) >= TradeSlots(state, civilizationId)) return false;

            int bestScore = int.MinValue;
            int bestDistance = int.MaxValue;
            for (int c = 0; c < state.Civilizations.Length; c++)
            {
                CivilizationState target = state.Civilizations[c];
                if (target == null || target.Id == civilizationId || target.IsDefeated || AtWar(state, civilizationId, target.Id)) continue;
                if (CountRoutes(state, target.Id) >= TradeSlots(state, target.Id) || HasTradeRouteRecord(state, civilizationId, target.Id)) continue;
                int acceptance = TradeAcceptance(actor, target, TreatyKind.TradeRoute);
                if (acceptance < 25) continue;

                for (int i = 0; i < state.Cities.Length; i++)
                {
                    CityState first = state.Cities[i];
                    if (first == null || first.OwnerId != civilizationId) continue;
                    for (int j = 0; j < state.Cities.Length; j++)
                    {
                        CityState second = state.Cities[j];
                        if (second == null || second.OwnerId != target.Id || !RouteExists(state, first, second)) continue;
                        int distance = HexGrid.Distance(first.Q, first.R, second.Q, second.R);
                        if (acceptance > bestScore || (acceptance == bestScore && distance < bestDistance))
                        {
                            bestScore = acceptance;
                            bestDistance = distance;
                            origin = first;
                            destination = second;
                            partnerId = target.Id;
                        }
                    }
                }
            }
            return origin != null && destination != null;
        }

        private static bool HasTradeRouteRecord(GameState state, int first, int second)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (treaty.Kind != TreatyKind.TradeRoute || (!treaty.Active && !treaty.Pending)) continue;
                if ((treaty.FromCivilizationId == first && treaty.ToCivilizationId == second) ||
                    (treaty.FromCivilizationId == second && treaty.ToCivilizationId == first)) return true;
            }
            return false;
        }

        public static bool CanExchangeLand(GameState state, int eventCityId, int actorId, int otherId)
        {
            CityState eventCity = state == null ? null : state.City(eventCityId);
            HexCell actorCell;
            HexCell otherCell;
            return TryFindLandExchange(state, eventCity, actorId, otherId, out actorCell, out otherCell);
        }

        private static bool TryFindLandExchange(GameState state, CityState eventCity, int actorId, int otherId, out HexCell actorCell, out HexCell otherCell)
        {
            actorCell = null;
            otherCell = null;
            if (state == null || eventCity == null || eventCity.OwnerId != actorId || otherId < 0 || otherId >= state.Civilizations.Length || otherId == actorId) return false;

            CityState otherCity = null;
            int nearestDistance = int.MaxValue;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState candidate = state.Cities[i];
                if (candidate == null || candidate.OwnerId != otherId) continue;
                int distance = HexGrid.Distance(eventCity.Q, eventCity.R, candidate.Q, candidate.R);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    otherCity = candidate;
                }
            }
            if (otherCity == null) return false;

            int bestScore = int.MaxValue;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell first = state.Cells[i];
                if (!CanExchangeCell(state, first, actorId) || HexGrid.Distance(first.Q, first.R, eventCity.Q, eventCity.R) > 3) continue;
                for (int j = 0; j < state.Cells.Length; j++)
                {
                    HexCell second = state.Cells[j];
                    if (!CanExchangeCell(state, second, otherId) || HexGrid.Distance(second.Q, second.R, otherCity.Q, otherCity.R) > 3) continue;
                    int score = HexGrid.Distance(first.Q, first.R, eventCity.Q, eventCity.R) +
                                HexGrid.Distance(second.Q, second.R, otherCity.Q, otherCity.R);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        actorCell = first;
                        otherCell = second;
                    }
                }
            }
            return actorCell != null && otherCell != null;
        }

        private static bool CanExchangeCell(GameState state, HexCell cell, int ownerId)
        {
            if (cell == null || cell.OwnerId != ownerId || cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Mountain ||
                cell.DistrictCityId >= 0 || !string.IsNullOrEmpty(cell.DistrictBuildingId) || cell.Improvement != ImprovementKind.None ||
                cell.HasRoad || cell.Damage > 0) return false;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i] != null && state.Cities[i].Q == cell.Q && state.Cities[i].R == cell.R) return false;
            for (int i = 0; i < state.Units.Length; i++)
                if (state.Units[i] != null && state.Units[i].Q == cell.Q && state.Units[i].R == cell.R) return false;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityProject project = state.Cities[i] == null ? null : state.Cities[i].Project;
                if (project != null && project.DistrictPlotReserved && project.TargetQ == cell.Q && project.TargetR == cell.R) return false;
            }
            return true;
        }

        public static bool CanStartDomesticSupply(GameState state, CityState city)
        {
            return state != null && city != null && city.Project != null && city.Project.Kind == ProjectKind.None &&
                   city.BuildingLevel("granary") == 0 && FindDistrictCell(state, city, null) != null;
        }

        public static bool HasLocalTruceOption(GameState state, int civilizationId)
        {
            if (state == null) return false;
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (conflict != null && conflict.Active && conflict.Phase >= 1 && conflict.Phase <= 2 &&
                    (conflict.AggressorId == civilizationId || conflict.DefenderId == civilizationId)) return true;
            }
            return false;
        }

        private static bool AtWar(GameState state, int first, int second)
        {
            ConflictState conflict = FindConflict(state, first, second);
            return conflict != null && conflict.Phase == 2;
        }

        private static bool HasTreaty(GameState state, TreatyKind kind, int first, int second)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || treaty.Kind != kind) continue;
                if ((treaty.FromCivilizationId == first && treaty.ToCivilizationId == second) ||
                    (treaty.FromCivilizationId == second && treaty.ToCivilizationId == first)) return true;
            }
            return false;
        }

        private static ConflictState FindConflict(GameState state, int first, int second)
        {
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (conflict.Active && ((conflict.AggressorId == first && conflict.DefenderId == second) ||
                    (conflict.AggressorId == second && conflict.DefenderId == first))) return conflict;
            }
            return null;
        }

        private static int MilitaryStrength(GameState state, int civilizationId)
        {
            int total = 0;
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.OwnerId != civilizationId || unit.Kind == UnitKind.SettlerWagon) continue;
                total += Math.Max(0, unit.Strength) * Math.Max(30, unit.Readiness) / 100 * Math.Max(3000, unit.SupplyBasisPoints) / 10000;
            }
            return total;
        }

        private static int ObservedMilitaryStrength(GameState state, int civilizationId, int observerId)
        {
            int total = 0;
            int observerMask = 1 << observerId;
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.OwnerId != civilizationId || unit.Kind == UnitKind.SettlerWagon) continue;
                HexCell cell = state.Cell(unit.Q, unit.R);
                if (cell == null || (cell.VisibleToMask & observerMask) == 0) continue;
                total += Math.Max(0, unit.Strength) * Math.Max(30, unit.Readiness) / 100 * Math.Max(3000, unit.SupplyBasisPoints) / 10000;
            }
            return total;
        }

        private static bool IsKnownToCivilization(HexCell cell, int civilizationId)
        {
            return cell != null && (cell.ExploredByMask & (1 << civilizationId)) != 0;
        }

        private static bool IsVisibleToCivilization(HexCell cell, int civilizationId)
        {
            return cell != null && (cell.VisibleToMask & (1 << civilizationId)) != 0;
        }

        private static UnitState WeakestMilitaryUnit(GameState state, int civilizationId)
        {
            UnitState weakest = null;
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.OwnerId != civilizationId || unit.Kind == UnitKind.SettlerWagon) continue;
                if (weakest == null || unit.Strength < weakest.Strength) weakest = unit;
            }
            return weakest;
        }

        private static HexCell FindBorderClaimCell(GameState state, int claimantId, int targetId, int observerId)
        {
            HexCell best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (!IsKnownToCivilization(cell, observerId) || cell.OwnerId != targetId || cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Mountain ||
                    cell.Terrain == TerrainKind.Swamp || cell.DistrictCityId >= 0 || !string.IsNullOrEmpty(cell.DistrictBuildingId)) continue;
                bool cityCell = false;
                for (int c = 0; c < state.Cities.Length; c++)
                    if (state.Cities[c].Q == cell.Q && state.Cities[c].R == cell.R && IsKnownToCivilization(cell, observerId)) { cityCell = true; break; }
                if (cityCell) continue;
                bool border = false;
                for (int d = 0; d < 6; d++)
                {
                    HexCell neighbor = state.Cell(cell.Q + HexGrid.Directions[d, 0], cell.R + HexGrid.Directions[d, 1]);
                    if (neighbor != null && neighbor.OwnerId == claimantId) { border = true; break; }
                }
                if (!border) continue;
                int claimantDistance = int.MaxValue;
                int targetDistance = int.MaxValue;
                for (int c = 0; c < state.Cities.Length; c++)
                {
                    CityState city = state.Cities[c];
                    int distance = HexGrid.Distance(cell.Q, cell.R, city.Q, city.R);
                    if (city.OwnerId == claimantId) claimantDistance = Math.Min(claimantDistance, distance);
                    if (city.OwnerId == targetId && IsKnownToCivilization(state.Cell(city.Q, city.R), observerId)) targetDistance = Math.Min(targetDistance, distance);
                }
                if (targetDistance == int.MaxValue) continue;
                int score = claimantDistance * 3 + targetDistance;
                if (score < bestScore) { bestScore = score; best = cell; }
            }
            return best;
        }

        private static void RestoreRouteAfterPeace(GameState state, int firstId, int secondId)
        {
            CityState firstCity = null;
            CityState secondCity = null;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                if (firstCity == null && state.Cities[i].OwnerId == firstId) firstCity = state.Cities[i];
                if (secondCity == null && state.Cities[i].OwnerId == secondId) secondCity = state.Cities[i];
            }
            if (firstCity == null || secondCity == null) return;
            TreatyState route = null;
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState candidate = state.Treaties[i];
                if (candidate.Kind == TreatyKind.TradeRoute &&
                    ((candidate.FromCivilizationId == firstId && candidate.ToCivilizationId == secondId) ||
                     (candidate.FromCivilizationId == secondId && candidate.ToCivilizationId == firstId)))
                { route = candidate; break; }
            }
            if (route == null)
            {
                route = new TreatyState
                {
                    Id = state.NextEntityId++, Kind = TreatyKind.TradeRoute,
                    FromCivilizationId = firstId, ToCivilizationId = secondId,
                    RouteCityA = firstCity.Id, RouteCityB = secondCity.Id,
                    TreasuryPerTurnTenths = 20, TurnsRemaining = 0
                };
                Append(ref state.Treaties, route);
            }
            else
            {
                CityState routeA = state.City(route.RouteCityA);
                CityState routeB = state.City(route.RouteCityB);
                if (routeA == null || routeB == null || !RouteExists(state, routeA, routeB))
                { route.RouteCityA = firstCity.Id; route.RouteCityB = secondCity.Id; }
                route.TurnsRemaining = 0;
                route.Broken = false;
                route.Fulfilled = false;
                route.Pending = false;
            }
            CityState selectedA = state.City(route.RouteCityA);
            CityState selectedB = state.City(route.RouteCityB);
            if (selectedA != null && selectedB != null && RouteExists(state, selectedA, selectedB) && !HasActiveEmbargo(state, selectedA.Id, selectedB.Id))
            {
                route.Active = true;
                ApplyTreatyEffect(state, route);
            }
        }

        private static int TreatyCost(TreatyKind kind)
        {
            switch (kind)
            {
                case TreatyKind.TradeRoute: return 20;
                case TreatyKind.NonAggression: return 50;
                case TreatyKind.OpenBorders: return 40;
                case TreatyKind.KnowledgeExchange: return 30;
                case TreatyKind.Guarantee: return 80;
                case TreatyKind.Embargo: return 30;
                case TreatyKind.ResourceExport: return 20;
                case TreatyKind.HumanitarianAid: return 20;
                case TreatyKind.Mediation: return 50;
                case TreatyKind.Peace: return 0;
                default: return 20;
            }
        }

        private static int TreatyDuration(TreatyKind kind)
        {
            if (kind == TreatyKind.TradeRoute) return 0;
            if (kind == TreatyKind.KnowledgeExchange || kind == TreatyKind.HumanitarianAid || kind == TreatyKind.Mediation) return 1;
            if (kind == TreatyKind.NonAggression || kind == TreatyKind.OpenBorders || kind == TreatyKind.Guarantee || kind == TreatyKind.ResourceExport) return 5;
            if (kind == TreatyKind.Embargo) return 3;
            if (kind == TreatyKind.Peace) return 2;
            return 0;
        }

        private static string TreatyName(TreatyKind kind)
        {
            switch (kind)
            {
                case TreatyKind.TradeRoute: return "торговый маршрут";
                case TreatyKind.NonAggression: return "договор о ненападении";
                case TreatyKind.OpenBorders: return "открытые границы";
                case TreatyKind.KnowledgeExchange: return "обмен знаниями";
                case TreatyKind.Guarantee: return "гарантия безопасности";
                case TreatyKind.Embargo: return "эмбарго";
                case TreatyKind.ResourceExport: return "экспорт соли";
                case TreatyKind.HumanitarianAid: return "гуманитарная помощь";
                case TreatyKind.Mediation: return "посредничество";
                case TreatyKind.Peace: return "перемирие";
                default: return "соглашение";
            }
        }

        private static void ApplyTreatyEffect(GameState state, TreatyState treaty)
        {
            state.TreatiesThisTurn++;
            CivilizationState first = state.Civilization(treaty.FromCivilizationId);
            CivilizationState second = state.Civilization(treaty.ToCivilizationId);
            if (first == null || second == null) return;
            first.Relations[second.Id] = Math.Min(100, first.Relations[second.Id] + 8);
            second.Relations[first.Id] = Math.Min(100, second.Relations[first.Id] + 8);
            first.Trusts[second.Id] = Math.Min(100, first.Trusts[second.Id] + 5);
            second.Trusts[first.Id] = Math.Min(100, second.Trusts[first.Id] + 5);
            if (treaty.Kind == TreatyKind.KnowledgeExchange)
            {
                int firstContribution = first.KnowledgeTenths / 5;
                int secondContribution = second.KnowledgeTenths / 5;
                first.KnowledgeTenths -= firstContribution;
                second.KnowledgeTenths -= secondContribution;
                first.KnowledgeTenths += secondContribution;
                second.KnowledgeTenths += firstContribution;
                first.CulturePrestige++;
                second.CulturePrestige++;
            }
            if (treaty.Kind == TreatyKind.HumanitarianAid)
            {
                CityState source = state.City(treaty.RouteCityA);
                CityState recipient = state.City(treaty.RouteCityB);
                if (source != null && recipient != null)
                {
                    source.FoodTenths = Math.Max(0, source.FoodTenths - 20);
                    recipient.FoodTenths += 20;
                    first.TreasuryTenths -= 50;
                    second.TreasuryTenths += 50;
                    recipient.Trust = Math.Min(100, recipient.Trust + 5);
                }
            }
            if (treaty.Kind == TreatyKind.Mediation)
            {
                for (int i = 0; i < state.Conflicts.Length; i++)
                {
                    ConflictState conflict = state.Conflicts[i];
                    if (!conflict.Active || conflict.Phase > 2 ||
                        (conflict.AggressorId != second.Id && conflict.DefenderId != second.Id) ||
                        conflict.AggressorId == first.Id || conflict.DefenderId == first.Id) continue;
                    conflict.Phase = 3;
                    conflict.TruceTurns = 3;
                    CivilizationState aggressor = state.Civilization(conflict.AggressorId);
                    CivilizationState defender = state.Civilization(conflict.DefenderId);
                    if (aggressor != null) aggressor.WarStrain = Math.Max(0, aggressor.WarStrain - 10);
                    if (defender != null) defender.WarStrain = Math.Max(0, defender.WarStrain - 10);
                    ReleaseOccupation(state, conflict);
                    break;
                }
            }
            if (treaty.Kind == TreatyKind.Guarantee)
            {
                first.Threats[second.Id] = Math.Max(0, first.Threats[second.Id] - 10);
                second.Threats[first.Id] = Math.Max(0, second.Threats[first.Id] - 10);
            }
            if (treaty.Kind == TreatyKind.Embargo)
            {
                for (int i = 0; i < state.Treaties.Length; i++)
                    if (state.Treaties[i].Active && (state.Treaties[i].Kind == TreatyKind.TradeRoute || state.Treaties[i].Kind == TreatyKind.ResourceExport) &&
                        RouteCitiesMatch(state.Treaties[i], treaty.RouteCityA, treaty.RouteCityB))
                    {
                        MarkTreatyBroken(state, state.Treaties[i]);
                        state.Treaties[i].Active = false;
                    }
            }
        }

        private static bool RouteCitiesMatch(TreatyState treaty, int cityA, int cityB)
        {
            return treaty != null && ((treaty.RouteCityA == cityA && treaty.RouteCityB == cityB) ||
                (treaty.RouteCityA == cityB && treaty.RouteCityB == cityA));
        }

        private static bool HasActiveEmbargo(GameState state, int cityA, int cityB)
        {
            if (state == null) return false;
            for (int i = 0; i < state.Treaties.Length; i++)
                if (state.Treaties[i].Active && state.Treaties[i].Kind == TreatyKind.Embargo &&
                    RouteCitiesMatch(state.Treaties[i], cityA, cityB)) return true;
            return false;
        }

        private static bool HasRecognizedEmbargoCrisis(GameState state, int firstId, int secondId)
        {
            if (state == null) return false;
            for (int i = 0; i < state.Conflicts.Length; i++)
            {
                ConflictState conflict = state.Conflicts[i];
                if (conflict.Active && conflict.Phase <= 1 &&
                    ((conflict.AggressorId == firstId && conflict.DefenderId == secondId) ||
                     (conflict.AggressorId == secondId && conflict.DefenderId == firstId))) return true;
            }
            if (HasTreatyRecord(state, TreatyKind.TradeRoute, firstId, secondId) ||
                HasTreatyRecord(state, TreatyKind.ResourceExport, firstId, secondId))
            {
                CivilizationState first = state.Civilization(firstId);
                CivilizationState second = state.Civilization(secondId);
                if (first != null && second != null && (first.Threats[secondId] >= 60 || second.Threats[firstId] >= 60)) return true;
            }
            return false;
        }

        private static bool HasPrisonersHeld(GameState state, int firstId, int secondId)
        {
            CivilizationState first = state.Civilization(firstId);
            CivilizationState second = state.Civilization(secondId);
            if (first == null || second == null || first.PrisonersHeldByCivilizationTenths == null || second.PrisonersHeldByCivilizationTenths == null) return false;
            return secondId < first.PrisonersHeldByCivilizationTenths.Length && firstId < second.PrisonersHeldByCivilizationTenths.Length &&
                   (first.PrisonersHeldByCivilizationTenths[secondId] > 0 || second.PrisonersHeldByCivilizationTenths[firstId] > 0);
        }

        private static void ReleasePrisoners(GameState state, int firstId, int secondId)
        {
            CivilizationState first = state.Civilization(firstId);
            CivilizationState second = state.Civilization(secondId);
            if (first == null || second == null || !HasPrisonersHeld(state, firstId, secondId)) return;
            int heldFromSecond = first.PrisonersHeldByCivilizationTenths[secondId];
            int heldFromFirst = second.PrisonersHeldByCivilizationTenths[firstId];
            first.PrisonersHeldByCivilizationTenths[secondId] = 0;
            second.PrisonersHeldByCivilizationTenths[firstId] = 0;
            second.RefugeePopulationTenths += heldFromSecond;
            first.RefugeePopulationTenths += heldFromFirst;
        }

        private static void CloseTradeRoutes(GameState state, int civilizationId, int partnerId)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || (treaty.Kind != TreatyKind.TradeRoute && treaty.Kind != TreatyKind.ResourceExport)) continue;
                bool belongsToActor = treaty.FromCivilizationId == civilizationId || treaty.ToCivilizationId == civilizationId;
                bool partnerMatches = partnerId < 0 || treaty.FromCivilizationId == partnerId || treaty.ToCivilizationId == partnerId;
                if (belongsToActor && partnerMatches)
                {
                    MarkTreatyBroken(state, treaty);
                    treaty.Active = false;
                }
            }
        }

        private static void MarkTreatyBroken(GameState state, TreatyState treaty)
        {
            if (state == null || treaty == null || treaty.Fulfilled || treaty.Broken || treaty.Pending) return;
            treaty.Broken = true;
        }

        private static int TradeAcceptance(CivilizationState actor, CivilizationState target, TreatyKind kind)
        {
            int score = target.Trusts[actor.Id] + target.Relations[actor.Id] / 2 - target.Threats[actor.Id] / 2;
            if (target.Archetype == "Торговец" && kind == TreatyKind.TradeRoute) score += 25;
            if (target.Archetype == "Попечитель" && (kind == TreatyKind.OpenBorders || kind == TreatyKind.NonAggression)) score += 15;
            if (kind == TreatyKind.TradeRoute) score += 15;
            if (kind == TreatyKind.ResourceExport && target.Archetype == "Торговец") score += 20;
            if (kind == TreatyKind.HumanitarianAid) score += 25;
            if (kind == TreatyKind.Mediation) score += 15;
            return score;
        }

        private static bool RouteExists(GameState state, CityState start, CityState goal)
        {
            if (HexGrid.Distance(start.Q, start.R, goal.Q, goal.R) <= 1) return true;
            var queue = new Queue<int>();
            var seen = new HashSet<int>();
            int startIndex = FindCellIndex(state, start.Q, start.R);
            if (startIndex < 0) return false;
            queue.Enqueue(startIndex);
            seen.Add(startIndex);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                HexCell cell = state.Cells[index];
                if (cell.Q == goal.Q && cell.R == goal.R) return true;
                for (int d = 0; d < 6; d++)
                {
                    int q = cell.Q + HexGrid.Directions[d, 0];
                    int r = cell.R + HexGrid.Directions[d, 1];
                    int next = FindCellIndex(state, q, r);
                    if (next < 0 || seen.Contains(next)) continue;
                    HexCell candidate = state.Cells[next];
                    if (candidate.Terrain == TerrainKind.Water || candidate.Terrain == TerrainKind.Mountain || candidate.Terrain == TerrainKind.Swamp) continue;
                    if (candidate.OwnerId >= 0 && candidate.OwnerId != start.OwnerId && candidate.OwnerId != goal.OwnerId &&
                        !HasTreaty(state, TreatyKind.OpenBorders, start.OwnerId, candidate.OwnerId) &&
                        !HasTreaty(state, TreatyKind.OpenBorders, goal.OwnerId, candidate.OwnerId)) continue;
                    seen.Add(next);
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        private static int FindCellIndex(GameState state, int q, int r)
        {
            for (int i = 0; i < state.Cells.Length; i++) if (state.Cells[i].Q == q && state.Cells[i].R == r) return i;
            return -1;
        }

        private static int TradeSlots(GameState state, int ownerId, bool includeLeague)
        {
            return TradeSlots(state, ownerId) + (includeLeague ? 1 : 0);
        }

        private static CityState FirstCityOf(GameState state, int ownerId)
        {
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == ownerId) return state.Cities[i];
            return null;
        }

        private static int FindBestTradePartner(GameState state, int ownerId)
        {
            int best = -1;
            int score = int.MinValue;
            CivilizationState civ = state.Civilization(ownerId);
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                if (i == ownerId || state.Civilizations[i].IsDefeated || AtWar(state, ownerId, i)) continue;
                CityState cityA = FirstCityOf(state, ownerId);
                CityState cityB = FirstCityOf(state, i);
                if (cityA == null || cityB == null || !RouteExists(state, cityA, cityB)) continue;
                int current = civ.Relations[i] + civ.Trusts[i] - civ.Threats[i] + (state.Civilizations[i].Archetype == "Торговец" ? 10 : 0);
                if (current > score) { score = current; best = i; }
            }
            return best;
        }

        private static bool HasUnitKind(GameState state, int ownerId, UnitKind kind)
        {
            for (int i = 0; i < state.Units.Length; i++) if (state.Units[i].OwnerId == ownerId && state.Units[i].Kind == kind) return true;
            return false;
        }

        private static bool IsThreatened(GameState state, int ownerId)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != ownerId) continue;
                for (int u = 0; u < state.Units.Length; u++)
                    if (state.Units[u].OwnerId != ownerId && AtWar(state, ownerId, state.Units[u].OwnerId) &&
                        HexGrid.Distance(city.Q, city.R, state.Units[u].Q, state.Units[u].R) <= 2) return true;
            }
            return false;
        }

        private static bool AtWarWithAny(GameState state, int ownerId)
        {
            for (int i = 0; i < state.Conflicts.Length; i++)
                if (state.Conflicts[i].Active && state.Conflicts[i].Phase == 2 &&
                    (state.Conflicts[i].AggressorId == ownerId || state.Conflicts[i].DefenderId == ownerId)) return true;
            return false;
        }

        private static CityState MostAttractiveDestination(GameState state, CityState origin)
        {
            CityState best = null;
            int bestValue = int.MinValue;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState candidate = state.Cities[i];
                if (candidate.Id == origin.Id || candidate.OwnerId != origin.OwnerId || candidate.OccupiedById >= 0 ||
                    candidate.PopulationTenths >= candidate.HousingTenths || candidate.Health < 30 || candidate.FoodShortageTurns > 0) continue;
                int attraction = CityAttraction(state, candidate);
                if (attraction > bestValue) { bestValue = attraction; best = candidate; }
            }
            return best;
        }

        private static int CityAttraction(GameState state, CityState city)
        {
            CivilizationState civ = state.Civilization(city.OwnerId);
            int value = city.Attraction + city.Health / 3 + city.Trust / 4 + city.Environment / 5 + city.Safety / 6;
            value += Math.Min(15, city.Education / 6) + (city.FoodShortageTurns == 0 ? 10 : -20);
            if (civ != null && civ.HasLaw("city_autonomy")) value += 10;
            if (city.OccupiedById >= 0) value -= 30;
            if (AtWarWithAny(state, city.OwnerId)) value -= 8;
            return value;
        }

        private static bool HasFriendlySupplyPath(GameState state, int ownerId, int fromQ, int fromR, int toQ, int toR)
        {
            if (HexGrid.Distance(fromQ, fromR, toQ, toR) > 4) return false;
            int currentQ = fromQ;
            int currentR = fromR;
            int guard = 0;
            while ((currentQ != toQ || currentR != toR) && guard++ < 8)
            {
                int bestQ = currentQ;
                int bestR = currentR;
                int bestDistance = HexGrid.Distance(currentQ, currentR, toQ, toR);
                for (int d = 0; d < 6; d++)
                {
                    int q = currentQ + HexGrid.Directions[d, 0];
                    int r = currentR + HexGrid.Directions[d, 1];
                    HexCell candidate = state.Cell(q, r);
                    if (candidate == null || (candidate.OwnerId != ownerId && !(q == toQ && r == toR)) || candidate.Terrain == TerrainKind.Water || candidate.Terrain == TerrainKind.Mountain) continue;
                    int distance = HexGrid.Distance(q, r, toQ, toR);
                    if (distance < bestDistance) { bestDistance = distance; bestQ = q; bestR = r; }
                }
                if (bestQ == currentQ && bestR == currentR) return false;
                currentQ = bestQ;
                currentR = bestR;
            }
            return currentQ == toQ && currentR == toR;
        }

        private static HexCell FindSettlerCell(GameState state, CityState city)
        {
            HexCell best = null;
            int bestValue = int.MinValue;
            for (int i = 0; i < state.Cells.Length; i++)
            {
                HexCell cell = state.Cells[i];
                if (cell.OwnerId >= 0 || cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Swamp || cell.Terrain == TerrainKind.Water) continue;
                bool farEnough = true;
                for (int c = 0; c < state.Cities.Length; c++) if (HexGrid.Distance(cell.Q, cell.R, state.Cities[c].Q, state.Cities[c].R) < 3) farEnough = false;
                if (!farEnough) continue;
                int value = cell.Terrain == TerrainKind.Meadow || cell.Terrain == TerrainKind.River ? 10 : cell.Terrain == TerrainKind.Forest ? 5 : 0;
                if (cell.Resource == ResourceKind.FertileSoil) value += 5;
                value -= HexGrid.Distance(city.Q, city.R, cell.Q, cell.R);
                if (value > bestValue) { bestValue = value; best = cell; }
            }
            return best;
        }

        private static void IncreaseCulturalExchange(GameState state, int firstId, int secondId)
        {
            CivilizationState first = state.Civilization(firstId);
            CivilizationState second = state.Civilization(secondId);
            if (first == null || second == null) return;
            int growth = 1;
            if (HasTreaty(state, TreatyKind.OpenBorders, firstId, secondId)) growth++;
            if (HasCulturalPresence(state, firstId) || HasCulturalPresence(state, secondId)) growth++;
            if (AverageEducation(state, firstId) >= 60 || AverageEducation(state, secondId) >= 60) growth++;
            first.CulturalInfluence[secondId] = Math.Min(100, first.CulturalInfluence[secondId] + growth);
            second.CulturalInfluence[firstId] = Math.Min(100, second.CulturalInfluence[firstId] + growth);
        }

        private static bool HasCulturalPresence(GameState state, int civilizationId)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId == civilizationId && (city.BuildingLevel("cultural_house") > 0 || city.BuildingLevel("archive") > 0 || city.BuildingLevel("exchange_house") > 0)) return true;
            }
            return false;
        }

        private static int AverageEducation(GameState state, int civilizationId)
        {
            int total = 0;
            int cities = 0;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civilizationId) { total += state.Cities[i].Education; cities++; }
            return cities == 0 ? 0 : total / cities;
        }

        private static string FormatPopulation(int tenths) { return (tenths / 10.0).ToString("0.0"); }

        private static string NextCityName(GameState state, int ownerId)
        {
            CivilizationState civilization = state.Civilization(ownerId);
            NationDefinition nation = civilization == null ? null : NationCatalog.Get(civilization.NationId);
            string[] names = nation == null || nation.AdditionalCityNames.Length == 0
                ? new[] { "Новый берег", "Дальний сад", "Сосновый круг", "Речные ворота", "Тихая гавань", "Зелёный холм" }
                : nation.AdditionalCityNames;
            int ordinal = 0;
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == ownerId) ordinal++;
            string candidate = names[Math.Max(0, ordinal - 1) % names.Length];
            int suffix = 2;
            while (true)
            {
                bool duplicate = false;
                for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].Name == candidate) duplicate = true;
                if (!duplicate) return candidate;
                candidate = names[(Math.Max(0, ordinal - 1) + suffix - 1) % names.Length] + " " + suffix;
                suffix++;
            }
        }

        private static string EraName(Era era)
        {
            switch (era)
            {
                case Era.Communities: return "общин";
                case Era.Cities: return "городов";
                case Era.ConnectedLands: return "связанных земель";
                case Era.Industry: return "индустрии";
                case Era.Synthesis: return "синтеза";
                default: return "неизвестной эпохи";
            }
        }

        private static string EventName(EventKind kind)
        {
            for (int i = 0; i < RulesCatalog.Events.Length; i++) if (RulesCatalog.Events[i].Kind == kind) return RulesCatalog.Events[i].NameRu;
            return kind.ToString();
        }
    }
}
