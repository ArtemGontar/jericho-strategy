using System;
using System.Collections.Generic;
using System.Text.Json;
using Nasledie.Core;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RunCampaignPaceAcceptance();
            RunAllDifficultiesToCompletion();
            AiActiveVictoryBalanceScenario.Run(Console.WriteLine);
            RunAiProgressionAcceptance();
            RunAiStrategicDiversityAcceptance();
            RunAiCrisisResponseAcceptance();
            RunAiBudgetAndInstitutionAcceptance();
            RunAiQualityServicePlanningAcceptance();
            RunAiVictoryCommitmentAcceptance();
            RunAiDifficultyScalingAcceptance();
            RunFairStarterEconomyAcceptance();
            MechanicsRegressionScenario.Run(Console.WriteLine);
            RunScienceFocusAssignmentAcceptance();
            RunInteractiveCampaignToCompletion();
            RunSaveResumeCampaign();
            RunPunitiveWarAndRecovery();
            RunBattleEventCaptureAcceptance();
            RunVictoryPathAcceptance();
            ActiveQualityVictoryScenario.Run(Console.WriteLine);
            FreshCulturalVictoryScenario.Run(Console.WriteLine);
            FreshDiplomaticVictoryScenario.Run(Console.WriteLine);
            FreshScienceVictoryScenario.Run(Console.WriteLine);
            RunScientificVictoryLiveProjectAcceptance();
            RunUniqueEmpireBuildingQueueAcceptance();
            RunEraTransitionAcceptance();
            Console.WriteLine("PASS: all headless campaign scenarios reached a valid ending.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
    }

    private static void RunCampaignPaceAcceptance()
    {
        GameSimulation relaxed = GameSimulation.NewCampaign(50391, Difficulty.Standard, GamePace.Relaxed);
        GameSimulation standard = GameSimulation.NewCampaign(50391, Difficulty.Standard, GamePace.Standard);
        GameSimulation quick = GameSimulation.NewCampaign(50391, Difficulty.Standard, GamePace.Quick);
        Assert(relaxed.State.Pace == GamePace.Relaxed && relaxed.State.MaximumTurn == 120, "relaxed pace must create a 120-turn campaign");
        Assert(standard.State.Pace == GamePace.Standard && standard.State.MaximumTurn == 80, "standard pace must preserve the 80-turn campaign");
        Assert(quick.State.Pace == GamePace.Quick && quick.State.MaximumTurn == 60, "quick pace must create a 60-turn campaign");
        Assert(GamePacing.IsGlobalCrisisTurn(GamePace.Relaxed, 24) && GamePacing.IsGlobalCrisisTurn(GamePace.Relaxed, 42), "relaxed global crises must start on turn 24 and recur every 18 turns");
        Assert(GamePacing.IsGlobalCrisisTurn(GamePace.Standard, 16) && GamePacing.IsGlobalCrisisTurn(GamePace.Standard, 28), "standard global crises must preserve the 16/28 turn schedule");
        Assert(GamePacing.IsGlobalCrisisTurn(GamePace.Quick, 12) && GamePacing.IsGlobalCrisisTurn(GamePace.Quick, 21), "quick global crises must start on turn 12 and recur every 9 turns");
        Assert(!GamePacing.IsGlobalCrisisTurn(GamePace.Relaxed, 16) && !GamePacing.IsGlobalCrisisTurn(GamePace.Quick, 16), "campaign paces must not share an identical crisis schedule");
        Console.WriteLine("PASS: campaign pace selects 120, 80, or 60 turns and adjusts the global-crisis cadence.");
        PlayUntilFinished(relaxed, "relaxed pace");
        PlayUntilFinished(quick, "quick pace");
        Assert(relaxed.State.Turn <= relaxed.State.MaximumTurn && quick.State.Turn <= quick.State.MaximumTurn,
            "campaigns on each selectable pace must reach a valid ending before their configured limit");
        Console.WriteLine("PASS: relaxed and quick campaigns reach valid endings within their selected turn limits.");
    }

    private static void RunAllDifficultiesToCompletion()
    {
        int[] winsByPath = new int[6];
        int totalTurns = 0;
        for (int difficulty = 0; difficulty < 4; difficulty++)
        {
            for (int sample = 0; sample < 4; sample++)
            {
                int seed = 126831 + difficulty * 7919 + sample * 137;
                GameSimulation simulation = GameSimulation.NewCampaign(seed, (Difficulty)difficulty);
                Assert(simulation.State.Cells.Length == 169, "map must contain 169 cells");
                Assert(simulation.State.Civilizations.Length == 4, "campaign must contain four civilizations");
                PlayUntilFinished(simulation, "difficulty " + (Difficulty)difficulty + ", seed " + seed);
                winsByPath[(int)simulation.State.WinningPath]++;
                totalTurns += simulation.State.Turn;
                Console.WriteLine("PASS: " + (Difficulty)difficulty + " / seed " + seed + " finished on turn " + simulation.State.Turn +
                    " via " + VictoryEvaluator.PathName(simulation.State.WinningPath) + ".");
                if (difficulty == (int)Difficulty.Standard && sample == 0)
                {
                    for (int civIndex = 0; civIndex < simulation.State.Civilizations.Length; civIndex++)
                    {
                        CivilizationState civ = simulation.State.Civilizations[civIndex];
                        int cities = 0;
                        for (int cityIndex = 0; cityIndex < simulation.State.Cities.Length; cityIndex++) if (simulation.State.Cities[cityIndex].OwnerId == civ.Id) cities++;
                        CityState capital = simulation.State.City(simulation.FindCapital(civ.Id));
                        CityYield capitalYield = capital == null ? new CityYield() : EconomyCalculator.Calculate(simulation.State, capital);
                        int nearbyFarms = 0;
                        if (capital != null)
                            for (int cellIndex = 0; cellIndex < simulation.State.Cells.Length; cellIndex++)
                                if (simulation.State.Cells[cellIndex].OwnerId == civ.Id && HexGrid.Distance(simulation.State.Cells[cellIndex].Q, simulation.State.Cells[cellIndex].R, capital.Q, capital.R) <= 2 &&
                                    (simulation.State.Cells[cellIndex].Improvement == ImprovementKind.Farm || simulation.State.Cells[cellIndex].Improvement == ImprovementKind.IrrigatedFarm)) nearbyFarms++;
                        int settlerCount = 0;
                        for (int unitIndex = 0; unitIndex < simulation.State.Units.Length; unitIndex++)
                            if (simulation.State.Units[unitIndex].OwnerId == civ.Id && simulation.State.Units[unitIndex].Kind == UnitKind.SettlerWagon) settlerCount++;
                        Console.WriteLine("METRIC: " + civ.Name + " · era " + civ.Era + " · techs " + civ.CompletedTechnologies + " · cities " + cities +
                            " · pop " + (capital == null ? 0 : capital.PopulationTenths) + "/" + (capital == null ? 0 : capital.HousingTenths) + " · food " + (capital == null ? 0 : capital.FoodTenths) +
                            " · influence " + civ.InfluenceTenths + " · research " + civ.ActiveResearchIndex + " · settlers " + settlerCount + " · project " + (capital == null || capital.Project == null ? "none" : capital.Project.Kind + ":" + capital.Project.TargetId) +
                            " · farmers " + (capital == null ? 0 : capital.Workers[(int)WorkerRole.Farmer]) + " · farms " + nearbyFarms + " · food output " + capitalYield.FoodTenths +
                            " · heritage " + VictoryEvaluator.HeritageScore(simulation.State, civ.Id));
                    }
                }
                if (difficulty == (int)Difficulty.Trial && sample == 2)
                {
                    for (int civIndex = 1; civIndex < simulation.State.Civilizations.Length; civIndex++)
                    {
                        CivilizationState civ = simulation.State.Civilizations[civIndex];
                        int cities = 0, universities = 0, culturalBuildings = 0, routes = 0, markets = 0;
                        int minHealth = 101, minSafety = 101, minQuality = 101, qualityReadyCities = 0;
                        bool academy = false;
                        for (int cityIndex = 0; cityIndex < simulation.State.Cities.Length; cityIndex++)
                        {
                            CityState city = simulation.State.Cities[cityIndex];
                            if (city.OwnerId != civ.Id) continue;
                            cities++;
                            minHealth = Math.Min(minHealth, city.Health);
                            minSafety = Math.Min(minSafety, city.Safety);
                            int quality = VictoryEvaluator.CityQualityIndex(simulation.State, city);
                            minQuality = Math.Min(minQuality, quality);
                            if (quality >= 75) qualityReadyCities++;
                            universities += city.BuildingLevel("university");
                            markets += city.BuildingLevel("market");
                            academy |= city.BuildingLevel("academy") > 0;
                            culturalBuildings += city.BuildingLevel("cultural_house") + city.BuildingLevel("archive") + city.BuildingLevel("exchange_house");
                        }
                        for (int treatyIndex = 0; treatyIndex < simulation.State.Treaties.Length; treatyIndex++)
                            if (simulation.State.Treaties[treatyIndex].Active && simulation.State.Treaties[treatyIndex].Kind == TreatyKind.TradeRoute &&
                                (simulation.State.Treaties[treatyIndex].FromCivilizationId == civ.Id || simulation.State.Treaties[treatyIndex].ToCivilizationId == civ.Id)) routes++;
                        bool culturalReady = VictoryEvaluator.CanStartCulturalFestival(simulation.State, civ.Id);
                        bool scienceReady = false, diplomaticReady = false;
                        for (int cityIndex = 0; cityIndex < simulation.State.Cities.Length; cityIndex++)
                            if (simulation.State.Cities[cityIndex].OwnerId == civ.Id)
                            {
                                scienceReady |= VictoryEvaluator.CanStartScientificProgram(simulation.State, civ.Id, simulation.State.Cities[cityIndex].Id);
                                diplomaticReady |= VictoryEvaluator.CanStartDiplomaticCoalition(simulation.State, civ.Id, simulation.State.Cities[cityIndex].Id);
                            }
                        Console.WriteLine("AI-FINAL: " + civ.Name + " · goal " + civ.StrategicGoal + " · " + civ.Era + " · tech " + civ.CompletedTechnologies +
                            " · cities " + cities + " · culture " + civ.CulturePrestige + "/" + culturalBuildings + " ready " + culturalReady +
                            " · science " + universities + " uni, academy " + academy + " ready " + scienceReady + " · markets " + markets + " · diplomacy routes " + routes +
                            " ready " + diplomaticReady + " · QoL min H/S/index " + minHealth + "/" + minSafety + "/" + minQuality +
                            " qualified cities " + qualityReadyCities + "/" + cities + " · QoL streak " + civ.QualityVictoryTurns +
                            " · treasury/influence " + civ.TreasuryTenths + "/" + civ.InfluenceTenths + ".");
                    }
                }
            }
        }
        Console.WriteLine("METRIC: average turns " + totalTurns / 16 + " · wins by path " + string.Join(",", winsByPath));
    }

    private static void RunInteractiveCampaignToCompletion()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(426817, Difficulty.Standard);
        int actor = simulation.State.HumanCivilizationId;
        int capital = simulation.FindCapital(actor);

        Accept(simulation.Issue(new GameCommand
        {
            Kind = CommandKind.AutoAssignWorkers,
            ActorId = actor,
            EntityId = capital,
            Text = "balanced"
        }), "assign balanced workers");

        Accept(simulation.Issue(new GameCommand
        {
            Kind = CommandKind.SetTax,
            ActorId = actor,
            Value = (int)TaxRate.Low
        }), "set tax policy");

        Accept(simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ChooseTechnology,
            ActorId = actor,
            Text = "pottery"
        }), "choose pottery research");

        Accept(simulation.Issue(new GameCommand
        {
            Kind = CommandKind.SetProject,
            ActorId = actor,
            EntityId = capital,
            Text = "house"
        }), "start house project");

        PlayUntilFinished(simulation, "interactive campaign");
        CivilizationState finalPlayer = simulation.State.Civilization(actor);
        Assert(finalPlayer.Knows("pottery"), "selected research must finish during the campaign (turn " +
            simulation.State.Turn + "/" + simulation.State.MaximumTurn + ", winner " + simulation.State.WinnerCivilizationId +
            ", path " + VictoryEvaluator.PathName(simulation.State.WinningPath) + ", research " + finalPlayer.ActiveResearchIndex + ").");
        Assert(simulation.State.City(capital).BuildingLevel("house") > 0, "selected building project must complete during the campaign");
        Assert(simulation.State.Turn >= 1 && simulation.State.Turn <= simulation.State.MaximumTurn,
            "final turn must remain within campaign length");
        Console.WriteLine("PASS: interactive campaign ended on turn " + simulation.State.Turn +
            " via " + VictoryEvaluator.PathName(simulation.State.WinningPath) + ".");
    }

    private static void RunFairStarterEconomyAcceptance()
    {
        for (int seed = 1200; seed < 1212; seed++)
        {
            GameSimulation simulation = GameSimulation.NewCampaign(seed, Difficulty.Standard);
            for (int civIndex = 0; civIndex < simulation.State.Civilizations.Length; civIndex++)
            {
                CivilizationState civ = simulation.State.Civilizations[civIndex];
                int farmCount = 0;
                for (int cellIndex = 0; cellIndex < simulation.State.Cells.Length; cellIndex++)
                    if (simulation.State.Cells[cellIndex].OwnerId == civ.Id && simulation.State.Cells[cellIndex].Improvement == ImprovementKind.Farm) farmCount++;
                int capitalId = simulation.FindCapital(civ.Id);
                CityState capital = simulation.State.City(capitalId);
                CityYield yield = EconomyCalculator.Calculate(simulation.State, capital);
                Assert(farmCount >= 2, civ.Name + " must receive two fair starter farms");
                Assert(yield.FoodTenths >= capital.PopulationTenths, civ.Name + " must be able to feed its starting population before issuing orders");
            }
        }
        Console.WriteLine("PASS: every civilization receives a fair food-secure start across twelve generated maps.");
    }

    private static void RunScienceFocusAssignmentAcceptance()
    {
        GameState state = MinimalVictoryState();
        CityState city = VictoryCity(1, 0);
        city.Q = 2;
        city.R = 2;
        city.PopulationTenths = 40;
        state.Cities = new[] { city };
        state.Cells = new HexCell[7];
        state.Cells[0] = new HexCell { Q = 2, R = 2, Terrain = TerrainKind.Plains, OwnerId = 0 };
        for (int i = 0; i < HexGrid.Directions.GetLength(0); i++)
            state.Cells[i + 1] = new HexCell
            {
                Q = 2 + HexGrid.Directions[i, 0],
                R = 2 + HexGrid.Directions[i, 1],
                Terrain = TerrainKind.Meadow,
                OwnerId = 0
            };

        EconomyCalculator.AutoAssign(state, city, "science");
        Assert(city.Workers[(int)WorkerRole.Scholar] == 1 && city.Workers[(int)WorkerRole.Artisan] == 0,
            "science focus must assign a specialist before the default artisan when only one worker is free after farming");
        EconomyCalculator.AutoAssign(state, city, "culture");
        Assert(city.Workers[(int)WorkerRole.Artist] == 1 && city.Workers[(int)WorkerRole.Artisan] == 0,
            "culture focus must assign an artist before the default artisan when only one worker is free after farming");
        Console.WriteLine("PASS: science and culture focus prioritize their specialists while preserving food assignments.");
    }

    private static void RunAiProgressionAcceptance()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(134887, Difficulty.Standard);
        simulation.State.MaximumTurn = 70;
        while (!simulation.State.IsFinished)
        {
            simulation.EndTurn();
        }
        for (int i = 1; i < simulation.State.Civilizations.Length; i++)
        {
            CivilizationState civ = simulation.State.Civilizations[i];
            int cities = 0;
            for (int j = 0; j < simulation.State.Cities.Length; j++) if (simulation.State.Cities[j].OwnerId == civ.Id) cities++;
            Assert((int)civ.Era >= (int)Era.ConnectedLands, civ.Archetype + " failed to establish institutions and transition beyond the first two eras");
            Assert(cities >= 3, civ.Archetype + " failed to establish three settlements during the campaign");
            Console.WriteLine("AI: " + civ.Archetype + " reached " + civ.Era + " with " + civ.CompletedTechnologies + " technologies and " + cities + " cities by turn " + simulation.State.Turn + ".");
        }
        Console.WriteLine("PASS: campaign rivals expand and complete multiple institutional era transitions within the 70-turn campaign.");
    }

    private static void RunAiStrategicDiversityAcceptance()
    {
        int distinctOpenings = 0;
        int rivalsWithMultipleGoals = 0;
        var openingPlans = new List<string>();
        var openingResearchPlans = new List<string>();
        var fullResearchPaths = new List<string>();
        var buildingPortfolios = new List<string>();
        int differentResearchSamples = 0;
        for (int sample = 0; sample < 12; sample++)
        {
            GameSimulation simulation = GameSimulation.NewCampaign(814273 + sample * 977, Difficulty.Standard);
            var observedTechnologies = new HashSet<string>[4];
            var researchOrders = new List<string>[4];
            for (int civ = 0; civ < 4; civ++)
            {
                observedTechnologies[civ] = new HashSet<string>(simulation.State.Civilization(civ).KnownTechnologies);
                researchOrders[civ] = new List<string>();
            }
            simulation.EndTurn();
            RecordAiResearchCompletions(simulation.State, observedTechnologies, researchOrders);
            CivilizationState scholar = simulation.State.Civilization(1);
            CivilizationState trader = simulation.State.Civilization(2);
            CivilizationState security = simulation.State.Civilization(3);
            string signature = scholar.StrategicGoal + "/" + trader.StrategicGoal + "/" + security.StrategicGoal;
            if (!openingPlans.Contains(signature)) { openingPlans.Add(signature); distinctOpenings++; }
            string scholarResearch = RulesCatalog.Technologies[scholar.ActiveResearchIndex].Id;
            string traderResearch = RulesCatalog.Technologies[trader.ActiveResearchIndex].Id;
            string securityResearch = RulesCatalog.Technologies[security.ActiveResearchIndex].Id;
            if (scholarResearch != securityResearch) differentResearchSamples++;
            string researchSignature = scholarResearch + "/" + traderResearch + "/" + securityResearch;
            if (!openingResearchPlans.Contains(researchSignature)) openingResearchPlans.Add(researchSignature);

            bool[,] goalsSeen = new bool[3, 7];
            for (int civ = 1; civ < 4; civ++) goalsSeen[civ - 1, AiGoalIndex(simulation.State.Civilization(civ).StrategicGoal)] = true;
            int turns = 1;
            while (!simulation.State.IsFinished && turns < 60)
            {
                simulation.EndTurn();
                RecordAiResearchCompletions(simulation.State, observedTechnologies, researchOrders);
                turns++;
                for (int civ = 1; civ < 4; civ++) goalsSeen[civ - 1, AiGoalIndex(simulation.State.Civilization(civ).StrategicGoal)] = true;
            }
            string fullResearchPath = string.Join(",", researchOrders[1]) + "/" +
                string.Join(",", researchOrders[2]) + "/" + string.Join(",", researchOrders[3]);
            if (!fullResearchPaths.Contains(fullResearchPath)) fullResearchPaths.Add(fullResearchPath);
            string portfolio = AiBuildingSignature(simulation.State, 1) + "/" + AiBuildingSignature(simulation.State, 2) + "/" + AiBuildingSignature(simulation.State, 3);
            if (!buildingPortfolios.Contains(portfolio)) buildingPortfolios.Add(portfolio);
            for (int civ = 0; civ < 3; civ++)
            {
                int count = 0;
                for (int goal = 0; goal < 7; goal++) if (goalsSeen[civ, goal]) count++;
                if (count >= 2) rivalsWithMultipleGoals++;
            }
        }
        Assert(distinctOpenings >= 4, "procedural campaign seeds must produce several recognizable opening plan combinations; found " +
            distinctOpenings + " (" + string.Join(", ", openingPlans.ToArray()) + ")");
        Assert(differentResearchSamples >= 6 && openingResearchPlans.Count >= 2,
            "opening research should vary by archetype and seed; opening patterns " + openingResearchPlans.Count + ", 60-turn research paths " + fullResearchPaths.Count +
            ", scholar/security separation " + differentResearchSamples + "/12");
        Assert(fullResearchPaths.Count >= 8,
            "AI rivals should follow meaningfully different technology orders across procedural seeds; found " + fullResearchPaths.Count + " chronological paths");
        Assert(buildingPortfolios.Count >= 6, "rivals should develop visibly different cities across seeds; distinct building portfolios " + buildingPortfolios.Count);
        Assert(rivalsWithMultipleGoals >= 4, "rivals must adapt their strategic course when their campaign conditions change");
        Console.WriteLine("PASS: " + distinctOpenings + " opening goal combinations, " + openingResearchPlans.Count + " opening research patterns, " +
            buildingPortfolios.Count + " 60-turn building portfolios, " + fullResearchPaths.Count + " chronological research paths, and " +
            rivalsWithMultipleGoals + " rival campaigns changed strategic goal at least once across twelve seeds.");
    }

    private static void RecordAiResearchCompletions(GameState state, HashSet<string>[] observed, List<string>[] orders)
    {
        for (int civ = 1; civ < state.Civilizations.Length; civ++)
        {
            string[] known = state.Civilization(civ).KnownTechnologies;
            for (int i = 0; i < known.Length; i++)
                if (observed[civ].Add(known[i])) orders[civ].Add(known[i]);
        }
    }

    private static string AiBuildingSignature(GameState state, int civilizationId)
    {
        string signature = "";
        for (int buildingIndex = 0; buildingIndex < RulesCatalog.Buildings.Length; buildingIndex++)
        {
            string buildingId = RulesCatalog.Buildings[buildingIndex].Id;
            int total = 0;
            for (int cityIndex = 0; cityIndex < state.Cities.Length; cityIndex++)
                if (state.Cities[cityIndex].OwnerId == civilizationId) total += state.Cities[cityIndex].BuildingLevel(buildingId);
            if (total > 0) signature += buildingId + ":" + total + ",";
        }
        return signature;
    }

    private static int AiGoalIndex(string goal)
    {
        switch (goal)
        {
            case "science": return 0;
            case "trade": return 1;
            case "culture": return 2;
            case "quality": return 3;
            case "defense": return 4;
            case "recovery": return 5;
            default: return 6;
        }
    }

    private static void RunAiCrisisResponseAcceptance()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(814274, Difficulty.Standard);
        CityState rivalCapital = simulation.State.City(simulation.FindCapital(1));
        rivalCapital.FoodTenths = 0;
        int farmersBefore = rivalCapital.Workers[(int)WorkerRole.Farmer];
        simulation.EndTurn();
        rivalCapital = simulation.State.City(rivalCapital.Id);
        Assert(rivalCapital.Workers[(int)WorkerRole.Farmer] > farmersBefore,
            "an AI city facing a food shortfall must override its preferred economy focus and add farmers");
        Console.WriteLine("PASS: an AI rival changes worker assignments immediately when a city faces a food emergency.");
    }

    private static void RunAiBudgetAndInstitutionAcceptance()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(814275, Difficulty.Standard);
        CivilizationState rival = simulation.State.Civilization(1);
        CityState capital = simulation.State.City(simulation.FindCapital(rival.Id));
        rival.TreasuryTenths = -500;
        rival.ConsecutiveDeficitTurns = 2;
        rival.StrategyHoldTurns = 5;
        capital.FoodTenths = 200;
        simulation.EndTurn();
        rival = simulation.State.Civilization(1);
        capital = simulation.State.City(simulation.FindCapital(rival.Id));
        Assert(rival.TaxRate == TaxRate.High && capital.Workers[(int)WorkerRole.Merchant] > 0,
            "an AI rival in a prolonged deficit must raise taxes and prioritize merchants while food remains secure; tax " + rival.TaxRate +
            ", merchants " + capital.Workers[(int)WorkerRole.Merchant] + ", deficit turns " + rival.ConsecutiveDeficitTurns +
            ", food " + capital.FoodTenths + ", project " + (capital.Project == null ? "none" : capital.Project.Kind.ToString()));

        rival.TreasuryTenths = 1000;
        rival.InfluenceTenths = 100;
        rival.Era = Era.ConnectedLands;
        rival.StrategicGoal = "science";
        rival.StrategyHoldTurns = 5;
        simulation.EndTurn();
        rival = simulation.State.Civilization(1);
        Assert(rival.HasLaw("universal_education"), "a science-focused rival must enact affordable education reform when it becomes available");
        Console.WriteLine("PASS: AI rivals respond to a sustained deficit and enact a goal-aligned affordable reform.");
    }

    private static void RunAiVictoryCommitmentAcceptance()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(814276, Difficulty.Standard);
        GameState state = simulation.State;
        CivilizationState rival = state.Civilization(1);
        rival.CulturePrestige = 120;
        rival.CulturalInfluence = new[] { 70, 0, 100, 100 };
        rival.InfluenceTenths = 500;
        rival.TreasuryTenths = 500;
        rival.StrategicGoal = "culture";
        rival.StrategyHoldTurns = 5;
        CityState culturalHouse = VictoryCity(5, 1, "cultural_house");
        culturalHouse.Q = 1; culturalHouse.R = 5;
        CityState archive = VictoryCity(6, 1, "archive");
        archive.Q = 6; archive.R = 5;
        CityState exchange = VictoryCity(7, 1, "exchange_house");
        exchange.Q = 8; exchange.R = 4;
        Array.Resize(ref state.Cities, state.Cities.Length + 3);
        state.Cities[state.Cities.Length - 3] = culturalHouse;
        state.Cities[state.Cities.Length - 2] = archive;
        state.Cities[state.Cities.Length - 1] = exchange;
        state.NextEntityId = 8;

        Assert(VictoryEvaluator.CanStartCulturalFestival(state, rival.Id), "victory commitment fixture must meet cultural requirements");
        simulation.EndTurn();
        rival = simulation.State.Civilization(1);
        Assert(rival.CultureFestivalTurns == 1 && !simulation.State.IsFinished,
            "the AI must begin a cultural victory once and preserve its first progress turn");
        simulation.EndTurn();
        Assert(simulation.State.IsFinished && simulation.State.WinnerCivilizationId == 1 && simulation.State.WinningPath == VictoryPath.Cultural,
            "the AI must not restart an active festival instead of completing the two-turn victory hold");
        Console.WriteLine("PASS: AI rivals commit to and complete an active victory project without restarting its timer.");
    }

    private static void RunAiQualityServicePlanningAcceptance()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(814277, Difficulty.Standard);
        CivilizationState rival = simulation.State.Civilization(1);
        rival.StrategicGoal = "quality";
        rival.StrategyHoldTurns = 5;
        rival.TreasuryTenths = 1000;
        CityState capital = simulation.State.City(simulation.FindCapital(rival.Id));
        capital.Safety = 45;
        capital.Education = 35;
        simulation.EndTurn();
        capital = simulation.State.City(capital.Id);
        Assert(capital.ServiceFunding[1] > 0 && capital.ServiceFunding[2] > 0,
            "a solvent quality-focused AI must invest in safety and education instead of only building economic infrastructure");
        Console.WriteLine("PASS: a quality-focused rival funds city services when its treasury can support long-term improvements.");
    }

    private static void RunAiDifficultyScalingAcceptance()
    {
        int seed = 428173;
        GameSimulation baseline = GameSimulation.NewCampaign(seed, Difficulty.Calm);
        for (int difficulty = (int)Difficulty.Standard; difficulty <= (int)Difficulty.Trial; difficulty++)
        {
            GameSimulation comparison = GameSimulation.NewCampaign(seed, (Difficulty)difficulty);
            for (int cityIndex = 1; cityIndex < 4; cityIndex++)
            {
                CityState baselineCity = baseline.State.City(cityIndex + 1);
                CityState comparisonCity = comparison.State.City(cityIndex + 1);
                CityYield baselineYield = EconomyCalculator.Calculate(baseline.State, baselineCity);
                CityYield comparisonYield = EconomyCalculator.Calculate(comparison.State, comparisonCity);
                Assert(comparisonYield.FoodTenths >= baselineYield.FoodTenths && comparisonYield.Production >= baselineYield.Production &&
                    comparisonYield.KnowledgeTenths >= baselineYield.KnowledgeTenths && comparisonYield.TreasuryTenths >= baselineYield.TreasuryTenths &&
                    comparisonYield.CulturePrestige >= baselineYield.CulturePrestige,
                    "AI output modifiers must scale consistently and never lower output on a more difficult setting");
            }
        }
        Console.WriteLine("PASS: AI output modifiers scale monotonically from Calm through Trial on identical maps.");
    }

    private static void RunEraTransitionAcceptance()
    {
        int[] expectedCosts = { 6, 9, 14, 20 };
        for (int era = 0; era < expectedCosts.Length; era++)
            Assert(GameSimulation.EraTransitionProductionCost((Era)era) == expectedCosts[era], "era transition costs must follow the 50% progression in the GDD");

        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        player.KnownTechnologies = new[] { "agriculture", "pottery", "stone" };
        player.InfluenceTenths = 100;
        state.Cities = new[] { VictoryCity(1, 0, "council"), VictoryCity(2, 0) };
        GameSimulation simulation = new GameSimulation(state);
        Accept(simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = 0, EntityId = 1, Text = "era_transition" }), "start first era transition");
        Assert(simulation.State.Civilization(0).InfluenceTenths == 0 && simulation.State.City(1).Project.Cost == 6,
            "first era transition must require three technologies, a council, and 10 influence at 6 production");
        CommandResult duplicateTransition = simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = 0, EntityId = 2, Text = "era_transition" });
        Assert(!duplicateTransition.Accepted && duplicateTransition.Code == "era_transition_busy",
            "a civilization must not advance multiple eras through parallel projects");

        state = MinimalVictoryState();
        player = state.Civilization(0);
        player.KnownTechnologies = new[] { "agriculture", "pottery", "stone" };
        player.InfluenceTenths = 100;
        state.Cities = new[] { VictoryCity(1, 0) };
        simulation = new GameSimulation(state);
        CommandResult missingCouncil = simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = 0, EntityId = 1, Text = "era_transition" });
        Assert(!missingCouncil.Accepted && missingCouncil.Code == "era_institution" && simulation.State.Civilization(0).InfluenceTenths == 100,
            "era transition must explain the missing council without charging influence");

        state.Civilization(0).KnownTechnologies = new[] { "agriculture", "pottery" };
        state.Cities = new[] { VictoryCity(1, 0, "council") };
        simulation = new GameSimulation(state);
        CommandResult missingTechnology = simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = 0, EntityId = 1, Text = "era_transition" });
        Assert(!missingTechnology.Accepted && missingTechnology.Code == "era_technologies" && simulation.State.Civilization(0).InfluenceTenths == 100,
            "era transition must require three current-era technologies without charging influence on rejection");
        Console.WriteLine("PASS: era transitions follow the GDD threshold, council institution, resource cost, and clear rejection rules.");
    }

    private static void RunUniqueEmpireBuildingQueueAcceptance()
    {
        GameState state = MinimalVictoryState();
        state.Civilization(0).KnownTechnologies = new[] { "writing" };
        state.Cities = new[] { VictoryCity(1, 0), VictoryCity(2, 0) };
        state.Cells = new[] { new HexCell { Q = 1, R = 0, Terrain = TerrainKind.Plains, OwnerId = -1 } };
        GameSimulation simulation = new GameSimulation(state);
        Accept(simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = 0, EntityId = 1, Text = "council" }),
            "queue the unique council in the first city");
        CommandResult duplicate = simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = 0, EntityId = 2, Text = "council" });
        Assert(!duplicate.Accepted && duplicate.Code == "unique_building",
            "a unique empire building must not be queued in multiple cities at the same time");
        Accept(simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = 0, EntityId = 1, Text = "council" }),
            "reselect the existing unique building project");
        Console.WriteLine("PASS: unique empire buildings cannot occupy parallel city queues, and repeating the chosen project is idempotent.");
    }

    private static void RunSaveResumeCampaign()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(918273, Difficulty.Standard);
        for (int i = 0; i < 12; i++)
        {
            int turnBefore = simulation.State.Turn;
            simulation.EndTurn();
            Assert(simulation.State.Turn == turnBefore + 1, "save scenario stalled before serialization");
        }

        var options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(simulation.State, options);
        GameState restored = JsonSerializer.Deserialize<GameState>(json, options);
        Assert(restored != null && restored.Turn == simulation.State.Turn && restored.Seed == simulation.State.Seed,
            "campaign state did not survive JSON save and reload");
        simulation = new GameSimulation(restored);
        PlayUntilFinished(simulation, "save/resume campaign");
        Console.WriteLine("PASS: save/resume campaign finished on turn " + simulation.State.Turn + ".");
    }

    private static void RunPunitiveWarAndRecovery()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(718293, Difficulty.Standard);
        CivilizationState player = simulation.State.Civilization(simulation.State.HumanCivilizationId);
        int startingTreasury = player.TreasuryTenths;
        int startingReputation = player.Reputation;
        Accept(simulation.Issue(new GameCommand
        {
            Kind = CommandKind.DeclareWar,
            ActorId = player.Id,
            TargetId = 1,
            Text = "Пресечение нарушения границы"
        }), "declare war with a legal goal");

        Assert(player.WarsStarted == 1, "war declaration must be recorded in the profile state");
        Assert(player.TreasuryTenths == startingTreasury - 80, "war mobilization must charge its treasury cost");
        Assert(player.Reputation == startingReputation - 12, "war declaration must immediately reduce reputation");
        simulation.EndTurn();
        player = simulation.State.Civilization(simulation.State.HumanCivilizationId);
        Assert(!simulation.State.IsFinished, "war campaign ended unexpectedly");
        Assert(player.WarStrain >= 10, "mobilization must create war strain");
        Assert(simulation.State.Conflicts.Length == 1 && simulation.State.Conflicts[0].Phase == 1,
            "war must provide a crisis turn before combat begins");

        Accept(simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MakePeace,
            ActorId = player.Id,
            TargetId = 1
        }), "make peace during the mobilization crisis");
        Assert(simulation.State.Conflicts[0].Phase == 3, "peace must enter the truce phase");

        for (int turn = 0; turn < 8 && simulation.State.Conflicts[0].Active; turn++)
        {
            int before = simulation.State.Turn;
            simulation.EndTurn();
            Assert(simulation.State.IsFinished || simulation.State.Turn == before + 1,
                "peace and recovery stalled the campaign");
        }
        player = simulation.State.Civilization(simulation.State.HumanCivilizationId);
        Assert(!simulation.State.Conflicts[0].Active, "truce must end in a finite recovery period");
        Assert(player.WarStrain < 25, "war strain must recover after the conflict closes");
        Console.WriteLine("PASS: mobilization penalties, pre-combat peace, truce, and recovery completed.");
    }

    private static void RunBattleEventCaptureAcceptance()
    {
        GameState state = MinimalVictoryState();
        state.HumanCivilizationId = 0;
        state.Cells = new[]
        {
            new HexCell { Q = 0, R = 0, Terrain = TerrainKind.Plains, OwnerId = 0, VisibleToMask = 1 },
            new HexCell { Q = 1, R = 0, Terrain = TerrainKind.Forest, OwnerId = 1, VisibleToMask = 1 }
        };
        state.Units = new[]
        {
            new UnitState { Id = 10, OwnerId = 0, Kind = UnitKind.FieldForce, Q = 0, R = 0, Strength = 100, MovementLeft = 1 },
            new UnitState { Id = 11, OwnerId = 1, Kind = UnitKind.Militia, Q = 1, R = 0, Strength = 100, MovementLeft = 1 }
        };
        state.Conflicts = new[] { new ConflictState { Id = 20, AggressorId = 0, DefenderId = 1, Phase = 2, Active = true } };
        GameSimulation simulation = new GameSimulation(state);
        CommandResult move = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit, ActorId = 0, EntityId = 10, Q = 1, R = 0
        });
        Assert(move.Accepted, "a valid wartime move onto an enemy unit must resolve");
        Assert(simulation.LastResolvedBattles.Length == 1, "a real battle must publish one presentation event");
        BattleResolutionEvent battle = simulation.LastResolvedBattles[0];
        Assert(battle.AttackerOwnerId == 0 && battle.DefenderOwnerId == 1 && battle.CellQ == 1 && battle.CellR == 0,
            "battle presentation must identify both sides and the contested cell");
        Assert(battle.PlayerFacing && battle.HumanVisibilityMask == 1,
            "human battle presentation must be marked as visible to the human player");
        Console.WriteLine("PASS: resolved battles publish presentation data with participants, losses, and visibility.");
    }

    private static void RunVictoryPathAcceptance()
    {
        RunQualityVictoryAcceptance();
        RunScientificVictoryAcceptance();
        RunCulturalVictoryAcceptance();
        RunDiplomaticVictoryAcceptance();
        RunHeritageScoreAndTieAcceptance();
        RunVictoryProjectCommandAcceptance();
    }

    private static void RunQualityVictoryAcceptance()
    {
        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        state.Cities = new[] { QualityCity(1, 0), QualityCity(2, 0), QualityCity(3, 0) };
        for (int i = 0; i < 5; i++) VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(!state.IsFinished, "quality victory must require four cities");
        state.Cities = new[] { QualityCity(1, 0), QualityCity(2, 0), QualityCity(3, 0), QualityCity(4, 0) };
        for (int i = 0; i < 4; i++) VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(!state.IsFinished, "quality victory must hold for five consecutive turns");
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(state.IsFinished && state.WinningPath == VictoryPath.QualityOfLife && player.QualityVictoryTurns == 5,
            "quality victory must trigger after five turns at the GDD thresholds");
        Console.WriteLine("PASS: quality victory requires four cities and five consecutive qualifying turns.");
    }

    private static void RunScientificVictoryAcceptance()
    {
        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        player.Era = Era.Synthesis;
        player.KnownTechnologies = new[] { "computing_networks", "open_science" };
        player.ScienceProjectTurns = 4;
        player.LastTurnKnowledgeOutputTenths = 50;
        player.PreviousTurnKnowledgeOutputTenths = 50;
        state.Cities = new[]
        {
            VictoryCity(1, 0, "university", "academy", true),
            VictoryCity(2, 0, "university"),
            VictoryCity(3, 1)
        };
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(state.IsFinished && state.WinningPath == VictoryPath.Scientific, "scientific victory must complete with prerequisites, project duration, and knowledge output");

        state = MinimalVictoryState();
        player = state.Civilization(0);
        player.Era = Era.Synthesis;
        player.KnownTechnologies = new[] { "computing_networks", "open_science" };
        player.ScienceProjectTurns = 4;
        player.LastTurnKnowledgeOutputTenths = 49;
        player.PreviousTurnKnowledgeOutputTenths = 50;
        state.Cities = new[]
        {
            VictoryCity(1, 0, "university", "academy", true),
            VictoryCity(2, 0, "university")
        };
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(!state.IsFinished, "scientific victory must not ignore the minimum knowledge output");
        Console.WriteLine("PASS: science victory validates its two-era technologies, distributed universities, academy, project, and recent output.");
    }

    private static void RunScientificVictoryLiveProjectAcceptance()
    {
        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        player.Era = Era.Synthesis;
        player.KnownTechnologies = new[] { "computing_networks", "open_science" };
        player.KnowledgeTenths = 300;
        player.InfluenceTenths = 80;
        state.Cities = new[]
        {
            VictoryCity(1, 0, "university", "academy", true),
            VictoryCity(2, 0, "university")
        };
        GameSimulation simulation = new GameSimulation(state);
        for (int i = 0; i < 4 && !simulation.State.IsFinished; i++) simulation.EndTurn();
        Assert(!simulation.State.IsFinished && simulation.State.Civilization(0).ScienceProjectTurns == 4,
            "the science project must wait for its knowledge-output condition instead of disappearing after four turns");
        Assert(simulation.State.City(1).Project.Kind == ProjectKind.ScientificProject && simulation.State.City(1).Project.Progress == 20,
            "completed science production must remain reserved while its final output condition is checked");

        Accept(simulation.Issue(new GameCommand { Kind = CommandKind.SetWorkers, ActorId = 0, EntityId = 1, Value = (int)WorkerRole.Scholar, TargetId = 1 }), "assign first scientific worker");
        Accept(simulation.Issue(new GameCommand { Kind = CommandKind.SetWorkers, ActorId = 0, EntityId = 2, Value = (int)WorkerRole.Scholar, TargetId = 1 }), "assign second scientific worker");
        simulation.EndTurn();
        Assert(!simulation.State.IsFinished, "the knowledge requirement must hold for two consecutive turns");
        simulation.EndTurn();
        Assert(simulation.State.IsFinished && simulation.State.WinningPath == VictoryPath.Scientific,
            "a completed scientific project must win after meeting both recent output checks");
        Console.WriteLine("PASS: live science project survives four turns, waits for output recovery, and awards its victory.");
    }

    private static void RunCulturalVictoryAcceptance()
    {
        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        player.CulturePrestige = 100;
        player.CulturalInfluence = new[] { 0, 70, 70, 40 };
        player.CultureFestivalTurns = 2;
        state.Cities = new[]
        {
            VictoryCity(1, 0, "cultural_house"),
            VictoryCity(2, 0, "archive"),
            VictoryCity(3, 0, "exchange_house")
        };
        Assert(VictoryEvaluator.CanStartCulturalFestival(state, 0), "cultural festival should become available with all three cities and required influence");
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(!state.IsFinished && player.CultureFestivalTurns == 1, "cultural victory must not complete before the festival's second turn");
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(state.IsFinished && state.WinningPath == VictoryPath.Cultural, "cultural victory must complete after two qualifying festival turns");

        state = MinimalVictoryState();
        player = state.Civilization(0);
        player.CulturePrestige = 100;
        player.CulturalInfluence = new[] { 0, 70, 69, 40 };
        state.Cities = new[] { VictoryCity(1, 0, "cultural_house"), VictoryCity(2, 0, "archive"), VictoryCity(3, 0, "exchange_house") };
        Assert(!VictoryEvaluator.CanStartCulturalFestival(state, 0), "cultural path must require two partners at seventy influence");
        Console.WriteLine("PASS: cultural victory validates three distinct city landmarks, influence thresholds, and two festival turns.");
    }

    private static void RunDiplomaticVictoryAcceptance()
    {
        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        player.KnownTechnologies = new[] { "international_assembly" };
        player.InfluenceTenths = 200;
        player.Relations[1] = 25; player.Relations[2] = 25;
        player.Trusts[1] = 50; player.Trusts[2] = 50;
        state.HasResolvedGlobalCrisis = true;
        state.Crisis = new CrisisState { Kind = CrisisKind.Epidemic, Resolved = true };
        state.ResolvedGlobalCrisisContributorMask = (1 << 0) | (1 << 1) | (1 << 2);
        CityState assembly = VictoryCity(10, 0, "assembly");
        CityState allyOne = VictoryCity(11, 1);
        CityState allyTwo = VictoryCity(12, 2);
        state.Cities = new[] { assembly, allyOne, allyTwo };
        state.Treaties = new[]
        {
            new TreatyState { Id = 1, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 1, RouteCityA = 10, RouteCityB = 11, Active = true },
            new TreatyState { Id = 2, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 2, RouteCityA = 10, RouteCityB = 12, Active = true }
        };
        Assert(VictoryEvaluator.CanStartDiplomaticCoalition(state, 0, 10), "diplomatic coalition should be available with support, crisis aid, and linked routes");
        player.DiplomaticCoalitionTurns = 2;
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(!state.IsFinished && player.DiplomaticCoalitionTurns == 1, "diplomatic victory must require a second coalition turn");
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(state.IsFinished && state.WinningPath == VictoryPath.Diplomatic, "diplomatic victory must close after two supported turns");

        state = MinimalVictoryState();
        player = state.Civilization(0);
        player.KnownTechnologies = new[] { "international_assembly" };
        player.Relations[1] = 25; player.Relations[2] = 25;
        player.Trusts[1] = 50; player.Trusts[2] = 50;
        player.DiplomaticCoalitionTurns = 2;
        state.HasResolvedGlobalCrisis = true;
        state.Crisis = new CrisisState { Kind = CrisisKind.FoodShortage, Resolved = false };
        state.ResolvedGlobalCrisisContributorMask = (1 << 0) | (1 << 1) | (1 << 2);
        state.Cities = new[] { VictoryCity(10, 0, "assembly"), VictoryCity(11, 1), VictoryCity(12, 2) };
        state.Treaties = new[]
        {
            new TreatyState { Id = 1, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 1, RouteCityA = 10, RouteCityB = 11, Active = true },
            new TreatyState { Id = 2, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 2, RouteCityA = 10, RouteCityB = 12, Active = true }
        };
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(!state.IsFinished && player.DiplomaticCoalitionTurns == 1, "a later crisis must not erase completed crisis cooperation during the coalition hold");
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(state.IsFinished && state.WinningPath == VictoryPath.Diplomatic, "a later crisis must not prevent a supported diplomatic coalition from closing");

        state = MinimalVictoryState();
        player = state.Civilization(0);
        player.KnownTechnologies = new[] { "international_assembly" };
        player.Relations[1] = 25; player.Relations[2] = 25;
        player.Trusts[1] = 50; player.Trusts[2] = 50;
        state.HasResolvedGlobalCrisis = true;
        state.Crisis = new CrisisState { Kind = CrisisKind.Epidemic, Resolved = true };
        state.ResolvedGlobalCrisisContributorMask = (1 << 0) | (1 << 1) | (1 << 2);
        state.Cities = new[] { VictoryCity(10, 0, "assembly"), VictoryCity(11, 1), VictoryCity(12, 2) };
        state.Treaties = new[]
        {
            new TreatyState { Id = 1, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 1, RouteCityA = 10, RouteCityB = 11, Active = true },
            new TreatyState { Id = 2, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 2, RouteCityA = 11, RouteCityB = 12, Active = true }
        };
        Assert(!VictoryEvaluator.HasDiplomaticAssemblyRoutes(state, 0, 10), "both diplomatic routes must connect directly to the assembly city");
        Console.WriteLine("PASS: diplomatic victory validates crisis participation, three votes, assembly-linked trade, and two turns.");
    }

    private static void RunHeritageScoreAndTieAcceptance()
    {
        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        state.Cities = new[] { QualityCity(1, 0), QualityCity(2, 0), QualityCity(3, 0), QualityCity(4, 0) };
        int fourCityScore = VictoryEvaluator.HeritageScore(state, 0);
        state.Cities = new[] { QualityCity(1, 0), QualityCity(2, 0), QualityCity(3, 0), QualityCity(4, 0), QualityCity(5, 0) };
        int fiveCityScore = VictoryEvaluator.HeritageScore(state, 0);
        Assert(fourCityScore == fiveCityScore, "heritage score must stop rewarding city count directly after society coverage reaches four cities");
        Assert(fourCityScore >= 0 && fourCityScore <= 100, "heritage score must be normalized to 0-100");

        state = new GameState
        {
            Turn = 70,
            MaximumTurn = 70,
            Civilizations = new[] { new CivilizationState { Id = 0, Name = "A", Trusts = new[] { 0, 0 } }, new CivilizationState { Id = 1, Name = "B", Trusts = new[] { 0, 0 } } },
            Cities = Array.Empty<CityState>(),
            Cells = Array.Empty<HexCell>(),
            Units = Array.Empty<UnitState>(),
            Treaties = Array.Empty<TreatyState>()
        };
        VictoryEvaluator.Check(state, new System.Collections.Generic.List<string>());
        Assert(state.IsFinished && state.WinningCivilizationIds.Length == 2, "equal heritage scores on the final turn must create a shared victory");
        Console.WriteLine("PASS: heritage score is weighted and normalized; tied final scores produce shared victory.");
    }

    private static void RunVictoryProjectCommandAcceptance()
    {
        GameState state = MinimalVictoryState();
        CivilizationState player = state.Civilization(0);
        player.Era = Era.Synthesis;
        player.KnownTechnologies = new[] { "computing_networks", "open_science" };
        player.KnowledgeTenths = 300;
        player.InfluenceTenths = 80;
        state.Cities = new[]
        {
            VictoryCity(1, 0, "university", "academy"),
            VictoryCity(2, 0, "university")
        };
        GameSimulation science = new GameSimulation(state);
        Accept(science.Issue(new GameCommand { Kind = CommandKind.BeginVictoryProject, ActorId = 0, EntityId = 1, Value = (int)VictoryPath.Scientific }), "begin science project");
        Assert(science.State.Civilization(0).KnowledgeTenths == 0 && science.State.Civilization(0).InfluenceTenths == 0 &&
            science.State.City(1).Project.Kind == ProjectKind.ScientificProject && science.State.City(1).Project.Cost == 20,
            "science launch must pay the documented resource cost and reserve 20 production");
        CommandResult duplicateScience = science.Issue(new GameCommand { Kind = CommandKind.BeginVictoryProject, ActorId = 0, EntityId = 2, Value = (int)VictoryPath.Scientific });
        Assert(!duplicateScience.Accepted && science.State.City(2).Project.Kind == ProjectKind.None,
            "a civilization must not split one scientific program across multiple city queues");

        state = MinimalVictoryState();
        player = state.Civilization(0);
        player.CulturePrestige = 100;
        player.CulturalInfluence = new[] { 0, 70, 70, 40 };
        player.InfluenceTenths = 200;
        player.TreasuryTenths = 100;
        state.Cities = new[] { VictoryCity(1, 0, "cultural_house"), VictoryCity(2, 0, "archive"), VictoryCity(3, 0, "exchange_house") };
        GameSimulation culture = new GameSimulation(state);
        Accept(culture.Issue(new GameCommand { Kind = CommandKind.BeginVictoryProject, ActorId = 0, EntityId = 1, Value = (int)VictoryPath.Cultural }), "start cultural festival");
        Assert(culture.State.Civilization(0).InfluenceTenths == 0 && culture.State.Civilization(0).TreasuryTenths == 0 && culture.State.Civilization(0).CultureFestivalTurns == 2,
            "cultural launch must pay 20 influence and 10 treasury");

        state = MinimalVictoryState();
        player = state.Civilization(0);
        player.KnownTechnologies = new[] { "international_assembly" };
        player.InfluenceTenths = 200;
        player.Relations[1] = 25; player.Relations[2] = 25;
        player.Trusts[1] = 50; player.Trusts[2] = 50;
        state.HasResolvedGlobalCrisis = true;
        state.Crisis = new CrisisState { Kind = CrisisKind.Epidemic, Resolved = true };
        state.ResolvedGlobalCrisisContributorMask = 7;
        state.Cities = new[] { VictoryCity(10, 0, "assembly"), VictoryCity(11, 1), VictoryCity(12, 2) };
        state.Treaties = new[]
        {
            new TreatyState { Id = 1, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 1, RouteCityA = 10, RouteCityB = 11, Active = true },
            new TreatyState { Id = 2, Kind = TreatyKind.TradeRoute, FromCivilizationId = 0, ToCivilizationId = 2, RouteCityA = 10, RouteCityB = 12, Active = true }
        };
        GameSimulation diplomatic = new GameSimulation(state);
        Accept(diplomatic.Issue(new GameCommand { Kind = CommandKind.BeginVictoryProject, ActorId = 0, EntityId = 10, Value = (int)VictoryPath.Diplomatic }), "start diplomatic coalition");
        Assert(diplomatic.State.Civilization(0).InfluenceTenths == 0 && diplomatic.State.Civilization(0).DiplomaticCoalitionTurns == 2,
            "diplomatic launch must invest 20 influence and start a two-turn coalition");
        Console.WriteLine("PASS: human commands start each active victory path only after prerequisites and resource payments.");
    }

    private static GameState MinimalVictoryState()
    {
        return new GameState
        {
            Turn = 10,
            MaximumTurn = 70,
            Civilizations = new[]
            {
                new CivilizationState { Id = 0, Name = "A", IsHuman = true, Trusts = new[] { 0, 0, 0, 0 }, Relations = new[] { 0, 0, 0, 0 }, CulturalInfluence = new[] { 0, 0, 0, 0 } },
                new CivilizationState { Id = 1, Name = "B", Trusts = new[] { 0, 0, 0, 0 }, Relations = new[] { 0, 0, 0, 0 }, CulturalInfluence = new[] { 0, 0, 0, 0 } },
                new CivilizationState { Id = 2, Name = "C", Trusts = new[] { 0, 0, 0, 0 }, Relations = new[] { 0, 0, 0, 0 }, CulturalInfluence = new[] { 0, 0, 0, 0 } },
                new CivilizationState { Id = 3, Name = "D", Trusts = new[] { 0, 0, 0, 0 }, Relations = new[] { 0, 0, 0, 0 }, CulturalInfluence = new[] { 0, 0, 0, 0 } }
            },
            Cities = Array.Empty<CityState>(),
            Cells = Array.Empty<HexCell>(),
            Units = Array.Empty<UnitState>(),
            Treaties = Array.Empty<TreatyState>(),
            Conflicts = Array.Empty<ConflictState>(),
            Crisis = new CrisisState()
        };
    }

    private static CityState VictoryCity(int id, int owner, params string[] buildings)
    {
        var city = new CityState { Id = id, OwnerId = owner, Name = "City " + id, PopulationTenths = 100, HousingTenths = 200, FoodTenths = 100, FoodStoreCapacityTenths = 400, Workers = new int[6], ServiceFunding = new int[3] };
        for (int i = 0; i < buildings.Length; i++) city.AddBuilding(buildings[i]);
        city.Project = new CityProject { Kind = ProjectKind.None };
        return city;
    }

    private static CityState VictoryCity(int id, int owner, string firstBuilding, string secondBuilding, bool scientificProject)
    {
        CityState city = VictoryCity(id, owner, firstBuilding, secondBuilding);
        if (scientificProject) city.Project = new CityProject { Kind = ProjectKind.ScientificProject, TargetId = "common_knowledge", Cost = 20, Progress = 20 };
        return city;
    }

    private static CityState QualityCity(int id, int owner)
    {
        CityState city = VictoryCity(id, owner);
        city.Health = 95;
        city.Safety = 95;
        city.Education = 85;
        city.Trust = 90;
        city.Environment = 95;
        city.Workers[(int)WorkerRole.Farmer] = 10;
        return city;
    }

    private static void PlayUntilFinished(GameSimulation simulation, string scenario)
    {
        int turnsPlayed = 0;
        while (!simulation.State.IsFinished && turnsPlayed <= simulation.State.MaximumTurn)
        {
            int turnBefore = simulation.State.Turn;
            simulation.EndTurn();
            turnsPlayed++;

            Assert(simulation.LastMessage == null || !simulation.LastMessage.StartsWith("Ход не сохранён", StringComparison.Ordinal),
                scenario + " failed to resolve turn " + turnBefore + ": " + simulation.LastMessage);
            if (!simulation.State.IsFinished)
                Assert(simulation.State.Turn == turnBefore + 1,
                    scenario + " stalled on turn " + turnBefore + ": " + simulation.LastMessage);
        }

        Assert(simulation.State.IsFinished, scenario + " did not reach a victory before the turn limit");
        Assert(simulation.State.WinnerCivilizationId >= 0, scenario + " has no winner");
        Assert(simulation.State.Civilization(simulation.State.WinnerCivilizationId) != null,
            scenario + " winner id is invalid");
        Assert(simulation.State.WinningPath != VictoryPath.None, scenario + " has no recorded victory path");
        Assert(simulation.State.Turn <= simulation.State.MaximumTurn,
            scenario + " advanced beyond the configured turn limit");
    }

    private static void Accept(CommandResult result, string action)
    {
        Assert(result != null && result.Accepted, action + " rejected: " + (result == null ? "no result" : result.Code + " · " + result.Message));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
