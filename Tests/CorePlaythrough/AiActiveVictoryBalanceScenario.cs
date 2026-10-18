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
                while (!simulation.State.IsFinished)
                {
                    int turnBefore = simulation.State.Turn;
                    simulation.EndTurn();
                    Require(simulation.State.IsFinished || simulation.State.Turn > turnBefore,
                        difficulty + " self-play stalled on seed " + seed + ": " + simulation.LastMessage);
                }

                bool botWon = simulation.State.WinnerCivilizationId != simulation.State.HumanCivilizationId;
                bool active = simulation.State.WinningPath != VictoryPath.HeritageScore;
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
