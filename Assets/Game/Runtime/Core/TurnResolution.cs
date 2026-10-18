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
            // a Heritage victory at the end of an 80-turn campaign.
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

                RefreshAiStrategy(state, civ);
                PlanAiBudget(state, civ);
                PlanAiInstitutions(state, civ);
                PlanAiServices(state, civ);
                ChooseAiResearch(state, civ);
                TryAiVictoryProjects(state, civ);
                for (int i = 0; i < state.Cities.Length; i++)
                {
                    CityState city = state.Cities[i];
                    if (city.OwnerId != civ.Id) continue;
                    CityYield yield = EconomyCalculator.Calculate(state, city);
                    if (city.FoodTenths + yield.FoodTenths < city.PopulationTenths * 2)
                        EconomyCalculator.AutoAssign(state, city, "food");
                    else EconomyCalculator.AutoAssign(state, city, AiCityFocus(state, civ, city));

                    if (city.Project != null && city.Project.Kind != ProjectKind.None) continue;
                    if (ShouldAiAdvanceEra(state, civ))
                    {
                        SetProject(state, civ, city, "era_transition", false);
                        continue;
                    }

                    if (city.Id == capital.Id && CanAiExpand(state, civ, capital))
                    {
                        Recruit(state, civ, capital, UnitKind.SettlerWagon, false);
                        continue;
                    }

                    string preferredBuilding = AiBuildingChoice(state, civ, city);
                    if (!string.IsNullOrEmpty(preferredBuilding)) SetProject(state, civ, city, preferredBuilding, false);
                }

                PlanAiDiplomacy(state, civ, capital);
                PlanAiDefense(state, civ);

                ResolveAiSettlers(state, civ);
            }
        }

        private void RefreshAiStrategy(GameState state, CivilizationState civ)
        {
            bool urgentFood = HasCriticalFoodProblem(state, civ.Id);
            bool urgentWar = IsThreatened(state, civ.Id) || AtWarWithAny(state, civ.Id);
            civ.StrategyHoldTurns = Math.Max(0, civ.StrategyHoldTurns - 1);
            if (civ.StrategyHoldTurns > 0 && !urgentFood && !urgentWar && civ.ConsecutiveDeficitTurns < 3) return;

            civ.StrategicGoal = ChooseAiGoal(state, civ);
            civ.StrategyHoldTurns = civ.Difficulty == Difficulty.Calm ? 7 :
                civ.Difficulty == Difficulty.Standard ? 6 : civ.Difficulty == Difficulty.Strong ? 5 : 4;
        }

        private void PlanAiBudget(GameState state, CivilizationState civ)
        {
            int projectedBalance = 0;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civ.Id) projectedBalance += EconomyCalculator.Calculate(state, state.Cities[i]).TreasuryTenths;

            TaxRate desired = civ.TaxRate;
            if (civ.TreasuryTenths < 300 || projectedBalance < 0) desired = TaxRate.High;
            else if (civ.TreasuryTenths >= 1500 && projectedBalance >= 30 && civ.TaxRate == TaxRate.High) desired = TaxRate.Normal;
            if (desired != civ.TaxRate) civ.TaxRate = desired;

            // AI cities start with three funded services, which can cost more than
            // their opening trade income. Stop discretionary spending before debt
            // compounds; the quality doctrine restores it once the budget can carry it.
            if (projectedBalance < 0 || civ.ConsecutiveDeficitTurns > 0)
            {
                for (int i = 0; i < state.Cities.Length; i++)
                {
                    CityState city = state.Cities[i];
                    if (city.OwnerId != civ.Id) continue;
                    if (city.ServiceFunding.Length > 1) city.ServiceFunding[1] = 0;
                    if (city.ServiceFunding.Length > 2) city.ServiceFunding[2] = 0;
                    if (city.ServiceFunding.Length > 0 && city.Health >= 60) city.ServiceFunding[0] = 0;
                }
            }
        }

        private void PlanAiServices(GameState state, CivilizationState civ)
        {
            if (AiVictoryDoctrine(state, civ) != "quality" && civ.StrategicGoal != "quality") return;

            int projectedBalance = 0;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civ.Id) projectedBalance += EconomyCalculator.Calculate(state, state.Cities[i]).TreasuryTenths;
            if (projectedBalance < 0 || civ.ConsecutiveDeficitTurns > 0) return;

            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civ.Id) continue;
                if (civ.ConsecutiveDeficitTurns > 1)
                {
                    city.ServiceFunding[1] = 0;
                    city.ServiceFunding[2] = 0;
                    continue;
                }
                if (city.Health < 75) city.ServiceFunding[0] = Math.Max(city.ServiceFunding[0], 1);
                else if (city.ServiceFunding[0] > 1) city.ServiceFunding[0] = 1;
                city.ServiceFunding[1] = city.Safety < 70 ? 1 : 0;
                city.ServiceFunding[2] = city.Education < 60 ? 1 : 0;
            }
        }

        private void PlanAiInstitutions(GameState state, CivilizationState civ)
        {
            if (civ.LawCooldownTurns > 0 || civ.InfluenceTenths - AiVictoryInfluenceReserve(state, civ) < 50 || civ.TreasuryTenths < 0) return;

            int cities = CountOwnedCities(state, civ.Id);
            int routes = CountRoutes(state, civ.Id);
            string goal = AiVictoryStrategicGoal(state, civ);
            string law = null;
            switch (goal)
            {
                case "science":
                    if ((int)civ.Era >= (int)Era.ConnectedLands && !civ.HasLaw("universal_education") && civ.TreasuryTenths >= 100)
                        law = "universal_education";
                    break;
                case "trade":
                    if ((int)civ.Era >= (int)Era.Cities && !civ.HasLaw("open_markets") && (routes > 0 || CountOwnedBuilding(state, civ.Id, "market") > 0))
                        law = "open_markets";
                    break;
                case "expansion":
                case "quality":
                case "recovery":
                    if ((int)civ.Era >= (int)Era.Cities && cities >= 2 && !civ.HasLaw("local_councils"))
                        law = "local_councils";
                    break;
                case "defense":
                    if (!AtWarWithAny(state, civ.Id) && !civ.HasLaw("volunteer_service") && CountMilitaryUnits(state, civ.Id) > 0)
                        law = "volunteer_service";
                    break;
            }

            if (law != null) EnactLaw(state, civ, law, false);
        }

        private int CountMilitaryUnits(GameState state, int civilizationId)
        {
            int count = 0;
            for (int i = 0; i < state.Units.Length; i++)
                if (state.Units[i].OwnerId == civilizationId && state.Units[i].Kind != UnitKind.SettlerWagon && state.Units[i].Strength > 0) count++;
            return count;
        }

        private int AiVictoryInfluenceReserve(GameState state, CivilizationState civ)
        {
            int reserve = 0;
            if ((int)civ.Difficulty >= (int)Difficulty.Strong)
            {
                string pursuit = AiVictoryDoctrine(state, civ);
                if (pursuit == "science") reserve = 80;
                else if (pursuit == "culture" || pursuit == "diplomatic") reserve = 200;
            }
            if (VictoryEvaluator.CanStartCulturalFestival(state, civ.Id)) reserve = 200;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civ.Id && VictoryEvaluator.CanStartScientificProgram(state, civ.Id, state.Cities[i].Id)) reserve = Math.Max(reserve, 80);
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId == civ.Id && city.Project == null && VictoryEvaluator.CanStartDiplomaticCoalition(state, civ.Id, city.Id)) reserve = Math.Max(reserve, 200);
            }
            return reserve;
        }

        private bool HasCriticalFoodProblem(GameState state, int civilizationId)
        {
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civilizationId) continue;
                CityYield yield = EconomyCalculator.Calculate(state, city);
                if (city.FoodShortageTurns > 0 || city.FoodTenths + yield.FoodTenths < city.PopulationTenths) return true;
            }
            return false;
        }

        private string AiCityFocus(GameState state, CivilizationState civ, CityState city)
        {
            CityYield yield = EconomyCalculator.Calculate(state, city);
            if (city.FoodShortageTurns > 0 || city.FoodTenths + yield.FoodTenths < city.PopulationTenths * 2) return "food";
            if (city.Project != null && city.Project.Kind == ProjectKind.ScientificProject)
                return city.Project.Progress < city.Project.Cost ? "industry" : "science";
            string plannedGoal = AiVictoryStrategicGoal(state, civ);
            if (civ.ConsecutiveDeficitTurns >= 4 || civ.TreasuryTenths < -300) return "trade";
            if (city.Project != null && city.Project.Kind != ProjectKind.None) return "industry";
            if (plannedGoal == "science" && HasAiScientificProject(state, civ.Id)) return "science";

            if (plannedGoal == "science") return "science";
            if (plannedGoal == "culture") return civ.CulturePrestige >= 100 && civ.TreasuryTenths < 1000 ? "trade" : "culture";
            if (plannedGoal == "trade") return "trade";

            switch (plannedGoal)
            {
                case "defense": return city.BuildingLevel("barracks") > 0 ? "industry" : "balanced";
                case "expansion": return city.BuildingLevel("workshop") > 0 ? "industry" : "balanced";
                case "recovery": return city.Health < 55 ? "culture" : "trade";
                case "quality":
                    if (city.Health < 55 || city.Safety < 55) return "culture";
                    if (city.Education < 40) return "science";
                    if (yield.TreasuryTenths < 0) return "trade";
                    return "balanced";
                default: return "balanced";
            }
        }

        private void TryAiVictoryProjects(GameState state, CivilizationState civ)
        {
            if (HasAiVictoryInProgress(state, civ)) return;
            string[] paths;
            string pursuit = AiVictoryDoctrine(state, civ);
            if (pursuit == "diplomatic") paths = new[] { "diplomatic", "science", "culture" };
            else if (pursuit == "culture") paths = new[] { "culture", "diplomatic", "science" };
            else if (pursuit == "quality") paths = new[] { "science", "culture", "diplomatic" };
            else paths = new[] { "science", "diplomatic", "culture" };
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civ.Id || city.Project == null || city.Project.Kind != ProjectKind.None) continue;
                for (int p = 0; p < paths.Length; p++)
                {
                    if (paths[p] == "science" && VictoryEvaluator.CanStartScientificProgram(state, civ.Id, city.Id) && civ.KnowledgeTenths >= 300 && civ.InfluenceTenths >= 80)
                    {
                        CityState projectCity = city;
                        int bestProduction = EconomyCalculator.Calculate(state, city).Production;
                        for (int candidateIndex = 0; candidateIndex < state.Cities.Length; candidateIndex++)
                        {
                            CityState candidate = state.Cities[candidateIndex];
                            if (candidate.OwnerId != civ.Id || !VictoryEvaluator.CanStartScientificProgram(state, civ.Id, candidate.Id)) continue;
                            CityYield candidateYield = EconomyCalculator.Calculate(state, candidate);
                            int production = candidateYield.Production + candidate.BuildingLevel("workshop") * 2;
                            int currentProduction = bestProduction + projectCity.BuildingLevel("workshop") * 2;
                            if (production > currentProduction)
                            {
                                projectCity = candidate;
                                bestProduction = candidateYield.Production;
                            }
                        }
                        BeginVictoryProject(state, civ, projectCity, VictoryPath.Scientific, false);
                        return;
                    }
                    if (paths[p] == "culture" && VictoryEvaluator.CanStartCulturalFestival(state, civ.Id) && civ.InfluenceTenths >= 200 && civ.TreasuryTenths >= 100)
                    {
                        BeginVictoryProject(state, civ, city, VictoryPath.Cultural, false);
                        return;
                    }
                    if (paths[p] == "diplomatic" && civ.InfluenceTenths >= 200 && VictoryEvaluator.CanStartDiplomaticCoalition(state, civ.Id, city.Id))
                    {
                        BeginVictoryProject(state, civ, city, VictoryPath.Diplomatic, false);
                        return;
                    }
                }
            }
        }

        private bool HasAiVictoryInProgress(GameState state, CivilizationState civ)
        {
            if (civ.CultureFestivalTurns > 0 || civ.DiplomaticCoalitionTurns > 0) return true;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civ.Id && state.Cities[i].Project != null &&
                    state.Cities[i].Project.Kind == ProjectKind.ScientificProject) return true;
            return false;
        }

        private bool HasAiScientificProject(GameState state, int civilizationId)
        {
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civilizationId && state.Cities[i].Project != null &&
                    state.Cities[i].Project.Kind == ProjectKind.ScientificProject) return true;
            return false;
        }

        private void ChooseAiResearch(GameState state, CivilizationState civ)
        {
            if (civ.ActiveResearchIndex >= 0 && civ.ActiveResearchIndex < RulesCatalog.Technologies.Length)
            {
                TechnologyDefinition active = RulesCatalog.Technologies[civ.ActiveResearchIndex];
                if (!civ.Knows(active.Id) && (int)active.Era <= (int)civ.Era) return;
            }
            int selected = -1;
            int bestScore = int.MinValue;
            for (int i = 0; i < RulesCatalog.Technologies.Length; i++)
            {
                TechnologyDefinition tech = RulesCatalog.Technologies[i];
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
                if (!prerequisites) continue;
                int score = -tech.Cost + AiTechnologyPreference(AiVictoryStrategicGoal(state, civ), tech.Id) + AiDoctrineTechnologyBonus(state, civ, tech.Id);
                if (civ.CompletedTechnologies <= 1 && tech.Id == AiOpeningResearchChoice(state, civ)) score += 120;
                if (tech.Id == "writing" && CountOwnedBuilding(state, civ.Id, "council") == 0) score += 18;
                if (tech.Id == "civic_schools" && CountOwnedBuilding(state, civ.Id, "school") == 0) score += 12;
                if (tech.Id == "coin_markets" && CountOwnedBuilding(state, civ.Id, "market") == 0) score += 12;
                if ((tech.Id == "medicine" || tech.Id == "public_health") && AiAverageCityHealth(state, civ.Id) < 65) score += 14;
                if (tech.Id == "stone" && (civ.Archetype == "Стратег безопасности" || AiDefensePressure(state, civ.Id) >= 35)) score += 20;
                if (tech.Id == "roads" && CountOwnedCities(state, civ.Id) >= 2) score += 10;
                if ((int)civ.Difficulty >= (int)Difficulty.Strong) score += AiRivalRaceBonus(state, civ, tech.Id);
                score += StableAiChoiceNudge(state, civ, i);
                if (score > bestScore) { bestScore = score; selected = i; }
            }
            civ.ActiveResearchIndex = selected;
        }

        private int AiTechnologyPreference(string goal, string technologyId)
        {
            switch (goal)
            {
                case "science":
                    if (technologyId == "writing" || technologyId == "civic_schools" || technologyId == "university" || technologyId == "computing_networks" ||
                        technologyId == "open_science" || technologyId == "mass_education" || technologyId == "electricity" || technologyId == "mechanization") return 48;
                    if (technologyId == "medicine" || technologyId == "public_health") return 18;
                    break;
                case "trade":
                    if (technologyId == "writing" || technologyId == "stone" || technologyId == "roads" || technologyId == "coin_markets" || technologyId == "trade_league" || technologyId == "international_assembly" || technologyId == "printing" || technologyId == "free_press") return 44;
                    if (technologyId == "coastal_craft") return 20;
                    break;
                case "culture":
                    if (technologyId == "writing" || technologyId == "printing" || technologyId == "free_press" || technologyId == "trade_league") return 42;
                    if (technologyId == "medicine" || technologyId == "public_health" || technologyId == "global_health") return 20;
                    break;
                case "defense":
                    if (technologyId == "stone" || technologyId == "roads" || technologyId == "engineering" || technologyId == "mechanization") return 45;
                    if (technologyId == "medicine" || technologyId == "public_health") return 20;
                    break;
                case "expansion":
                    if (technologyId == "stone" || technologyId == "roads" || technologyId == "irrigation" || technologyId == "engineering") return 42;
                    if (technologyId == "agriculture" || technologyId == "pottery") return 16;
                    break;
                case "recovery":
                    if (technologyId == "agriculture" || technologyId == "pottery" || technologyId == "irrigation" || technologyId == "medicine" || technologyId == "coin_markets") return 40;
                    if (technologyId == "public_health" || technologyId == "global_health") return 24;
                    break;
                default:
                    if (technologyId == "agriculture" || technologyId == "medicine" || technologyId == "public_health" || technologyId == "writing") return 30;
                    if (technologyId == "civic_schools" || technologyId == "printing" || technologyId == "clean_energy") return 18;
                    break;
            }
            return 0;
        }

        private string AiVictoryDoctrine(GameState state, CivilizationState civ)
        {
            int variant = AiPersonalityVariant(state, civ);
            if ((int)civ.Difficulty >= (int)Difficulty.Strong)
            {
                // Higher settings commit to recognizable plans instead of rolling
                // between routes. This gives the researcher and guardian a reliable
                // path to pressure the player while the trader remains diplomacy-first.
                if (civ.Archetype == "Учёный") return "science";
                if (civ.Archetype == "Торговец") return "diplomatic";
                if (civ.Archetype == "Стратег безопасности") return "quality";
            }
            if (civ.Archetype == "Учёный")
            {
                if (variant == 0 || variant == 3) return "science";
                return variant == 1 ? "quality" : "culture";
            }
            if (civ.Archetype == "Торговец")
            {
                if (variant == 0 || variant == 3) return "diplomatic";
                return variant == 1 ? "science" : "quality";
            }
            if (civ.Archetype == "Попечитель")
            {
                if (variant == 0 || variant == 3) return "culture";
                return variant == 1 ? "quality" : "diplomatic";
            }
            if (variant == 0 || variant == 3) return "quality";
            return variant == 1 ? "diplomatic" : "science";
        }

        private string AiVictoryStrategicGoal(GameState state, CivilizationState civ)
        {
            if (HasCriticalFoodProblem(state, civ.Id) || AtWarWithAny(state, civ.Id)) return civ.StrategicGoal;
            if ((int)civ.Difficulty < (int)Difficulty.Strong &&
                (civ.ConsecutiveDeficitTurns >= 4 || civ.TreasuryTenths < -300)) return civ.StrategicGoal;
            switch (AiVictoryDoctrine(state, civ))
            {
                case "science": return "science";
                case "culture": return "culture";
                case "diplomatic": return "trade";
                case "quality": return "quality";
                default: return civ.StrategicGoal;
            }
        }

        private string AiOpeningResearchChoice(GameState state, CivilizationState civ)
        {
            int variant = AiPersonalityVariant(state, civ);
            if (civ.Archetype == "Учёный")
            {
                if (variant == 0) return "writing";
                if (variant == 1) return "agriculture";
                if (variant == 2) return "pottery";
                return "stone";
            }
            if (civ.Archetype == "Торговец")
            {
                if (variant == 0) return "writing";
                if (variant == 1) return "stone";
                if (variant == 2) return "pottery";
                return "agriculture";
            }
            if (variant == 0) return "stone";
            if (variant == 1) return "agriculture";
            if (variant == 2) return "writing";
            return "pottery";
        }

        private int AiDoctrineTechnologyBonus(GameState state, CivilizationState civ, string technologyId)
        {
            string pursuit = AiVictoryDoctrine(state, civ);
            if (pursuit == "science")
            {
                if (technologyId == "writing" || technologyId == "civic_schools" || technologyId == "university" ||
                    technologyId == "mechanization" || technologyId == "electricity" || technologyId == "mass_education" ||
                    technologyId == "computing_networks" || technologyId == "open_science") return 22;
            }
            else if (pursuit == "culture")
            {
                if (technologyId == "writing" || technologyId == "civic_schools" || technologyId == "printing" ||
                    technologyId == "free_press" || technologyId == "trade_league") return 20;
            }
            else if (pursuit == "diplomatic")
            {
                if (technologyId == "writing" || technologyId == "coin_markets" || technologyId == "trade_league" ||
                    technologyId == "printing" || technologyId == "free_press" || technologyId == "international_assembly") return 22;
            }
            else if (pursuit == "quality" &&
                (technologyId == "agriculture" || technologyId == "irrigation" || technologyId == "medicine" ||
                    technologyId == "public_health" || technologyId == "global_health" || technologyId == "clean_energy")) return 18;
            return 0;
        }

        private int StableAiChoiceNudge(GameState state, CivilizationState civ, int candidateIndex)
        {
            unchecked
            {
                int value = state.Seed * 31 + civ.Id * 131 + candidateIndex * 17 + civ.CompletedTechnologies * 7;
                value = value & 0x7fffffff;
                return value % 5 - 2;
            }
        }

        private int AiRivalRaceBonus(GameState state, CivilizationState civ, string technologyId)
        {
            int best = 0;
            TechnologyDefinition technology = RulesCatalog.Technology(technologyId);
            if (technology == null) return 0;
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState rival = state.Civilizations[i];
                if (rival == null || rival.Id == civ.Id || rival.IsDefeated) continue;
                if ((int)rival.Era > (int)civ.Era && technology.KeyTechnology && AiTechnologyPreference(civ.StrategicGoal, technologyId) > 0)
                    best = Math.Max(best, 16);
                if (rival.CompletedTechnologies >= civ.CompletedTechnologies + 2 && technology.KeyTechnology &&
                    AiTechnologyPreference(civ.StrategicGoal, technologyId) > 0) best = Math.Max(best, 12);
            }
            return civ.Difficulty == Difficulty.Trial ? best * 2 : best;
        }

        private string AiBuildingChoice(GameState state, CivilizationState civ, CityState city)
        {
            string[] candidates = { "house", "granary", "clinic", "hospital", "council", "administration", "school", "university", "academy",
                "cultural_house", "archive", "exchange_house", "market", "workshop", "road_services", "assembly", "barracks" };
            string best = null;
            int bestScore = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (!CanAiBuild(state, civ, city, candidate)) continue;
                if ((candidate == "cultural_house" || candidate == "archive" || candidate == "exchange_house") &&
                    !CanPlaceAiCulturalBuilding(state, civ, city, candidate)) continue;
                int baseScore = AiBuildingScore(state, civ, city, candidate);
                if (baseScore <= 0) continue;
                int score = baseScore + StableAiChoiceNudge(state, civ, i);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            for (int i = 0; i < RulesCatalog.Buildings.Length; i++)
            {
                BuildingDefinition district = RulesCatalog.Buildings[i];
                if (district.RequiredNationId != civ.NationId || !CanAiBuild(state, civ, city, district.Id)) continue;
                int baseScore = AiBuildingScore(state, civ, city, district.Id);
                if (baseScore <= 0) continue;
                int score = baseScore + StableAiChoiceNudge(state, civ, 50 + i);
                if (score > bestScore) { bestScore = score; best = district.Id; }
            }
            return best;
        }

        private int AiBuildingScore(GameState state, CivilizationState civ, CityState city, string id)
        {
            int cities = CountOwnedCities(state, civ.Id);
            string goal = AiVictoryStrategicGoal(state, civ);
            int score = 0;
            if (id == "house")
            {
                int room = city.HousingTenths - city.PopulationTenths;
                score = room <= 0 ? 110 : room <= 20 ? 80 : room <= 40 ? 50 : room <= 60 ? 20 : 0;
            }
            else if (id == "granary") score = city.FoodShortageTurns > 0 ? 100 : city.FoodTenths < city.PopulationTenths * 2 ? 48 : 0;
            else if (id == "clinic" || id == "hospital") score = city.Health < 55 ? 78 : city.Health < 70 ? 48 : city.Health < 82 ? 18 : 0;
            else if (id == "council") score = CountOwnedBuilding(state, civ.Id, "council") == 0 ? 92 : 0;
            else if (id == "administration") score = cities >= civ.AdministrativeCapacity ? 76 : civ.AdministrativeCapacity <= cities + 1 ? 42 : 0;
            else if (id == "school") score = (goal == "science" || goal == "quality" || goal == "culture") ? (city.Education < 55 ? 66 : 42) : city.Education < 35 ? 22 : 0;
            else if (id == "university") score = (goal == "science" ? 82 : goal == "quality" ? 50 : 0) - CountCitiesWithBuilding(state, civ.Id, "university") * 12;
            else if (id == "academy") score = goal == "science" ? 82 : goal == "quality" ? 30 : 0;
            else if (id == "cultural_house" || id == "archive" || id == "exchange_house")
                score = goal == "culture" ? 76 : goal == "quality" ? 38 : civ.CulturePrestige >= 100 ? 28 : 0;
            else if (id == "market") score = goal == "trade" ? 72 : civ.TreasuryTenths < 100 ? 58 : 0;
            else if (id == "workshop") score = goal == "expansion" || goal == "defense" ? 58 : city.BuildingLevel("workshop") == 0 && EconomyCalculator.Calculate(state, city).Production < 2 ? 24 : 0;
            else if (id == "road_services") score = goal == "defense" || city.Safety < 55 ? 58 : 0;
            else if (id == "assembly") score = goal == "trade" ? 84 : civ.DiplomaticCoalitionTurns > 0 ? 40 : 0;
            else if (id == "barracks")
            {
                int pressure = AiDefensePressure(state, civ.Id);
                score = AtWarWithAny(state, civ.Id) ? 112 : pressure >= 40 ? 82 : civ.Archetype == "Стратег безопасности" ? 54 : 0;
            }
            else if (id == "jericho_spring_tower") score = city.FoodTenths < city.PopulationTenths * 2 ? 76 : 42;
            else if (id == "sumer_ziggurat_archive") score = AiVictoryDoctrine(state, civ) == "science" ? 78 : 46;
            else if (id == "egypt_nilometer") score = city.FoodShortageTurns > 0 || city.FoodTenths < city.PopulationTenths * 2 ? 78 : 40;
            else if (id == "indus_great_bath") score = city.Health < 70 ? 82 : 44;

            string pursuit = AiVictoryDoctrine(state, civ);
            if (id == "administration" && (int)civ.Difficulty >= (int)Difficulty.Strong &&
                cities >= civ.AdministrativeCapacity && civ.AdministrativeCapacity < 4 && civ.Knows("engineering"))
                score = Math.Max(score, 118);
            if (pursuit == "science")
            {
                if (id == "university" && CountCitiesWithBuilding(state, civ.Id, "university") < 2) score = Math.Max(score, 104);
                if (id == "academy" && civ.Knows("computing_networks")) score = Math.Max(score, 112);
            }
            else if (pursuit == "culture" && (id == "cultural_house" || id == "archive" || id == "exchange_house")) score = Math.Max(score, 104);
            else if (pursuit == "diplomatic" && id == "assembly" && civ.Knows("international_assembly")) score = Math.Max(score, 112);

            int age = (int)civ.Era;
            if ((id == "school" || id == "market" || id == "workshop") && age == 0) score -= 12;
            if (id == "house" && city.UnemploymentTurns > 1) score -= 8;
            if (civ.ConsecutiveDeficitTurns > 1 && RulesCatalog.Building(id).UpkeepTenths > 15) score -= 18;
            return score;
        }

        private bool CanAiBuild(GameState state, CivilizationState civ, CityState city, string id)
        {
            BuildingDefinition building = RulesCatalog.Building(id);
            if (building == null || city.BuildingLevel(id) > 0 || !HasDistrictPlot(state, city, id)) return false;
            if (!string.IsNullOrEmpty(building.RequiredNationId) && building.RequiredNationId != civ.NationId) return false;
            if (building.RequiredTechnology != null && !civ.Knows(building.RequiredTechnology)) return false;
            if (building.UniqueEmpireBuilding && CountOwnedBuilding(state, civ.Id, id) > 0) return false;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityProject project = state.Cities[i].Project;
                if (state.Cities[i].OwnerId == civ.Id && project != null && project.Kind == ProjectKind.Building && project.TargetId == id) return false;
            }
            return true;
        }

        private bool CanPlaceAiCulturalBuilding(GameState state, CivilizationState civ, CityState city, string id)
        {
            if (CountOwnedBuilding(state, civ.Id, id) > 0) return false;
            bool cityHasCulturalBuilding = city.BuildingLevel("cultural_house") > 0 || city.BuildingLevel("archive") > 0 || city.BuildingLevel("exchange_house") > 0;
            if (city.Project != null && city.Project.Kind == ProjectKind.Building &&
                (city.Project.TargetId == "cultural_house" || city.Project.TargetId == "archive" || city.Project.TargetId == "exchange_house")) cityHasCulturalBuilding = true;
            return !cityHasCulturalBuilding;
        }

        private bool CanAiExpand(GameState state, CivilizationState civ, CityState capital)
        {
            int populationReserve = civ.Difficulty == Difficulty.Calm ? 100 : civ.Difficulty == Difficulty.Trial ? 70 : 80;
            int foodReserve = civ.Difficulty == Difficulty.Calm ? 100 : civ.Difficulty == Difficulty.Trial ? 50 : 60;
            int cityCount = CountOwnedCities(state, civ.Id);
            if (cityCount >= Math.Min(4, civ.AdministrativeCapacity) || capital.PopulationTenths < populationReserve || capital.FoodTenths < foodReserve) return false;
            if (cityCount >= 3 && (civ.ConsecutiveDeficitTurns > 1 || civ.TreasuryTenths < 0)) return false;
            for (int i = 0; i < state.Units.Length; i++)
                if (state.Units[i].OwnerId == civ.Id && state.Units[i].Kind == UnitKind.SettlerWagon) return false;
            return true;
        }

        private bool ShouldAiAdvanceEra(GameState state, CivilizationState civ)
        {
            if ((int)civ.Era >= 4 || civ.InfluenceTenths < 100 || CountOwnedBuilding(state, civ.Id, "council") == 0) return false;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civ.Id && state.Cities[i].Project != null && state.Cities[i].Project.Kind == ProjectKind.EraTransition) return false;
            int eraKnowledge = 0;
            for (int i = 0; i < civ.KnownTechnologies.Length; i++)
            {
                TechnologyDefinition tech = RulesCatalog.Technology(civ.KnownTechnologies[i]);
                if (tech != null && tech.Era == civ.Era) eraKnowledge++;
            }
            return eraKnowledge >= 3;
        }

        private void ResolveAiSettlers(GameState state, CivilizationState civ)
        {
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState wagon = state.Units[i];
                if (wagon.OwnerId != civ.Id || wagon.Kind != UnitKind.SettlerWagon) continue;
                if (CountOwnedCities(state, civ.Id) >= Math.Min(4, civ.AdministrativeCapacity)) continue;
                CityState source = state.City(wagon.CitySourceId);
                if (source == null) continue;
                HexCell target = FindSettlerCell(state, source);
                if (target == null) continue;
                if (wagon.Q == target.Q && wagon.R == target.R)
                {
                    FoundCity(state, civ, wagon.Id, false);
                    i--;
                    continue;
                }
                HexCell step = FindPathStep(state, civ.Id, wagon, target);
                if (step == null || wagon.MovementLeft <= 0) continue;
                if (!MoveUnit(state, civ, wagon.Id, step.Q, step.R, false).Accepted) continue;
                if (wagon.Q == target.Q && wagon.R == target.R)
                {
                    FoundCity(state, civ, wagon.Id, false);
                    i--;
                }
            }
        }

        private HexCell FindPathStep(GameState state, int civilizationId, UnitState unit, HexCell target)
        {
            var queue = new Queue<HexCell>();
            var parent = new Dictionary<int, int>();
            int start = CellKey(unit.Q, unit.R);
            int goal = CellKey(target.Q, target.R);
            HexCell startCell = state.Cell(unit.Q, unit.R);
            if (startCell == null) return null;
            queue.Enqueue(startCell);
            parent[start] = start;
            while (queue.Count > 0)
            {
                HexCell current = queue.Dequeue();
                int currentKey = CellKey(current.Q, current.R);
                if (currentKey == goal) break;
                for (int d = 0; d < HexGrid.Directions.GetLength(0); d++)
                {
                    int q = current.Q + HexGrid.Directions[d, 0];
                    int r = current.R + HexGrid.Directions[d, 1];
                    int key = CellKey(q, r);
                    if (parent.ContainsKey(key)) continue;
                    HexCell next = state.Cell(q, r);
                    if (next == null || next.Terrain == TerrainKind.Mountain || next.Terrain == TerrainKind.Water) continue;
                    if (next.OwnerId >= 0 && next.OwnerId != civilizationId && !HasTreaty(state, TreatyKind.OpenBorders, civilizationId, next.OwnerId)) continue;
                    parent[key] = currentKey;
                    queue.Enqueue(next);
                }
            }
            if (!parent.ContainsKey(goal)) return null;
            int cursor = goal;
            while (parent[cursor] != start)
            {
                cursor = parent[cursor];
                if (cursor == start) return null;
            }
            return state.Cell(cursor / 100, cursor % 100);
        }

        private static int CellKey(int q, int r) { return q * 100 + r; }

        private string ChooseAiGoal(GameState state, CivilizationState civ)
        {
            int science = 18;
            int trade = 16;
            int culture = 12;
            int quality = 24;
            int defense = 10;
            int recovery = 6;
            int expansion = 18;
            int personality = AiPersonalityVariant(state, civ);
            string pursuit = AiVictoryDoctrine(state, civ);
            if (civ.Archetype == "Учёный")
            {
                science += 40;
                if (personality == 0) quality += 7;
                else if (personality == 1) { culture += 20; quality += 5; }
                else if (personality == 2) trade += 20;
                else expansion += 18;
            }
            else if (civ.Archetype == "Торговец")
            {
                if (personality == 0) { trade += 42; expansion += 8; }
                else if (personality == 1) { trade += 30; expansion += 32; }
                else if (personality == 2) { trade += 30; culture += 26; }
                else { trade += 30; science += 22; }
            }
            else if (civ.Archetype == "Попечитель")
            {
                culture += personality == 0 ? 38 : 30;
                if (personality == 0) quality += 22;
                else if (personality == 1) quality += 18;
                else if (personality == 2) trade += 14;
                else recovery += 15;
            }
            else if (civ.Archetype == "Стратег безопасности")
            {
                if (personality == 0) { defense += 42; quality += 8; }
                else if (personality == 1) { defense += 28; expansion += 34; }
                else if (personality == 2) { defense += 28; recovery += 30; }
                else { defense += 28; trade += 26; }
            }

            int cities = CountOwnedCities(state, civ.Id);
            int routes = CountRoutes(state, civ.Id);
            int foodProblems = 0;
            int healthyCities = 0;
            int averageEducation = 0;
            int cityTotal = 0;
            CityState capital = null;
            int knowledgeOutput = 0;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civ.Id) continue;
                if (capital == null || city.AdministrativeLoad > capital.AdministrativeLoad) capital = city;
                CityYield yield = EconomyCalculator.Calculate(state, city);
                if (city.FoodShortageTurns > 0 || city.FoodTenths + yield.FoodTenths < city.PopulationTenths * 2) foodProblems++;
                if (city.Health >= 70 && city.Safety >= 65) healthyCities++;
                averageEducation += city.Education;
                knowledgeOutput += yield.KnowledgeTenths;
                cityTotal++;
            }
            if (cityTotal > 0) averageEducation /= cityTotal;

            if (routes == 0) trade += civ.Archetype == "Торговец" ? 22 : 5;
            else trade += Math.Min(18, routes * 6);
            if (CountOwnedBuilding(state, civ.Id, "market") > 0) trade += 8;
            if (CountOwnedBuilding(state, civ.Id, "university") > 0 || CountOwnedBuilding(state, civ.Id, "school") > 0) science += 12;
            if (knowledgeOutput < 30 && civ.CompletedTechnologies >= 4) science += 10;
            if (CountOwnedBuilding(state, civ.Id, "cultural_house") > 0 || civ.CulturePrestige >= 50) culture += 12;
            if (averageEducation < 45) { science += 8; quality += 7; }
            if (cities >= 4 && healthyCities * 100 >= cities * 75 && civ.ConsecutiveDeficitTurns == 0) quality += 28;
            if (cities < Math.Min(4, civ.AdministrativeCapacity) && capital != null && capital.PopulationTenths >= 60 && capital.FoodTenths >= 40)
                expansion += 25;
            else if (cities >= Math.Min(4, civ.AdministrativeCapacity)) expansion -= 18;

            int defensePressure = AiDefensePressure(state, civ.Id);
            defense += defensePressure;
            if (AtWarWithAny(state, civ.Id)) defense += 70;
            if (foodProblems > 0) recovery += 12 + foodProblems * 22;
            if (civ.DebtTenths > 0 || civ.ConsecutiveDeficitTurns > 0) recovery += 26 + Math.Min(30, civ.ConsecutiveDeficitTurns * 10);
            if (civ.TreasuryTenths < 80) recovery += 18;

            if (civ.Knows("computing_networks") && civ.Knows("open_science") && CountOwnedBuilding(state, civ.Id, "university") >= 2 && CountOwnedBuilding(state, civ.Id, "academy") > 0)
                science += 90;
            if (VictoryEvaluator.CanStartCulturalFestival(state, civ.Id)) culture += 90;
            if (civ.Knows("international_assembly")) trade += 8;
            if (civ.Knows("international_assembly") && CountOwnedBuilding(state, civ.Id, "assembly") > 0 && routes >= 2) trade += 26;
            if (cities >= 4 && healthyCities < cities) quality += 12;

            // Each rival chooses a seed-stable long-term victory route. Short-term needs
            // still outrank it during a food emergency, active war, or sustained insolvency.
            if (pursuit == "science") science += 62;
            else if (pursuit == "culture") culture += 62;
            else if (pursuit == "diplomatic") trade += 62;
            else if (pursuit == "quality")
            {
                quality += 58;
                if (cities < 4) expansion += 22;
            }

            // Stronger rivals notice which parts of their heritage score trail the field and repair that gap.
            if ((int)civ.Difficulty >= (int)Difficulty.Strong)
            {
                HeritageScoreBreakdown own = VictoryEvaluator.HeritageBreakdown(state, civ.Id);
                int ownPublicProgress = AiPublicProgress(civ);
                int bestRivalProgress = -1;
                for (int i = 0; i < state.Civilizations.Length; i++)
                    if (state.Civilizations[i].Id != civ.Id && !state.Civilizations[i].IsDefeated)
                        bestRivalProgress = Math.Max(bestRivalProgress, AiPublicProgress(state.Civilizations[i]));
                if (bestRivalProgress >= 0 && bestRivalProgress - ownPublicProgress >= 10)
                {
                    int response = civ.Difficulty == Difficulty.Trial ? 24 : 14;
                    if (own.EducationAndTechnology < 40) science += response;
                    if (own.CulturalContribution < 40) culture += response;
                    if (own.QualityOfLife < 55) quality += response;
                    if (own.TrustAndAgreements < 45) trade += response - 2;
                    if (own.Sustainability < 45) recovery += response - 2;
                }
            }

            // A small seed-based tie breaker gives equivalent starts different plans without nondeterministic saves.
            science += GoalTieBreaker(state, civ, 1);
            trade += GoalTieBreaker(state, civ, 2);
            culture += GoalTieBreaker(state, civ, 3);
            quality += GoalTieBreaker(state, civ, 4);
            defense += GoalTieBreaker(state, civ, 5);
            recovery += GoalTieBreaker(state, civ, 6);
            expansion += GoalTieBreaker(state, civ, 7);

            string selected = "quality";
            int best = quality;
            SelectAiGoal("science", science, ref selected, ref best);
            SelectAiGoal("trade", trade, ref selected, ref best);
            SelectAiGoal("culture", culture, ref selected, ref best);
            SelectAiGoal("defense", defense, ref selected, ref best);
            SelectAiGoal("recovery", recovery, ref selected, ref best);
            SelectAiGoal("expansion", expansion, ref selected, ref best);
            return selected;
        }

        private static void SelectAiGoal(string goal, int score, ref string selected, ref int best)
        {
            if (score > best) { best = score; selected = goal; }
        }

        private int GoalTieBreaker(GameState state, CivilizationState civ, int candidate)
        {
            int value = StableAiChoiceNudge(state, civ, candidate + 101);
            return value;
        }

        private int AiPersonalityVariant(GameState state, CivilizationState civ)
        {
            unchecked
            {
                int value = state.Seed * 486187739 + civ.Id * 16777619;
                value ^= value >> 13;
                value &= 0x7fffffff;
                return value % 4;
            }
        }

        private int AiAverageCityHealth(GameState state, int civilizationId)
        {
            int total = 0;
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civilizationId) { total += state.Cities[i].Health; count++; }
            return count == 0 ? 0 : total / count;
        }

        private int AiDefensePressure(GameState state, int civilizationId)
        {
            if (AtWarWithAny(state, civilizationId)) return 100;
            int pressure = 0;
            for (int c = 0; c < state.Cities.Length; c++)
            {
                CityState city = state.Cities[c];
                if (city.OwnerId != civilizationId) continue;
                for (int u = 0; u < state.Units.Length; u++)
                {
                    UnitState unit = state.Units[u];
                    if (unit.OwnerId == civilizationId || unit.Strength < 20) continue;
                    HexCell observedCell = state.Cell(unit.Q, unit.R);
                    if (observedCell == null || (observedCell.VisibleToMask & (1 << civilizationId)) == 0) continue;
                    CivilizationState rival = state.Civilization(unit.OwnerId);
                    if (rival == null || rival.IsDefeated) continue;
                    int distance = HexGrid.Distance(city.Q, city.R, unit.Q, unit.R);
                    int rivalThreat = rival.Threats != null && civilizationId < rival.Threats.Length ? rival.Threats[civilizationId] : 0;
                    int relation = rival.Relations != null && civilizationId < rival.Relations.Length ? rival.Relations[civilizationId] : 0;
                    if (distance <= 2) pressure = Math.Max(pressure, 45 + rivalThreat / 2 - relation / 5);
                    else if (distance <= 4) pressure = Math.Max(pressure, 22 + rivalThreat / 3 - relation / 8);
                }
            }
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState rival = state.Civilizations[i];
                if (rival == null || rival.Id == civilizationId || rival.IsDefeated || rival.Threats == null || civilizationId >= rival.Threats.Length) continue;
                pressure = Math.Max(pressure, rival.Threats[civilizationId] / 2);
            }
            return Math.Max(0, Math.Min(100, pressure));
        }

        private int AiPublicProgress(CivilizationState civ)
        {
            return (int)civ.Era * 24 + civ.CompletedTechnologies * 3 + civ.Reputation / 4 + civ.CulturePrestige / 10;
        }

        private void PlanAiDiplomacy(GameState state, CivilizationState civ, CityState capital)
        {
            string goal = AiVictoryStrategicGoal(state, civ);
            string pursuit = AiVictoryDoctrine(state, civ);
            bool tradePriority = goal == "trade" || civ.Archetype == "Торговец" ||
                (CountRoutes(state, civ.Id) == 0 && (civ.TreasuryTenths < 400 || goal == "recovery"));
            int protectedInfluence = AiVictoryInfluenceReserve(state, civ);
            if (tradePriority && civ.InfluenceTenths - protectedInfluence >= TreatyCost(TreatyKind.TradeRoute) && CountRoutes(state, civ.Id) < TradeSlots(state, civ.Id))
            {
                int partner = FindBestAiTradePartner(state, civ.Id);
                if (partner >= 0)
                {
                    CityState destination = FirstCityOf(state, partner);
                    if (destination != null) ProposeTreaty(state, civ, partner, TreatyKind.TradeRoute, capital.Id, destination.Id, false);
                }
            }

            TreatyKind kind;
            if (pursuit == "diplomatic") kind = TreatyKind.OpenBorders;
            else if (goal == "science") kind = TreatyKind.KnowledgeExchange;
            else if (goal == "defense") kind = TreatyKind.NonAggression;
            else if (goal == "recovery" && AiDefensePressure(state, civ.Id) >= 30) kind = TreatyKind.NonAggression;
            else if (goal == "culture" || goal == "quality" || goal == "expansion" || goal == "recovery") kind = TreatyKind.OpenBorders;
            else return;

            if (AiDefensePressure(state, civ.Id) >= 55 && goal != "science") kind = TreatyKind.NonAggression;

            if (civ.InfluenceTenths - protectedInfluence < TreatyCost(kind) + 10) return;
            for (int i = 0; i < state.Treaties.Length; i++)
                if ((state.Treaties[i].FromCivilizationId == civ.Id || state.Treaties[i].ToCivilizationId == civ.Id) &&
                    state.Treaties[i].Kind != TreatyKind.TradeRoute && (state.Treaties[i].Active || state.Treaties[i].Pending)) return;
            int diplomaticPartner = FindBestAiDiplomacyPartner(state, civ.Id, kind);
            if (diplomaticPartner < 0) return;
            CityState partnerCity = FirstCityOf(state, diplomaticPartner);
            if (partnerCity == null) return;
            ProposeTreaty(state, civ, diplomaticPartner, kind, capital.Id, partnerCity.Id, false);
        }

        private int FindBestAiTradePartner(GameState state, int civilizationId)
        {
            int best = -1;
            int bestScore = int.MinValue;
            CivilizationState civ = state.Civilization(civilizationId);
            CityState origin = FirstCityOf(state, civilizationId);
            if (civ == null || origin == null) return -1;
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState target = state.Civilizations[i];
                if (target == null || target.Id == civilizationId || target.IsDefeated || AtWar(state, civilizationId, target.Id) ||
                    HasAiTreatyRecord(state, civilizationId, target.Id, TreatyKind.TradeRoute)) continue;
                CityState destination = FirstCityOf(state, target.Id);
                if (destination == null || !RouteExists(state, origin, destination)) continue;
                int score = target.Trusts[civ.Id] + target.Relations[civ.Id] - target.Threats[civ.Id] / 2;
                if (target.Archetype == "Торговец") score += 10;
                if (score > bestScore) { bestScore = score; best = target.Id; }
            }
            return best;
        }

        private int FindBestAiDiplomacyPartner(GameState state, int civilizationId, TreatyKind kind)
        {
            int best = -1;
            int bestScore = int.MinValue;
            CivilizationState civ = state.Civilization(civilizationId);
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState target = state.Civilizations[i];
                if (target == null || target.Id == civilizationId || target.IsDefeated || AtWar(state, civilizationId, target.Id) ||
                    HasAiTreatyRecord(state, civilizationId, target.Id, kind)) continue;
                int acceptance = target.Trusts[civ.Id] + target.Relations[civ.Id] / 2 - target.Threats[civ.Id] / 2;
                if (target.Archetype == "Торговец" && kind == TreatyKind.TradeRoute) acceptance += 25;
                if (target.Archetype == "Попечитель" && (kind == TreatyKind.OpenBorders || kind == TreatyKind.NonAggression)) acceptance += 15;
                if (kind == TreatyKind.TradeRoute) acceptance += 15;
                if (acceptance < 25) continue;
                int score = acceptance;
                if (civ.StrategicGoal == "defense") score += civ.Threats[target.Id] * 2;
                if (civ.StrategicGoal == "science") score += Math.Abs(civ.KnowledgeTenths - target.KnowledgeTenths) / 10;
                if (score > bestScore) { bestScore = score; best = target.Id; }
            }
            return best;
        }

        private bool HasAiTreatyRecord(GameState state, int first, int second, TreatyKind kind)
        {
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (treaty.Kind != kind) continue;
                if (!treaty.Active && !treaty.Pending && treaty.Fulfilled && !treaty.Broken) continue;
                if ((treaty.FromCivilizationId == first && treaty.ToCivilizationId == second) ||
                    (treaty.FromCivilizationId == second && treaty.ToCivilizationId == first)) return true;
            }
            return false;
        }

        private void PlanAiDefense(GameState state, CivilizationState civ)
        {
            bool atWar = AtWarWithAny(state, civ.Id);
            int pressure = AiDefensePressure(state, civ.Id);
            int units = 0;
            for (int i = 0; i < state.Units.Length; i++)
                if (state.Units[i].OwnerId == civ.Id && state.Units[i].Kind != UnitKind.SettlerWagon && state.Units[i].Strength > 0) units++;
            int desired = atWar ? Math.Min(3, 1 + (int)civ.Difficulty / 2 + CountOwnedCities(state, civ.Id) / 3 + (civ.Difficulty == Difficulty.Trial ? 1 : 0)) :
                (civ.Difficulty == Difficulty.Trial && pressure >= 65 ? 2 : civ.Archetype == "Стратег безопасности" || pressure >= 65 ? 1 : 0);
            if (units >= desired || civ.TreasuryTenths < 60) return;

            CityState city = null;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civ.Id && state.Cities[i].BuildingLevel("barracks") > 0 &&
                    (city == null || state.Cities[i].AdministrativeLoad > city.AdministrativeLoad)) city = state.Cities[i];
            if (city == null || city.Project == null || city.Project.Kind != ProjectKind.None || city.PopulationTenths < 50) return;

            UnitKind kind = civ.Knows("roads") && civ.TreasuryTenths >= 100 ? UnitKind.FieldForce : UnitKind.Militia;
            Recruit(state, civ, city, kind, false);
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
                    if (city.Project.Kind == ProjectKind.ScientificProject && city.Project.Progress >= city.Project.Cost)
                        city.Project.Progress = city.Project.Cost;
                    else city.Project.Progress += yield.Production;
                    city.Project.TurnsActive++;
                }
                if (city.IntegrationTurns > 0) city.IntegrationTurns--;
                if (city.WarDamageTurns > 0) city.WarDamageTurns--;
            }

            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                if (civ.IsDefeated) continue;
                civ.PreviousTurnKnowledgeOutputTenths = civ.LastTurnKnowledgeOutputTenths;
                civ.LastTurnKnowledgeOutputTenths = knowledgeProduced[i];
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
            if (!civ.IsHuman)
            {
                if (civ.Difficulty == Difficulty.Calm && civ.CompletedTechnologies + 2 < CivLeadTechnologyCount(state, civ.Id)) scienceRate = 11000;
                else if (civ.Difficulty == Difficulty.Strong) scienceRate = 10500;
                else if (civ.Difficulty == Difficulty.Trial) scienceRate = 11000;
            }
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
                    MarkTreatyBroken(state, treaty);
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
                    bool districtPlaced = project.DistrictPlotReserved
                        ? PlaceDistrict(state, city, building.Id, project.TargetQ, project.TargetR)
                        : PlaceDistrict(state, city, building.Id);
                    HexCell placedDistrict = districtPlaced ? FindDistrictCell(state, city, building.Id) : null;
                    city.HousingTenths += building.HousingTenths;
                    city.FoodStoreCapacityTenths += building.FoodStorageTurns * city.PopulationTenths;
                    city.Health = Math.Min(100, city.Health + building.Health);
                    city.Safety = Math.Min(100, city.Safety + building.Safety);
                    city.Education = Math.Min(100, city.Education + building.Education);
                    CivilizationState owner = state.Civilization(city.OwnerId);
                    if (building.AdminCapacity > 0 && owner != null) owner.AdministrativeCapacity += building.AdminCapacity;
                    if (building.CulturalObject && Array.IndexOf(city.CompletedCulturalIds, building.Id) < 0)
                        Append(ref city.CompletedCulturalIds, building.Id);
                    lines.Add(city.Name + ": завершено строительство «" + building.NameRu + "»" +
                        (placedDistrict != null ? "; район занял клетку " + placedDistrict.Q + "," + placedDistrict.R + "." : "; свободная клетка для района не найдена."));
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
                    if (owner != null && owner.ScienceProjectTurns < 4)
                    {
                        owner.ScienceProjectTurns++;
                        if (owner.ScienceProjectTurns >= 4)
                        {
                            owner.ScienceProjectTurns = 4;
                            lines.Add(owner.Name + " завершает международную научную программу; проверяются последние показатели знаний.");
                        }
                    }
                    continue;
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
