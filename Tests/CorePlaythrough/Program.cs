using System;
using System.Text.Json;
using Nasledie.Core;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RunAllDifficultiesToCompletion();
            RunFairStarterEconomyAcceptance();
            RunScienceFocusAssignmentAcceptance();
            RunInteractiveCampaignToCompletion();
            RunSaveResumeCampaign();
            RunPunitiveWarAndRecovery();
            RunVictoryPathAcceptance();
            ActiveQualityVictoryScenario.Run(Console.WriteLine);
            FreshCulturalVictoryScenario.Run(Console.WriteLine);
            FreshDiplomaticVictoryScenario.Run(Console.WriteLine);
            FreshScienceVictoryScenario.Run(Console.WriteLine);
            RunScientificVictoryLiveProjectAcceptance();
            RunUniqueEmpireBuildingQueueAcceptance();
            RunEraTransitionAcceptance();
            RunAiProgressionAcceptance();
            Console.WriteLine("PASS: all headless campaign scenarios reached a valid ending.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
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
            Text = "food"
        }), "assign workers to food");

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
        Assert(simulation.State.Civilization(actor).Knows("pottery"), "selected research must finish during the campaign");
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
        GameSimulation simulation = GameSimulation.NewCampaign(134750, Difficulty.Standard);
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
