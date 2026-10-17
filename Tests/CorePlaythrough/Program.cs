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
            RunInteractiveCampaignToCompletion();
            RunSaveResumeCampaign();
            RunPunitiveWarAndRecovery();
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
        for (int difficulty = 0; difficulty < 4; difficulty++)
        {
            for (int sample = 0; sample < 4; sample++)
            {
                int seed = 126831 + difficulty * 7919 + sample * 137;
                GameSimulation simulation = GameSimulation.NewCampaign(seed, (Difficulty)difficulty);
                Assert(simulation.State.Cells.Length == 169, "map must contain 169 cells");
                Assert(simulation.State.Civilizations.Length == 4, "campaign must contain four civilizations");
                PlayUntilFinished(simulation, "difficulty " + (Difficulty)difficulty + ", seed " + seed);
                Console.WriteLine("PASS: " + (Difficulty)difficulty + " / seed " + seed + " finished on turn " + simulation.State.Turn +
                    " via " + VictoryEvaluator.PathName(simulation.State.WinningPath) + ".");
            }
        }
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
