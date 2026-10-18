using System;
using Nasledie.Core;

internal static class CampaignReplayScenario
{
    public static void Run(Action<string> report)
    {
        GameSimulation simulation = GameSimulation.NewCampaign(760132, Difficulty.Strong, GamePace.Quick, "jericho");
        for (int turn = 0; turn < 4; turn++)
        {
            CommandResult tax = simulation.Issue(new GameCommand
            {
                Kind = CommandKind.SetTax, ActorId = simulation.State.HumanCivilizationId,
                Value = (int)(turn % 2 == 0 ? TaxRate.Normal : TaxRate.Low)
            });
            Assert(tax.Accepted, "the replay fixture must accept every recorded command");
            if (turn == 0)
            {
                CommandResult workers = simulation.Issue(new GameCommand
                {
                    Kind = CommandKind.AutoAssignWorkers, ActorId = simulation.State.HumanCivilizationId,
                    EntityId = simulation.State.Cities[0].Id, Text = "food"
                });
                Assert(workers.Accepted, "the replay fixture must accept a city command");
            }
            simulation.EndTurn();
        }

        CampaignReplayResult replay = CampaignReplay.Verify(simulation.State);
        Assert(replay.Success, "a seeded campaign with accepted commands must replay to the same full state hash: " + replay.Message);
        Assert(simulation.State.TurnStateHashes.Length == 4,
            "every completed turn must save a state checkpoint hash");
        Assert(replay.ExpectedHash.Length == 64 && replay.ExpectedHash == replay.ReplayHash,
            "campaign replay must expose matching SHA-256 state hashes");

        GameState damaged = simulation.State.Copy();
        damaged.Cities[0].FoodTenths++;
        CampaignReplayResult mismatch = CampaignReplay.Verify(damaged);
        Assert(!mismatch.Success, "replay verification must detect a changed saved value");

        GameState truncated = simulation.State.Copy();
        truncated.CommandJournalTruncated = true;
        CampaignReplayResult incomplete = CampaignReplay.Verify(truncated);
        Assert(!incomplete.Success && incomplete.Message.IndexOf("усечён", StringComparison.OrdinalIgnoreCase) >= 0,
            "a truncated command journal must be reported as non-replayable");

        string rulesHash = CampaignFingerprint.Rules();
        Assert(rulesHash.Length == 64 && rulesHash == CampaignFingerprint.Rules(),
            "the current rules catalog must have a deterministic SHA-256 fingerprint");
        report("PASS: seed-based command replay reproduces the full state hash and detects state edits, truncated journals, and rules fingerprints.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
