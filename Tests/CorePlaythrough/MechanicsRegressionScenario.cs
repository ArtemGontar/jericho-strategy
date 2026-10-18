using System;
using Nasledie.Core;

internal static class MechanicsRegressionScenario
{
    public static void Run(Action<string> report)
    {
        CheckCoastAndResources();
        CheckImprovementYields();
        CheckRiverMovement();
        CheckNonAggressionBreachPenalty();
        report("PASS: generated coasts, fishery/workshop/herb yields, bridge movement, and treaty breach consequences are playable mechanics.");
    }

    private static void CheckCoastAndResources()
    {
        bool foundCoast = false;
        bool foundSalt = false;
        for (int seed = 1; seed <= 12; seed++)
        {
            GameState state = GameFactory.NewGame(seed, Difficulty.Standard);
            for (int i = 0; i < state.Cells.Length; i++)
            {
                foundCoast |= state.Cells[i].Terrain == TerrainKind.Coast;
                foundSalt |= state.Cells[i].Resource == ResourceKind.Salt;
            }
        }
        Assert(foundCoast, "procedural maps must generate reachable coast cells");
        Assert(foundSalt, "coastal map generation must make salt available in a campaign set");
    }

    private static void CheckImprovementYields()
    {
        GameState state = GameFactory.NewGame(9001, Difficulty.Standard);
        CityState city = state.City(1);
        CivilizationState owner = state.Civilization(city.OwnerId);
        owner.NationId = "";
        owner.KnownTechnologies = Array.Empty<string>();
        owner.Laws = Array.Empty<string>();
        owner.TaxRate = TaxRate.Normal;
        city.Workers = new[] { 1, 1, 0, 1, 0, 0 };
        city.ServiceFunding = new[] { 0, 0, 0 };
        city.BuildingIds = Array.Empty<string>();
        city.BuildingLevels = Array.Empty<int>();

        for (int i = 0; i < state.Cells.Length; i++)
        {
            HexCell cell = state.Cells[i];
            if (cell.OwnerId != owner.Id || HexGrid.Distance(cell.Q, cell.R, city.Q, city.R) > 2) continue;
            cell.Improvement = ImprovementKind.None;
            cell.ImprovementLevel = 0;
            cell.Resource = ResourceKind.None;
            cell.Damage = 0;
            cell.Terrain = TerrainKind.Plains;
        }

        CityYield baseline = EconomyCalculator.Calculate(state, city);
        HexCell fishery = state.Cell(city.Q + 1, city.R);
        HexCell workshop = state.Cell(city.Q, city.R + 1);
        HexCell herbs = state.Cell(city.Q - 1, city.R + 1);
        Assert(fishery != null && workshop != null && herbs != null, "test improvement cells must exist on the generated map");
        fishery.OwnerId = owner.Id;
        fishery.Terrain = TerrainKind.Coast;
        fishery.Improvement = ImprovementKind.Fishery;
        fishery.ImprovementLevel = 1;
        workshop.OwnerId = owner.Id;
        workshop.Terrain = TerrainKind.Plains;
        workshop.Improvement = ImprovementKind.Workshop;
        workshop.ImprovementLevel = 1;
        herbs.OwnerId = owner.Id;
        herbs.Terrain = TerrainKind.Swamp;
        herbs.Resource = ResourceKind.Herbs;

        CityYield improved = EconomyCalculator.Calculate(state, city);
        Assert(improved.FoodTenths - baseline.FoodTenths == 20, "fishery must add two food per turn");
        Assert(improved.Production - baseline.Production == 1, "workshop tile must add one production per turn");
        Assert(improved.TreasuryTenths - baseline.TreasuryTenths == 10, "fishery must add one treasury when a merchant works the city");
        Assert(improved.HealthChange - baseline.HealthChange == 5, "herbs must add five health per turn");
    }

    private static void CheckRiverMovement()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9002, Difficulty.Standard);
        GameState state = simulation.State;
        HexCell origin = state.Cell(5, 5);
        HexCell river = state.Cell(6, 5);
        Assert(origin != null && river != null, "test movement cells must exist on the generated map");
        origin.Terrain = TerrainKind.Plains;
        origin.OwnerId = state.HumanCivilizationId;
        river.Terrain = TerrainKind.River;
        river.OwnerId = state.HumanCivilizationId;
        river.Improvement = ImprovementKind.None;
        river.HasRoad = false;
        var unit = new UnitState
        {
            Id = 9003,
            OwnerId = state.HumanCivilizationId,
            CitySourceId = state.Cities[0].Id,
            Kind = UnitKind.Militia,
            Q = origin.Q,
            R = origin.R,
            Strength = 70,
            Readiness = 100,
            SupplyBasisPoints = 10000,
            MovementLeft = 1
        };
        state.Units = new[] { unit };
        CommandResult withoutBridge = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit,
            ActorId = state.HumanCivilizationId,
            EntityId = unit.Id,
            Q = river.Q,
            R = river.R
        });
        Assert(!withoutBridge.Accepted && withoutBridge.Code == "movement_spent", "a militia must need two movement points to cross an unbridged river");

        river.Improvement = ImprovementKind.Bridge;
        CommandResult withBridge = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit,
            ActorId = state.HumanCivilizationId,
            EntityId = unit.Id,
            Q = river.Q,
            R = river.R
        });
        Assert(withBridge.Accepted && unit.Q == river.Q && unit.R == river.R, "a bridge must reduce river movement cost to one");
    }

    private static void CheckNonAggressionBreachPenalty()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9004, Difficulty.Standard);
        GameState state = simulation.State;
        CivilizationState actor = state.Civilization(state.HumanCivilizationId);
        CivilizationState target = state.Civilization(1);
        int reputationBefore = actor.Reputation;
        int targetRelationBefore = target.Relations[actor.Id];
        var pact = new TreatyState
        {
            Id = 9005,
            Kind = TreatyKind.NonAggression,
            FromCivilizationId = actor.Id,
            ToCivilizationId = target.Id,
            TurnsRemaining = 3,
            Active = true
        };
        state.Treaties = new[] { pact };

        CommandResult result = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.DeclareWar,
            ActorId = actor.Id,
            TargetId = target.Id,
            Q = state.Cities[1].Q,
            R = state.Cities[1].R,
            Text = "treaty breach"
        });
        Assert(result.Accepted, "a player may still break a non-aggression pact by choosing war");
        Assert(!pact.Active && pact.Broken, "declaring war must close and mark the active pact as broken");
        Assert(actor.Reputation <= reputationBefore - 32, "breaking a pact must add a distinct reputation penalty");
        Assert(target.Relations[actor.Id] <= targetRelationBefore - 20, "the target must register the broken promise");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
