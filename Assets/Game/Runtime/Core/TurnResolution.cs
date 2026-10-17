using System;
using System.Collections.Generic;
using System.Linq;

namespace Nasledie.Core
{
    public sealed partial class GameSimulation
    {
        private readonly Dictionary<int, CityYield> turnYields = new Dictionary<int, CityYield>();

        private void ResolveTurn(GameState state)
        {
            TurnReport report = new TurnReport { Turn = state.Turn, Entries = Array.Empty<string>() };
            var lines = new List<string>();
            int[] populationBefore = new int[state.Civilizations.Length];
            int[] treasuryBefore = new int[state.Civilizations.Length];
            int[] foodBefore = new int[state.Civilizations.Length];
            int[] knowledgeBefore = new int[state.Civilizations.Length];
            int[] influenceBefore = new int[state.Civilizations.Length];
            int[] cultureBefore = new int[state.Civilizations.Length];
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                populationBefore[i] = CivilizationPopulationTenths(state, civ.Id);
                treasuryBefore[i] = civ.TreasuryTenths;
                knowledgeBefore[i] = civ.KnowledgeTenths;
                influenceBefore[i] = civ.InfluenceTenths;
                cultureBefore[i] = civ.CulturePrestige;
            }
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId >= 0 && state.Cities[i].OwnerId < foodBefore.Length) foodBefore[state.Cities[i].OwnerId] += state.Cities[i].FoodTenths;

            PlanAndResolveAiOrders(state, lines);
            ResolveCityProductionAndResearch(state, report, lines);
            ResolveTradeRoutes(state, lines);
            ResolveRecruitmentAndProjects(state, lines);
            ResolveSupplyAndSieges(state, lines);
            ResolveFoodHealthGrowth(state, report, lines);
            ResolveMigrationAndRecovery(state, report, lines);
            ResolveConflictsAndDiplomacy(state, lines);
            ResolveEventsAndCrisis(state, lines);
            VictoryEvaluator.Check(state, lines);

            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                int foodAfter = 0;
                for (int j = 0; j < state.Cities.Length; j++) if (state.Cities[j].OwnerId == civ.Id) foodAfter += state.Cities[j].FoodTenths;
                int popDelta = CivilizationPopulationTenths(state, civ.Id) - populationBefore[i];
                int treasuryDelta = civ.TreasuryTenths - treasuryBefore[i];
                int knowledgeDelta = civ.KnowledgeTenths - knowledgeBefore[i];
                int influenceDelta = civ.InfluenceTenths - influenceBefore[i];
                int cultureDelta = civ.CulturePrestige - cultureBefore[i];
                if (civ.Id == state.HumanCivilizationId)
                {
                    report.FoodDeltaTenths = foodAfter - foodBefore[i];
                    report.PopulationDeltaTenths = popDelta;
                    report.TreasuryDeltaTenths = treasuryDelta;
                    report.KnowledgeDeltaTenths = knowledgeDelta;
                    report.InfluenceDeltaTenths = influenceDelta;
                    report.CultureDelta = cultureDelta;
                    state.LastPlayerBalanceTenths = treasuryDelta;
                }
            }
            report.BattleCount = state.BattlesThisTurn;
            report.TreatyCount = state.TreatiesThisTurn;
            state.BattlesThisTurn = 0;
            state.TreatiesThisTurn = 0;
            report.Entries = lines.ToArray();
            Append(ref state.Reports, report);
            if (state.Reports.Length > 5)
            {
                var retained = new TurnReport[5];
                Array.Copy(state.Reports, state.Reports.Length - 5, retained, 0, 5);
                state.Reports = retained;
            }

            // The final evaluated turn is already a completed turn. Keeping the
            // counter at MaximumTurn avoids showing a phantom 71/70 turn after
            // a Heritage victory at the end of a 70-turn campaign.
            if (!state.IsFinished) state.Turn++;
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitDefinition definition = RulesCatalog.Unit(state.Units[i].Kind);
                state.Units[i].MovementLeft = definition == null ? 0 : definition.Movement;
                state.Units[i].Readiness = Math.Min(120, state.Units[i].Readiness + (state.Units[i].SupplyBasisPoints >= 7000 ? 8 : -10));
                state.Units[i].IsWounded = state.Units[i].WoundedTurns > 0;
                if (state.Units[i].WoundedTurns > 0) state.Units[i].WoundedTurns--;
            }
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                if (state.Civilizations[i].LawCooldownTurns > 0) state.Civilizations[i].LawCooldownTurns--;
                if (state.Civilizations[i].PoliticalCrisisCooldown > 0) state.Civilizations[i].PoliticalCrisisCooldown--;
            }
            GameFactory.RebuildVisibility(state);
            for (int i = lines.Count - 1; i >= 0; i--) AppendLog(state, lines[i]);
            AppendLog(state, "Ход " + (state.Turn - 1) + " завершён. Откройте отчёт, чтобы увидеть источники изменений.");
        }

        private void PlanAndResolveAiOrders(GameState state, List<string> lines)
        {
            for (int c = 0; c < state.Civilizations.Length; c++)
            {
                CivilizationState civ = state.Civilizations[c];
                if (civ.IsHuman || civ.IsDefeated) continue;
                CityState capital = null;
                for (int i = 0; i < state.Cities.Length; i++)
                    if (state.Cities[i].OwnerId == civ.Id && (capital == null || state.Cities[i].AdministrativeLoad > capital.AdministrativeLoad)) capital = state.Cities[i];
                if (capital == null) continue;

                CityYield yield = EconomyCalculator.Calculate(state, capital);
                int consumption = capital.PopulationTenths;
                if (capital.FoodTenths + yield.FoodTenths < consumption * 2)
                    EconomyCalculator.AutoAssign(state, capital, "food");
                else if (civ.Archetype == "Учёный") EconomyCalculator.AutoAssign(state, capital, "science");
                else if (civ.Archetype == "Торговец") EconomyCalculator.AutoAssign(state, capital, "trade");
                else if (civ.Archetype == "Стратег безопасности") EconomyCalculator.AutoAssign(state, capital, "industry");
                else EconomyCalculator.AutoAssign(state, capital, "balanced");

                ChooseAiResearch(civ);
                if (capital.Project == null || capital.Project.Kind == ProjectKind.None)
                {
                    int eraKnowledge = 0;
                    for (int t = 0; t < civ.KnownTechnologies.Length; t++)
                    {
                        TechnologyDefinition known = RulesCatalog.Technology(civ.KnownTechnologies[t]);
                        if (known != null && known.Era == civ.Era) eraKnowledge++;
                    }
                    if ((int)civ.Era < 4 && eraKnowledge >= 4 && civ.InfluenceTenths >= 100)
                        SetProject(state, civ, capital, "era_transition", false);
                    else
                    {
                        string preferredBuilding = AiBuildingChoice(state, civ, capital);
                        if (!string.IsNullOrEmpty(preferredBuilding)) SetProject(state, civ, capital, preferredBuilding, false);
                    }
                }

                if (civ.Archetype == "Торговец" && CountRoutes(state, civ.Id) == 0 && civ.InfluenceTenths >= 20)
                {
                    int partner = FindBestTradePartner(state, civ.Id);
                    if (partner >= 0)
                    {
                        CityState destination = FirstCityOf(state, partner);
                        if (destination != null) ProposeTreaty(state, civ, partner, TreatyKind.TradeRoute, capital.Id, destination.Id, false);
                    }
                }

                if (capital.PopulationTenths >= 60 && capital.FoodTenths >= 80 && CountOwnedCities(state, civ.Id) < Math.Min(4, civ.AdministrativeCapacity) && capital.Project.Kind == ProjectKind.None)
                    Recruit(state, civ, capital, UnitKind.SettlerWagon, false);

                if (capital.BuildingLevel("barracks") > 0 && !HasUnitKind(state, civ.Id, UnitKind.Militia) && civ.TreasuryTenths > 200 && capital.Project.Kind == ProjectKind.None)
                    Recruit(state, civ, capital, UnitKind.Militia, false);

                civ.StrategyHoldTurns = Math.Max(0, civ.StrategyHoldTurns - 1);
                if (civ.StrategyHoldTurns == 0)
                {
                    civ.StrategicGoal = ChooseAiGoal(state, civ);
                    civ.StrategyHoldTurns = 5;
                }
            }
        }

        private void ChooseAiResearch(CivilizationState civ)
        {
            if (civ.ActiveResearchIndex >= 0 && civ.ActiveResearchIndex < RulesCatalog.Technologies.Length)
            {
                TechnologyDefinition active = RulesCatalog.Technologies[civ.ActiveResearchIndex];
                if (!civ.Knows(active.Id) && (int)active.Era <= (int)civ.Era) return;
            }
            string[] priorities = civ.Archetype == "Учёный"
                ? new[] { "writing", "civic_schools", "university", "computing_networks", "open_science", "agriculture", "stone", "roads", "medicine", "engineering" }
                : civ.Archetype == "Торговец"
                    ? new[] { "writing", "coin_markets", "roads", "trade_league", "coastal_craft", "agriculture", "stone", "engineering" }
                    : civ.Archetype == "Стратег безопасности"
                        ? new[] { "stone", "roads", "engineering", "medicine", "agriculture", "coin_markets" }
                        : new[] { "agriculture", "writing", "medicine", "civic_schools", "public_health", "pottery", "stone" };
            for (int i = 0; i < priorities.Length; i++)
            {
                TechnologyDefinition tech = RulesCatalog.Technology(priorities[i]);
                if (tech == null || civ.Knows(tech.Id) || (int)tech.Era > (int)civ.Era) continue;
                bool prerequisites = true;
                for (int j = 0; j < tech.Prerequisites.Length; j++)
                {
                    string prerequisite = tech.Prerequisites[j];
                    if (prerequisite.Contains("|"))
                    {
                        string[] alternatives = prerequisite.Split('|');
                        if (!civ.Knows(alternatives[0]) && !civ.Knows(alternatives[1])) prerequisites = false;
                    }
                    else if (!civ.Knows(prerequisite)) prerequisites = false;
                }
                if (prerequisites) { civ.ActiveResearchIndex = RulesCatalog.TechnologyIndex(tech.Id); return; }
            }
            civ.ActiveResearchIndex = -1;
        }

        private string AiBuildingChoice(GameState state, CivilizationState civ, CityState city)
        {
            if (city.HousingTenths <= city.PopulationTenths + 10 && city.BuildingLevel("house") == 0) return "house";
            if (city.Health < 65 && civ.Knows("medicine") && city.BuildingLevel("clinic") == 0) return "clinic";
            if (civ.Archetype == "Учёный" && civ.Knows("civic_schools") && city.BuildingLevel("school") == 0) return "school";
            if (civ.Archetype == "Торговец" && civ.Knows("coin_markets") && city.BuildingLevel("market") == 0) return "market";
            if (civ.Knows("writing") && CountOwnedBuilding(state, civ.Id, "council") == 0) return "council";
            if (civ.Knows("stone") && city.BuildingLevel("workshop") == 0) return "workshop";
            if (civ.Knows("writing") && city.BuildingLevel("cultural_house") == 0) return "cultural_house";
            return "house";
        }

        private string ChooseAiGoal(GameState state, CivilizationState civ)
        {
            if (civ.Archetype == "Учёный" && CountOwnedBuilding(state, civ.Id, "university") > 0) return "science";
            if (civ.Archetype == "Торговец" && CountRoutes(state, civ.Id) > 0) return "trade";
            if (civ.Archetype == "Стратег безопасности" && IsThreatened(state, civ.Id)) return "defense";
            return "quality";
        }

        private void ResolveCityProductionAndResearch(GameState state, TurnReport report, List<string> lines)
        {
            turnYields.Clear();
            int[] knowledgeProduced = new int[state.Civilizations.Length];
            int[] cultureProduced = new int[state.Civilizations.Length];
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId < 0 || city.OwnerId >= state.Civilizations.Length || state.Civilizations[city.OwnerId].IsDefeated) continue;
                CityYield yield = EconomyCalculator.Calculate(state, city);
                turnYields[city.Id] = yield;
                CivilizationState civ = state.Civilization(city.OwnerId);
                if (civ.Knows("mechanization") && city.BuildingLevel("workshop") > 0)
                    city.Environment = Math.Max(0, city.Environment - 1);
                if (civ.Knows("clean_energy") && city.Environment < 100)
                    city.Environment = Math.Min(100, city.Environment + 1);
                civ.TreasuryTenths += yield.TreasuryTenths;
                knowledgeProduced[civ.Id] += yield.KnowledgeTenths;
                cultureProduced[civ.Id] += yield.CulturePrestige;
                city.LocalCulturePrestige += yield.CulturePrestige;
                if (yield.UnemployedWorkers * 5 > city.WorkerCount) city.UnemploymentTurns++;
                else city.UnemploymentTurns = 0;
                if (city.UnemploymentTurns >= 2)
                {
                    city.Trust = Math.Max(0, city.Trust - 2);
                    city.Attraction = Math.Max(0, city.Attraction - 5);
                }
                if (city.Project != null && city.Project.Kind != ProjectKind.None)
                {
                    city.Project.Progress += yield.Production;
                    city.Project.TurnsActive++;
                }
                if (city.IntegrationTurns > 0) city.IntegrationTurns--;
                if (city.WarDamageTurns > 0) city.WarDamageTurns--;
            }

            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                if (civ.IsDefeated) continue;
                civ.KnowledgeTenths += knowledgeProduced[i];
                civ.CulturePrestige += cultureProduced[i];
                if (civ.CulturePrestige > 1000) civ.Reputation = Math.Min(100, civ.Reputation + Math.Min(3, (civ.CulturePrestige - 1000) / 50));
                if (civ.HasLaw("local_councils") || civ.HasLaw("centralized_service")) civ.InfluenceTenths += 10;
                else civ.InfluenceTenths += 10; // Council is the baseline institution.
                if (civ.InfluenceTenths > 1000)
                {
                    int excess = civ.InfluenceTenths - 1000;
                    civ.Reputation = Math.Min(100, civ.Reputation + Math.Min(3, excess / 50));
                    civ.InfluenceTenths = 1000;
                }
                AdvanceResearch(state, civ, lines);
                int creditLimit = (20 + CountOwnedCities(state, civ.Id) * 5) * 10;
                civ.DebtTenths = Math.Max(0, -civ.TreasuryTenths);
                if (civ.DebtTenths > 0)
                {
                    civ.TreasuryTenths -= Math.Max(10, civ.DebtTenths / 20);
                    civ.DebtTenths = Math.Max(0, -civ.TreasuryTenths);
                    civ.ConsecutiveDeficitTurns++;
                    if (civ.DebtTenths >= creditLimit)
                    {
                        for (int j = 0; j < state.Cities.Length; j++)
                            if (state.Cities[j].OwnerId == civ.Id) state.Cities[j].Trust = Math.Max(0, state.Cities[j].Trust - 3);
                        lines.Add(civ.Name + ": финансовый дефолт, доверие городов снижается.");
                    }
                }
                else civ.ConsecutiveDeficitTurns = 0;
            }
            report.KnowledgeDeltaTenths += knowledgeProduced[state.HumanCivilizationId];
        }

        private void AdvanceResearch(GameState state, CivilizationState civ, List<string> lines)
        {
            int index = civ.ActiveResearchIndex;
            if (index < 0 || index >= RulesCatalog.Technologies.Length) return;
            TechnologyDefinition technology = RulesCatalog.Technologies[index];
            if (civ.Knows(technology.Id) || (int)technology.Era > (int)civ.Era) { civ.ActiveResearchIndex = -1; return; }
            int cost = technology.Cost * 10;
            if (technology.KeyTechnology) cost = cost * 125 / 100;
            int scienceRate = 10000;
            if (civ.Difficulty == Difficulty.Calm && civ.CompletedTechnologies + 2 < CivLeadTechnologyCount(state, civ.Id)) scienceRate = 11000;
            int available = civ.KnowledgeTenths * scienceRate / 10000;
            int spent = Math.Min(available, Math.Max(0, cost - civ.TechnologyProgressTenths[index]));
            civ.KnowledgeTenths -= spent;
            civ.TechnologyProgressTenths[index] += spent;
            if (civ.TechnologyProgressTenths[index] >= cost)
            {
                civ.TechnologyProgressTenths[index] -= cost;
                civ.AddTechnology(technology.Id);
                civ.ActiveResearchIndex = -1;
                lines.Add(civ.Name + " исследовала «" + technology.NameRu + "».");
                for (int i = 0; i < civ.CulturalInfluence.Length; i++) civ.CulturalInfluence[i] = Math.Min(100, civ.CulturalInfluence[i] + 1);
            }
        }

        private int CivLeadTechnologyCount(GameState state, int civilizationId)
        {
            int maximum = 0;
            for (int i = 0; i < state.Civilizations.Length; i++) maximum = Math.Max(maximum, state.Civilizations[i].CompletedTechnologies);
            return maximum;
        }

        private void ResolveTradeRoutes(GameState state, List<string> lines)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || treaty.Kind != TreatyKind.TradeRoute) continue;
                if (AtWar(state, treaty.FromCivilizationId, treaty.ToCivilizationId))
                {
                    treaty.Active = false;
                    treaty.TurnsRemaining = Math.Max(treaty.TurnsRemaining, 1);
                    lines.Add("Война перекрыла торговый маршрут.");
                    continue;
                }
                CivilizationState from = state.Civilization(treaty.FromCivilizationId);
                CivilizationState to = state.Civilization(treaty.ToCivilizationId);
                if (from == null || to == null) continue;
                int income = treaty.TreasuryPerTurnTenths > 0 ? treaty.TreasuryPerTurnTenths : 20;
                from.TreasuryTenths += income;
                to.TreasuryTenths += income;
                from.TradeDependency[to.Id] = Math.Min(100, from.TradeDependency[to.Id] + 1);
                to.TradeDependency[from.Id] = Math.Min(100, to.TradeDependency[from.Id] + 1);
                int fromCityId = treaty.RouteCityA;
                int toCityId = treaty.RouteCityB;
                CityState source = state.City(fromCityId);
                CityState destination = state.City(toCityId);
                if (source != null && destination != null && treaty.FoodPerTurnTenths > 0)
                {
                    int reserve = source.PopulationTenths * 2;
                    int shipment = Math.Min(treaty.FoodPerTurnTenths, Math.Max(0, source.FoodTenths - reserve));
                    source.FoodTenths -= shipment;
                    destination.FoodTenths += shipment;
                    from.TreasuryTenths -= 10;
                    to.TreasuryTenths -= 10;
                }
                if (state.Turn % 4 == 0)
                {
                    from.Relations[to.Id] = Math.Min(100, from.Relations[to.Id] + 2);
                    to.Relations[from.Id] = Math.Min(100, to.Relations[from.Id] + 2);
                }
                IncreaseCulturalExchange(state, from.Id, to.Id);
            }
        }

        private void ResolveRecruitmentAndProjects(GameState state, List<string> lines)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                CityProject project = city.Project;
                if (project == null || project.Kind == ProjectKind.None || project.Progress < project.Cost) continue;
                if (project.Kind == ProjectKind.Recruitment)
                {
                    UnitKind kind;
                    if (!Enum.TryParse(project.TargetId, out kind)) { city.Project = new CityProject(); continue; }
                    UnitDefinition definition = RulesCatalog.Unit(kind);
                    if (definition == null) { city.Project = new CityProject(); continue; }
                    CivilizationState recruiter = state.Civilization(city.OwnerId);
                    int minimumTurns = kind == UnitKind.SettlerWagon ? 1 : definition.RecruitTurns;
                    if (recruiter != null && recruiter.HasLaw("limited_conscription") && kind != UnitKind.SettlerWagon) minimumTurns = Math.Max(1, minimumTurns - 1);
                    if (project.TurnsActive < minimumTurns) continue;
                    if (kind == UnitKind.SettlerWagon)
                    {
                        if (city.PopulationTenths < 40) { project.Progress = project.Cost; continue; }
                        city.PopulationTenths -= 20;
                        city.ReservedPopulationTenths = Math.Max(0, city.ReservedPopulationTenths - 20);
                        int q = city.Q + 1;
                        int r = city.R;
                        HexCell target = FindSettlerCell(state, city);
                        if (target == null) { city.PopulationTenths += 20; city.Project = new CityProject(); continue; }
                        q = target.Q; r = target.R;
                        Append(ref state.Units, new UnitState
                        {
                            Id = state.NextEntityId++, OwnerId = city.OwnerId, CitySourceId = city.Id,
                            Kind = UnitKind.SettlerWagon, Q = q, R = r, PopulationTenths = 20,
                            FoodCargoTenths = 40, Strength = 0, MovementLeft = 2
                        });
                        lines.Add(city.Name + ": гражданский обоз отправлен основать город.");
                    }
                    else
                    {
                        city.PopulationTenths -= project.ReservedPopulationTenths;
                        city.ReservedPopulationTenths = Math.Max(0, city.ReservedPopulationTenths - project.ReservedPopulationTenths);
                        Append(ref state.Units, new UnitState
                        {
                            Id = state.NextEntityId++, OwnerId = city.OwnerId, CitySourceId = city.Id,
                            Kind = kind, Q = city.Q, R = city.R,
                            Strength = definition.Strength, PopulationTenths = definition.PopulationTenths,
                            Readiness = 80, SupplyBasisPoints = 10000, MovementLeft = 0
                        });
                        lines.Add(city.Name + ": подготовлен отряд «" + definition.NameRu + "». Работники переведены на службу.");
                    }
                    city.Project = new CityProject { Kind = ProjectKind.None, TargetId = "" };
                    continue;
                }
                if (project.Kind == ProjectKind.Building)
                {
                    BuildingDefinition building = RulesCatalog.Building(project.TargetId);
                    if (building == null) { city.Project = new CityProject(); continue; }
                    city.AddBuilding(building.Id);
                    city.HousingTenths += building.HousingTenths;
                    city.FoodStoreCapacityTenths += building.FoodStorageTurns * city.PopulationTenths;
                    city.Health = Math.Min(100, city.Health + building.Health);
                    city.Safety = Math.Min(100, city.Safety + building.Safety);
                    city.Education = Math.Min(100, city.Education + building.Education);
                    CivilizationState owner = state.Civilization(city.OwnerId);
                    if (building.AdminCapacity > 0 && owner != null) owner.AdministrativeCapacity += building.AdminCapacity;
                    if (building.CulturalObject && Array.IndexOf(city.CompletedCulturalIds, building.Id) < 0)
                        Append(ref city.CompletedCulturalIds, building.Id);
                    lines.Add(city.Name + ": завершено строительство «" + building.NameRu + "».");
                    city.Project = new CityProject { Kind = ProjectKind.None, TargetId = "" };
                    continue;
                }
                if (project.Kind == ProjectKind.Improvement)
                {
                    HexCell cell = state.Cell(project.TargetQ, project.TargetR);
                    if (cell != null && cell.OwnerId == city.OwnerId)
                    {
                        if (project.TargetId == "repair_road") { cell.HasRoad = true; cell.Damage = 0; }
                        else if (project.TargetId == "repair_cell") cell.Damage = 0;
                        else
                        {
                            ImprovementKind improvement;
                            if (Enum.TryParse(project.TargetId, out improvement))
                            {
                                cell.Improvement = improvement;
                                cell.ImprovementLevel++;
                                if (improvement == ImprovementKind.Road) cell.HasRoad = true;
                            }
                        }
                        lines.Add(city.Name + ": завершено улучшение клетки " + cell.Q + "," + cell.R + ".");
                    }
                    city.Project = new CityProject { Kind = ProjectKind.None, TargetId = "" };
                    continue;
                }
                if (project.Kind == ProjectKind.EraTransition)
                {
                    CivilizationState owner = state.Civilization(city.OwnerId);
                    if (owner != null && (int)owner.Era < 4)
                    {
                        owner.Era = (Era)((int)owner.Era + 1);
                        lines.Add(owner.Name + " переходит в эпоху «" + EraName(owner.Era) + "».");
                    }
                    city.Project = new CityProject { Kind = ProjectKind.None, TargetId = "" };
                    continue;
                }
                if (project.Kind == ProjectKind.ScientificProject)
                {
                    CivilizationState owner = state.Civilization(city.OwnerId);
                    if (owner != null) owner.ScienceProjectTurns++;
                    if (owner != null && owner.ScienceProjectTurns >= 4)
                    {
                        owner.ScienceProjectTurns = 4;
                        owner.WinningPath = VictoryPath.Scientific;
                        lines.Add(owner.Name + " завершает международную научную программу.");
                        city.Project = new CityProject { Kind = ProjectKind.None, TargetId = "" };
                    }
                }
            }
        }

        private void ResolveSupplyAndSieges(GameState state, List<string> lines)
        {
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.Kind == UnitKind.SettlerWagon) continue;
                CityState source = state.City(unit.CitySourceId);
                int routeDistance = source == null ? int.MaxValue : HexGrid.Distance(unit.Q, unit.R, source.Q, source.R);
                bool roadSupply = source != null && routeDistance <= 4 && HasFriendlySupplyPath(state, unit.OwnerId, source.Q, source.R, unit.Q, unit.R);
                unit.SupplyBasisPoints = roadSupply ? 10000 : routeDistance <= 2 ? 7000 : 4000;
                if (!roadSupply)
                {
                    unit.Readiness = Math.Max(50, unit.Readiness - 10);
                    unit.Strength = Math.Max(0, unit.Strength - 5);
                }
            }
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                bool underSiege = false;
                for (int u = 0; u < state.Units.Length; u++)
                {
                    UnitState unit = state.Units[u];
                    if (unit.OwnerId == city.OwnerId || unit.Kind == UnitKind.SettlerWagon) continue;
                    if (AtWar(state, unit.OwnerId, city.OwnerId) && HexGrid.Distance(unit.Q, unit.R, city.Q, city.R) <= 1)
                        underSiege = true;
                }
                if (underSiege)
                {
                    city.SiegeTurns++;
                    city.EfficiencyBasisPoints = Math.Max(4000, city.EfficiencyBasisPoints - 1500);
                    int maxEvacuation = Math.Max(1, city.PopulationTenths * 12 / 100);
                    int evacuation = Math.Min(Math.Min(maxEvacuation, 5), Math.Max(0, city.PopulationTenths - 40));
                    if (evacuation > 0)
                    {
                        city.PopulationTenths -= evacuation;
                        CivilizationState owner = state.Civilization(city.OwnerId);
                        if (owner != null) owner.RefugeePopulationTenths += evacuation;
                        lines.Add(city.Name + " эвакуирует " + FormatPopulation(evacuation) + " жителей из-за осады.");
                    }
                    if (city.SiegeTurns == 2) lines.Add(city.Name + " теряет 15% выпуска из-за осады.");
                }
                else if (city.SiegeTurns > 0)
                {
                    city.SiegeTurns = 0;
                    city.EfficiencyBasisPoints = Math.Min(10000, city.EfficiencyBasisPoints + 1500);
                }
            }
        }

        private void ResolveFoodHealthGrowth(GameState state, TurnReport report, List<string> lines)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                CityYield yield;
                int foodOutput = turnYields.TryGetValue(city.Id, out yield) ? yield.FoodTenths : 0;
                int available = city.FoodTenths + foodOutput;
                int consumption = Math.Max(1, city.PopulationTenths);
                if (available < consumption)
                {
                    city.FoodTenths = 0;
                    city.FoodShortageTurns++;
                    city.Trust = Math.Max(0, city.Trust - 2);
                    city.Attraction = Math.Max(0, city.Attraction - 5);
                    if (city.FoodShortageTurns >= 2) city.Health = Math.Max(0, city.Health - 5);
                    if (city.FoodShortageTurns >= 3)
                    {
                        int deaths = Math.Min(5, Math.Max(1, city.PopulationTenths / 100));
                        deaths = Math.Min(deaths, Math.Max(0, city.PopulationTenths - 20));
                        city.PopulationTenths -= deaths;
                        report.DeathsTenths += deaths;
                        lines.Add(city.Name + ": голод унёс " + FormatPopulation(deaths) + " населения.");
                    }
                    else lines.Add(city.Name + ": дефицит еды, здоровье и доверие падают.");
                    if (city.FoodShortageTurns >= 2)
                    {
                        int outflow = Math.Min(Math.Max(1, city.PopulationTenths * 5 / 100), Math.Max(0, city.PopulationTenths - 20));
                        CityState destination = MostAttractiveDestination(state, city);
                        if (destination != null && outflow > 0)
                        {
                            city.PopulationTenths -= outflow;
                            destination.PopulationTenths += outflow;
                            report.EmigrationTenths += outflow;
                            lines.Add(FormatPopulation(outflow) + " жителей покинули " + city.Name + " из-за голода и переехали в " + destination.Name + ".");
                        }
                    }
                }
                else
                {
                    city.FoodShortageTurns = 0;
                    int afterConsumption = available - consumption;
                    int reserveTarget = consumption * Math.Max(1, Math.Min(4, city.FoodReserveTurns));
                    int overflow = Math.Max(0, afterConsumption - reserveTarget);
                    city.FoodTenths = Math.Min(city.FoodStoreCapacityTenths, Math.Min(afterConsumption, reserveTarget));
                    city.GrowthTenths += overflow;
                    while (city.GrowthTenths >= 80 && city.PopulationTenths + 10 <= city.HousingTenths && city.Health > 20 && city.Trust > 10)
                    {
                        city.GrowthTenths -= 80;
                        city.PopulationTenths += 10;
                        lines.Add(city.Name + " вырос: накопитель роста достиг 8 еды.");
                    }
                    if (city.PopulationTenths > city.HousingTenths)
                        city.PopulationTenths = city.HousingTenths;
                }

                CityYield currentYield;
                int coverage = turnYields.TryGetValue(city.Id, out currentYield) ? currentYield.ServiceCoverage : 0;
                int healthChange = currentYield == null ? 0 : currentYield.HealthChange;
                int safetyChange = currentYield == null ? 0 : currentYield.SafetyChange;
                int educationChange = currentYield == null ? 0 : currentYield.EducationChange;
                city.Health = Math.Min(100, city.Health + healthChange + coverage / 100);
                city.Safety = Math.Min(100, city.Safety + safetyChange);
                city.Education = Math.Min(100, city.Education + educationChange);
                if (city.Safety < 25) city.Trust = Math.Max(0, city.Trust - 1);
                else if (city.Safety > 70 && !AtWarWithAny(state, city.OwnerId)) city.Trust = Math.Min(100, city.Trust + 1);
                CivilizationState cityOwner = state.Civilization(city.OwnerId);
                if (cityOwner != null && cityOwner.HasLaw("local_councils") && city.AdministrativeLoad <= 0)
                    city.Trust = Math.Min(100, city.Trust + 1);
                if (cityOwner != null && cityOwner.HasLaw("limited_conscription") && AtWarWithAny(state, city.OwnerId))
                    city.Trust = Math.Max(0, city.Trust - 1);
                if (city.Environment < 30) city.Health = Math.Max(0, city.Health - 2);
                if (city.Environment > 70 && city.Health < 100) city.Health = Math.Min(100, city.Health + 1);
                city.Trust = Math.Max(0, Math.Min(100, city.Trust));
                city.Health = Math.Max(0, Math.Min(100, city.Health));
                city.Safety = Math.Max(0, Math.Min(100, city.Safety));
                city.Education = Math.Max(0, Math.Min(100, city.Education));
                city.Environment = Math.Max(0, Math.Min(100, city.Environment));
            }
        }

        private void ResolveMigrationAndRecovery(GameState state, TurnReport report, List<string> lines)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OccupiedById >= 0) continue;
                CityState destination = MostAttractiveDestination(state, city);
                if (destination == null) continue;
                int difference = CityAttraction(state, destination) - CityAttraction(state, city);
                if (difference < 25 || city.PopulationTenths <= 30 || destination.PopulationTenths >= destination.HousingTenths) continue;
                int flow = Math.Min(2, Math.Min(city.PopulationTenths - 20, destination.HousingTenths - destination.PopulationTenths));
                if (flow <= 0) continue;
                city.PopulationTenths -= flow;
                destination.PopulationTenths += flow;
                destination.Trust = Math.Min(100, destination.Trust + 1);
                report.EmigrationTenths += flow;
                lines.Add(FormatPopulation(flow) + " жителей добровольно переехали из " + city.Name + " в " + destination.Name + " из-за условий жизни.");
            }

            for (int c = 0; c < state.Civilizations.Length; c++)
            {
                CivilizationState civ = state.Civilizations[c];
                if (civ.RefugeePopulationTenths <= 0) continue;
                for (int i = 0; i < state.Cities.Length && civ.RefugeePopulationTenths > 0; i++)
                {
                    CityState city = state.Cities[i];
                    if (city.OwnerId != civ.Id || city.OccupiedById >= 0 || city.Trust < 35) continue;
                    int space = city.HousingTenths - city.PopulationTenths;
                    int returned = Math.Min(Math.Min(2, space), civ.RefugeePopulationTenths);
                    if (returned <= 0) continue;
                    civ.RefugeePopulationTenths -= returned;
                    city.PopulationTenths += returned;
                    lines.Add(FormatPopulation(returned) + " жителей вернулись в " + city.Name + " после восстановления безопасности.");
                }
            }

            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OccupiedById >= 0)
                {
                    city.Trust = Math.Max(0, city.Trust - 2);
                    city.Attraction = Math.Max(0, city.Attraction - 3);
                }
                else if (city.Trust < 60 && city.Health >= 50 && city.FoodShortageTurns == 0)
                    city.Trust = Math.Min(60, city.Trust + 1);
                city.Attraction = Math.Max(0, Math.Min(100, city.Attraction + (city.Health >= 60 ? 1 : -1)));
            }
        }
    }
}
