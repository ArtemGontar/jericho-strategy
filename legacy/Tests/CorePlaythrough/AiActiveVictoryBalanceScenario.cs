using System;
using Nasledie.Core;

internal static class AiActiveVictoryBalanceScenario
{
    public static void Run(Action<string> output)
    {
        int strongActiveWins = 0;
        int strongQualityWins = 0;
        int strongVictoryTurns = 0;
        int trialActiveWins = 0;
        int trialScienceWins = 0;
        int trialVictoryTurns = 0;
        int[] difficulties = { (int)Difficulty.Strong, (int)Difficulty.Trial };
        for (int difficultyIndex = 0; difficultyIndex < difficulties.Length; difficultyIndex++)
        {
            Difficulty difficulty = (Difficulty)difficulties[difficultyIndex];
            for (int sample = 0; sample < 32; sample++)
            {
                int seed = 280001 + (int)difficulty * 10007 + sample * 379;
                GameSimulation simulation = GameSimulation.NewCampaign(seed, difficulty);
                int[] qualityStreak = new int[simulation.State.Civilizations.Length];
                int[] bestQualityStreak = new int[simulation.State.Civilizations.Length];
                string[] qualityBreakReason = new string[simulation.State.Civilizations.Length];
                while (!simulation.State.IsFinished)
                {
                    int turnBefore = simulation.State.Turn;
                    simulation.EndTurn();
                    for (int c = 0; c < simulation.State.Civilizations.Length; c++)
                    {
                        CivilizationState civ = simulation.State.Civilizations[c];
                        if (civ.IsHuman || civ.IsDefeated) continue;
                        string failure = QualityConditionsFailure(simulation.State, civ);
                        if (string.IsNullOrEmpty(failure))
                        {
                            qualityStreak[c]++;
                            bestQualityStreak[c] = Math.Max(bestQualityStreak[c], qualityStreak[c]);
                        }
                        else
                        {
                            if (qualityStreak[c] > 0) qualityBreakReason[c] = failure;
                            qualityStreak[c] = 0;
                        }
                    }
                    Require(simulation.State.IsFinished || simulation.State.Turn > turnBefore,
                        difficulty + " self-play stalled on seed " + seed + ": " + simulation.LastMessage);
                }

                bool botWon = simulation.State.WinnerCivilizationId != simulation.State.HumanCivilizationId;
                bool active = simulation.State.WinningPath != VictoryPath.HeritageScore;
                if (difficulty == Difficulty.Trial && Environment.GetEnvironmentVariable("JERICHO_DIAG_AI") == "1" &&
                    !(botWon && active))
                    DiagnoseTrialOutcome(simulation, seed, bestQualityStreak, qualityBreakReason, output);
                if (difficulty == Difficulty.Strong && botWon && active)
                {
                    strongActiveWins++;
                    strongVictoryTurns += simulation.State.Turn;
                    if (simulation.State.WinningPath == VictoryPath.QualityOfLife) strongQualityWins++;
                    output("AI-ROUTE: Strong seed " + seed + " · " + VictoryEvaluator.PathName(simulation.State.WinningPath) + " · turn " + simulation.State.Turn);
                }
                if (difficulty == Difficulty.Trial && botWon && active)
                {
                    trialActiveWins++;
                    trialVictoryTurns += simulation.State.Turn;
                    if (simulation.State.WinningPath == VictoryPath.Scientific) trialScienceWins++;
                    output("AI-ROUTE: Trial seed " + seed + " · " + VictoryEvaluator.PathName(simulation.State.WinningPath) + " · turn " + simulation.State.Turn);
                }
            }
        }

        output("METRIC: Strong active " + strongActiveWins + "/32, welfare " + strongQualityWins +
            "; Trial active " + trialActiveWins + "/32, science " + trialScienceWins + ".");
        Require(strongActiveWins >= 8,
            "Strong opponents must demonstrate at least eight active victories in thirty-two full self-play campaigns; observed " + strongActiveWins);
        Require(strongQualityWins >= 4,
            "Strong opponents must demonstrate at least four quality-of-life victories in the balance set; observed " + strongQualityWins);
        Require(trialActiveWins >= 16,
            "Trial opponents must demonstrate at least sixteen active victories in thirty-two full self-play campaigns; observed " + trialActiveWins);
        Require(trialScienceWins >= 8,
            "Trial opponents must demonstrate at least eight scientific victories in the balance set; observed " + trialScienceWins);
        output("PASS: Strong AI completed " + strongActiveWins + "/32 active wins (" + strongQualityWins + " quality, mean turn " +
            strongVictoryTurns / Math.Max(1, strongActiveWins) + "); Trial completed " + trialActiveWins + "/32 active wins (" +
            trialScienceWins + " scientific, mean turn " + trialVictoryTurns / Math.Max(1, trialActiveWins) + ").");
    }

    private static string QualityConditionsFailure(GameState state, CivilizationState civ)
    {
        int cityCount = 0;
        int totalPopulation = 0;
        int qualifiedPopulation = 0;
        int lowest = 101;
        int highest = -1;
        string belowThreshold = "";
        for (int i = 0; i < state.Cities.Length; i++)
        {
            CityState city = state.Cities[i];
            if (city.OwnerId != civ.Id) continue;
            cityCount++;
            if (city.Health < 60) return "health@" + city.Name + "=" + city.Health;
            if (city.Safety < 60) return "safety@" + city.Name + "=" + city.Safety;
            int index = VictoryEvaluator.CityQualityIndex(state, city);
            lowest = Math.Min(lowest, index);
            highest = Math.Max(highest, index);
            totalPopulation += Math.Max(0, city.PopulationTenths);
            if (index >= 75) qualifiedPopulation += Math.Max(0, city.PopulationTenths);
            else
            {
                int assigned = 0;
                if (city.Workers != null) for (int worker = 0; worker < city.Workers.Length; worker++) assigned += Math.Max(0, city.Workers[worker]);
                belowThreshold += city.Name + ":q" + index + "/p" + city.PopulationTenths + "/h" + city.HousingTenths +
                    "/f" + city.FoodTenths + "/hlth" + city.Health + "/safe" + city.Safety + "/edu" + city.Education +
                    "/env" + city.Environment + "/jobs" + assigned + "-" + city.WorkerCount + ";";
            }
        }
        if (cityCount < 4) return "cities=" + cityCount;
        if (civ.ConsecutiveDeficitTurns > 2) return "deficit=" + civ.ConsecutiveDeficitTurns;
        if (totalPopulation <= 0) return "population=0";
        if (qualifiedPopulation * 100L < totalPopulation * 80L)
            return "qualified=" + qualifiedPopulation + "/" + totalPopulation + " below=" + belowThreshold;
        if (highest - lowest > 20) return "spread=" + (highest - lowest);
        return "";
    }

    private static void DiagnoseTrialOutcome(GameSimulation simulation, int seed, int[] bestQualityStreak,
        string[] qualityBreakReason, Action<string> output)
    {
        GameState state = simulation.State;
        output("AI-DIAG: seed " + seed + " winner " + state.WinnerCivilizationId + " · " +
            VictoryEvaluator.PathName(state.WinningPath) + " · turn " + state.Turn);
        for (int c = 0; c < state.Civilizations.Length; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            if (civ.IsHuman) continue;
            int cities = 0;
            int universities = 0;
            bool academy = false;
            bool diplomacyReady = false;
            int totalPopulation = 0;
            int qualifyingPopulation = 0;
            int lowestQuality = 101;
            int highestQuality = -1;
            bool cityHealthAndSafetyReady = true;
            string projects = "";
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civ.Id) continue;
                cities++;
                if (city.BuildingLevel("university") > 0) universities++;
                if (city.BuildingLevel("academy") > 0) academy = true;
                if (VictoryEvaluator.CanStartDiplomaticCoalition(state, civ.Id, city.Id)) diplomacyReady = true;
                int quality = VictoryEvaluator.CityQualityIndex(state, city);
                totalPopulation += city.PopulationTenths;
                if (quality >= 75) qualifyingPopulation += city.PopulationTenths;
                lowestQuality = Math.Min(lowestQuality, quality);
                highestQuality = Math.Max(highestQuality, quality);
                if (city.Health < 60 || city.Safety < 60) cityHealthAndSafetyReady = false;
                if (city.Project != null && city.Project.Kind != ProjectKind.None)
                    projects += city.Name + ":" + city.Project.Kind + "/" + city.Project.TargetId + ";";
            }
            output("AI-DIAG: civ " + civ.Id + " " + civ.Name + " · " + civ.Archetype + " · " + civ.StrategicGoal +
                " · era " + civ.Era + " · tech " + civ.CompletedTechnologies + " · cities " + cities +
                " · university cities " + universities + " · academy " + academy + " · knowledge output " +
                civ.LastTurnKnowledgeOutputTenths + "/" + civ.PreviousTurnKnowledgeOutputTenths + " · science turns " +
                civ.ScienceProjectTurns + " · science techs " + civ.Knows("computing_networks") + "/" + civ.Knows("open_science") +
                " · culture " + civ.CulturePrestige + " · culture ready " +
                VictoryEvaluator.CanStartCulturalFestival(state, civ.Id) + " · diplomacy ready " +
                diplomacyReady + " · QoL " + cityHealthAndSafetyReady + "/" + qualifyingPopulation + "/" + totalPopulation +
                " spread " + (highestQuality - lowestQuality) + " deficit " + civ.ConsecutiveDeficitTurns +
                " · QoL streak " + civ.QualityVictoryTurns + " max " + bestQualityStreak[civ.Id] +
                " break " + qualityBreakReason[civ.Id] +
                " · projects " + projects);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
