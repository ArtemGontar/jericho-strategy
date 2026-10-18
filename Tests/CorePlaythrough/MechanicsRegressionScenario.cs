using System;
using Nasledie.Core;

internal static class MechanicsRegressionScenario
{
    public static void Run(Action<string> report)
    {
        CheckCoastAndResources();
        CheckFairStartsAndExpansionRoutes();
        CheckImprovementYields();
        CheckTerrainSpecializationYields();
        CheckWorkerCapacity();
        CheckIronUpgradeAndTreaties();
        CheckTreatyDurationsAndEmbargo();
        CheckPeaceNegotiations();
        CheckAnnexation();
        CheckPrisonerExchange();
        CheckNeutralMediation();
        CheckHighDifficultyWarInitiative();
        CheckRiverMovement();
        CheckNonAggressionBreachPenalty();
        CheckSettlerWagonSafety();
        CheckSettlerInterceptionChoices();
        CheckBattlePrisonerCapture();
        report("PASS: fair starts, terrain-specialized worker yields and capacities, coasts, salt/resource deals, humanitarian aid, treaty timing/embargo, mediation, park/fortification/iron/herb effects, peace demands/annexation, high-difficulty AI war initiative, bridge movement, treaty breach consequences, civilian wagon evacuation/interception choices, and command journaling work.");
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

    private static void CheckFairStartsAndExpansionRoutes()
    {
        for (int seed = 1; seed <= 64; seed++)
        {
            GameState state = GameFactory.NewGame(seed, Difficulty.Standard);
            int minimum = int.MaxValue;
            int maximum = 0;
            for (int civ = 0; civ < state.Civilizations.Length; civ++)
            {
                int potential = GameFactory.StartPotential(state, civ);
                minimum = Math.Min(minimum, potential);
                maximum = Math.Max(maximum, potential);
                CityState city = FirstCity(state, civ);
                int safeRoutes = 0;
                for (int direction = 0; direction < HexGrid.Directions.GetLength(0); direction++)
                {
                    bool safe = true;
                    for (int step = 1; step <= 3; step++)
                    {
                        HexCell cell = state.Cell(city.Q + HexGrid.Directions[direction, 0] * step,
                            city.R + HexGrid.Directions[direction, 1] * step);
                        if (!IsPassable(cell)) { safe = false; break; }
                    }
                    if (safe) safeRoutes++;
                }
                Assert(safeRoutes >= 2, "each capital must have at least two clear, independent early expansion directions");
            }
            Assert(minimum * 100 >= maximum * 80,
                "starting terrain potential must stay within a 20% spread; seed " + seed + " had " + minimum + "/" + maximum);
        }
    }

    private static bool IsPassable(HexCell cell)
    {
        return cell != null && cell.Terrain != TerrainKind.Water && cell.Terrain != TerrainKind.Mountain && cell.Terrain != TerrainKind.Swamp;
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
        Assert(improved.Production - baseline.Production == 1, "workshop tile must add one production per turn (" + baseline.Production + " -> " + improved.Production + ")");
        Assert(improved.TreasuryTenths - baseline.TreasuryTenths == 10, "fishery must add one treasury when a merchant works the city");
        Assert(improved.HealthChange - baseline.HealthChange == 5, "herbs must add five health per turn");

        HexCell park = FindOwnedCell(state, owner.Id, city, fishery, workshop, herbs);
        HexCell fort = FindOwnedCell(state, owner.Id, city, fishery, workshop, herbs, park);
        Assert(park != null && fort != null, "park and fortification test cells must exist");
        park.Terrain = TerrainKind.Meadow;
        park.Improvement = ImprovementKind.Park;
        fort.Terrain = TerrainKind.Plains;
        fort.Improvement = ImprovementKind.Fortification;
        CityYield parkAndFort = EconomyCalculator.Calculate(state, city);
        Assert(parkAndFort.EnvironmentChange == 2 && parkAndFort.AttractionChange == 2,
            "a park must restore the surrounding city's environment and attraction each turn");
        Assert(parkAndFort.TreasuryTenths == improved.TreasuryTenths - 5,
            "a fortification must carry a recurring maintenance cost");
    }

    private static void CheckIronUpgradeAndTreaties()
    {
        GameSimulation upgradeSimulation = GameSimulation.NewCampaign(9009, Difficulty.Standard);
        GameState upgradeState = upgradeSimulation.State;
        int playerId = upgradeState.HumanCivilizationId;
        CivilizationState player = upgradeState.Civilization(playerId);
        CityState source = FirstCity(upgradeState, playerId);
        player.KnownTechnologies = new[] { "engineering" };
        player.TreasuryTenths = 100;
        HexCell iron = FindOwnedCell(upgradeState, playerId, source);
        Assert(iron != null, "an owned iron test cell must exist");
        iron.Resource = ResourceKind.Iron;
        iron.Improvement = ImprovementKind.Workshop;
        iron.Damage = 0;
        var fieldForce = new UnitState
        {
            Id = 9050, OwnerId = playerId, CitySourceId = source.Id, Kind = UnitKind.FieldForce,
            Q = source.Q, R = source.R, Strength = 100, Readiness = 100, SupplyBasisPoints = 10000, MovementLeft = 1
        };
        upgradeState.Units = new[] { fieldForce };
        CommandResult upgrade = upgradeSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.UpgradeUnit, ActorId = playerId, EntityId = fieldForce.Id
        });
        Assert(upgrade.Accepted && fieldForce.IronEquipped && player.TreasuryTenths == 50,
            "engineering, a worked iron deposit, and 5 treasury must unlock field force equipment");
        Assert(upgradeState.CommandJournal.Length == 1 && upgradeState.CommandJournal[0].Contains("kind=UpgradeUnit"),
            "accepted player actions must be retained in the diagnostic command journal");
        GameState copied = upgradeState.Copy();
        Assert(copied.CommandJournal.Length == 1 && !ReferenceEquals(copied.CommandJournal, upgradeState.CommandJournal),
            "copying a campaign must preserve an independent command journal");

        CheckHumanitarianAid();
        CheckSaltExport();
        CheckMediation();
    }

    private static void CheckTerrainSpecializationYields()
    {
        GameState state = GameFactory.NewGame(9020, Difficulty.Standard);
        CityState city = state.City(1);
        int ownerId = city.OwnerId;
        CivilizationState owner = state.Civilization(ownerId);
        owner.NationId = string.Empty;
        owner.KnownTechnologies = Array.Empty<string>();
        owner.Laws = Array.Empty<string>();
        owner.TaxRate = TaxRate.Normal;
        city.Workers = new[] { 1, 1, 0, 0, 0, 0 };
        for (int i = 0; i < state.Cells.Length; i++)
        {
            HexCell cell = state.Cells[i];
            if (HexGrid.Distance(cell.Q, cell.R, city.Q, city.R) > 2) continue;
            cell.OwnerId = ownerId;
            cell.Terrain = TerrainKind.Plains;
            cell.Resource = ResourceKind.None;
            cell.Improvement = ImprovementKind.None;
            cell.ImprovementLevel = 0;
            cell.Damage = 0;
        }
        CityYield baseline = EconomyCalculator.Calculate(state, city);
        HexCell meadow = state.Cell(city.Q + 1, city.R);
        HexCell workshopCell = state.Cell(city.Q, city.R + 1);
        Assert(meadow != null && workshopCell != null, "terrain specialization cells must exist near the capital");
        meadow.Terrain = TerrainKind.Meadow;
        workshopCell.Terrain = TerrainKind.Forest;
        int efficiency = Math.Max(5000, Math.Min(11500, city.EfficiencyBasisPoints));
        int scaledFoodBonus = 10 * efficiency / 10000;
        CityYield terrainYield = EconomyCalculator.Calculate(state, city);
        Assert(terrainYield.FoodTenths - baseline.FoodTenths == scaledFoodBonus,
            "the farmer must automatically work a productive meadow and gain one food (" + baseline.FoodTenths + " -> " + terrainYield.FoodTenths + ", expected " + scaledFoodBonus + ", efficiency " + efficiency + ")");
        Assert(terrainYield.Production - baseline.Production == scaledFoodBonus / 10,
            "the artisan must automatically work a forest and gain one production");

        meadow.Improvement = ImprovementKind.Farm;
        meadow.ImprovementLevel = 1;
        workshopCell.Improvement = ImprovementKind.Workshop;
        workshopCell.ImprovementLevel = 1;
        CityYield improvedYield = EconomyCalculator.Calculate(state, city);
        Assert(improvedYield.FoodTenths - terrainYield.FoodTenths == scaledFoodBonus,
            "a worked farm must add one food to its farmer rather than produce food without a worker");
        Assert(improvedYield.Production - terrainYield.Production == 1,
            "a workshop improves production only while an artisan works its cell");
    }

    private static void CheckWorkerCapacity()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9027, Difficulty.Standard);
        GameState state = simulation.State;
        int ownerId = state.HumanCivilizationId;
        CityState city = FirstCity(state, ownerId);
        city.PopulationTenths = 2000;
        city.Workers = new int[6];
        int totalCapacity = EconomyCalculator.WorkerCapacity(state, city);
        int farmerCapacity = EconomyCalculator.WorkerRoleCapacity(state, city, WorkerRole.Farmer);
        HexCell remoteCell = state.Cell(city.Q + 5, city.R);
        Assert(remoteCell != null, "remote worker capacity test cell must exist on the map");
        int capacityBeforeRemoteClaim = totalCapacity;
        int farmerBeforeRemoteClaim = farmerCapacity;
        remoteCell.OwnerId = ownerId;
        Assert(EconomyCalculator.WorkerCapacity(state, city) == capacityBeforeRemoteClaim &&
               EconomyCalculator.WorkerRoleCapacity(state, city, WorkerRole.Farmer) == farmerBeforeRemoteClaim,
            "owned cells outside a city's four-hex town district radius must not create workplaces");

        EconomyCalculator.AutoAssign(state, city, "balanced");
        int assigned = EconomyCalculator.AssignedWorkers(city);
        Assert(assigned <= Math.Min(city.WorkerCount, totalCapacity), "automatic assignment must fit both population and city workplaces");
        for (int role = 0; role < city.Workers.Length; role++)
            Assert(city.Workers[role] <= EconomyCalculator.WorkerRoleCapacity(state, city, (WorkerRole)role),
                "automatic assignment must stay within the capacity for role " + (WorkerRole)role);

        Array.Clear(city.Workers, 0, city.Workers.Length);
        CommandResult overRole = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.SetWorkers, ActorId = ownerId, EntityId = city.Id,
            Value = (int)WorkerRole.Farmer, TargetId = farmerCapacity + 1
        });
        Assert(!overRole.Accepted && overRole.Code == "worker_role_capacity",
            "manual assignment must reject a role count above its local workplace capacity");
        CommandResult validRole = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.SetWorkers, ActorId = ownerId, EntityId = city.Id,
            Value = (int)WorkerRole.Farmer, TargetId = farmerCapacity
        });
        Assert(validRole.Accepted, "manual assignment at the exact role capacity must be accepted");

        GameState legacyState = GameFactory.NewGame(9028, Difficulty.Standard);
        CityState legacyCity = FirstCity(legacyState, legacyState.HumanCivilizationId);
        legacyCity.Workers = new[] { 50, 50, 50, 50, 50, 50 };
        GameSimulation loaded = new GameSimulation(legacyState);
        int normalizedAssignment = EconomyCalculator.AssignedWorkers(legacyCity);
        Assert(normalizedAssignment <= Math.Min(legacyCity.WorkerCount, EconomyCalculator.WorkerCapacity(legacyState, legacyCity)),
            "loading an older or malformed save must normalize workers to the available population and workplaces");
        for (int role = 0; role < legacyCity.Workers.Length; role++)
            Assert(legacyCity.Workers[role] <= EconomyCalculator.WorkerRoleCapacity(legacyState, legacyCity, (WorkerRole)role),
                "save normalization must enforce each role's capacity");
    }

    private static void CheckHumanitarianAid()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9011, Difficulty.Standard);
        GameState state = simulation.State;
        int actorId = state.HumanCivilizationId;
        CityState source = FirstCity(state, actorId);
        CityState recipient = FirstCity(state, 1);
        MakeAdjacent(source, recipient);
        CivilizationState actor = state.Civilization(actorId);
        CivilizationState target = state.Civilization(recipient.OwnerId);
        actor.InfluenceTenths = 100;
        actor.TreasuryTenths = 200;
        source.FoodTenths = source.PopulationTenths * 2 + 100;
        int sourceFoodBefore = source.FoodTenths;
        int recipientFoodBefore = recipient.FoodTenths;
        int actorTreasuryBefore = actor.TreasuryTenths;
        int recipientTreasuryBefore = target.TreasuryTenths;
        target.Trusts[actorId] = 100;
        target.Relations[actorId] = 100;
        CommandResult aid = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ProposeTreaty, ActorId = actorId, TargetId = target.Id,
            EntityId = source.Id, Sequence = recipient.Id, Value = (int)TreatyKind.HumanitarianAid
        });
        Assert(aid.Accepted && source.FoodTenths == sourceFoodBefore - 20 && recipient.FoodTenths == recipientFoodBefore + 20,
            "accepted humanitarian aid must transfer two food from the donor city");
        Assert(actor.TreasuryTenths == actorTreasuryBefore - 50 && target.TreasuryTenths == recipientTreasuryBefore + 50,
            "accepted humanitarian aid must transfer five treasury to the recipient");
    }

    private static void CheckSaltExport()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9012, Difficulty.Standard);
        GameState state = simulation.State;
        int actorId = state.HumanCivilizationId;
        CityState source = FirstCity(state, actorId);
        CityState recipient = FirstCity(state, 1);
        MakeAdjacent(source, recipient);
        CivilizationState actor = state.Civilization(actorId);
        CivilizationState target = state.Civilization(recipient.OwnerId);
        actor.InfluenceTenths = 100;
        target.Trusts[actorId] = 100;
        target.Relations[actorId] = 100;
        HexCell salt = FindOwnedCell(state, actorId, source);
        Assert(salt != null, "an owned salt test cell must exist");
        salt.Resource = ResourceKind.Salt;
        CommandResult contract = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ProposeTreaty, ActorId = actorId, TargetId = target.Id,
            EntityId = source.Id, Sequence = recipient.Id, Value = (int)TreatyKind.ResourceExport
        });
        Assert(contract.Accepted && state.Treaties.Length > 0 && state.Treaties[state.Treaties.Length - 1].Active,
            "an AI trade partner must accept an available salt export at high trust");
        TreatyState export = state.Treaties[state.Treaties.Length - 1];
        Assert(export.ExportedResource == ResourceKind.Salt && export.TreasuryPerTurnTenths == 10,
            "the salt contract must record its resource and one-coin recurring payment");
        simulation.EndTurn();
        bool exportStillActive = false;
        for (int i = 0; i < simulation.State.Treaties.Length; i++)
            if (simulation.State.Treaties[i].Kind == TreatyKind.ResourceExport && simulation.State.Treaties[i].Active) exportStillActive = true;
        Assert(exportStillActive && simulation.State.Civilization(actorId).TradeDependency[target.Id] > 0,
            "the salt export contract must remain active and register trade during turn resolution");
    }

    private static void CheckMediation()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9013, Difficulty.Standard);
        GameState state = simulation.State;
        int mediatorId = state.HumanCivilizationId;
        CivilizationState mediator = state.Civilization(mediatorId);
        CivilizationState party = state.Civilization(1);
        mediator.InfluenceTenths = 100;
        party.Trusts[mediatorId] = 100;
        party.Relations[mediatorId] = 100;
        var conflict = new ConflictState { Id = 9051, AggressorId = party.Id, DefenderId = 2, Phase = 2, Active = true };
        party.WarStrain = 30;
        state.Civilization(2).WarStrain = 20;
        state.Conflicts = new[] { conflict };
        CommandResult mediation = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ProposeTreaty, ActorId = mediatorId, TargetId = party.Id,
            Value = (int)TreatyKind.Mediation
        });
        Assert(mediation.Accepted && conflict.Phase == 3 && conflict.TruceTurns == 3,
            "accepted third-party mediation must stop the crisis and open a two-turn truce");
        Assert(party.WarStrain == 20 && state.Civilization(2).WarStrain == 10,
            "mediation must reduce both parties' war strain");
    }

    private static void CheckTreatyDurationsAndEmbargo()
    {
        GameState legacy = GameFactory.NewGame(9030, Difficulty.Standard);
        int playerId = legacy.HumanCivilizationId;
        int targetId = playerId == 1 ? 2 : 1;
        legacy.Treaties = new[]
        {
            new TreatyState { Id = 9301, Kind = TreatyKind.TradeRoute, FromCivilizationId = playerId, ToCivilizationId = targetId, Active = true, TurnsRemaining = 6 },
            new TreatyState { Id = 9302, Kind = TreatyKind.NonAggression, FromCivilizationId = playerId, ToCivilizationId = targetId, Active = true, TurnsRemaining = 8 },
            new TreatyState { Id = 9303, Kind = TreatyKind.OpenBorders, FromCivilizationId = playerId, ToCivilizationId = targetId, Active = true, TurnsRemaining = 8 },
            new TreatyState { Id = 9304, Kind = TreatyKind.Guarantee, FromCivilizationId = playerId, ToCivilizationId = targetId, Active = true, TurnsRemaining = 8 },
            new TreatyState { Id = 9305, Kind = TreatyKind.Embargo, FromCivilizationId = playerId, ToCivilizationId = targetId, Active = true, TurnsRemaining = 8 },
            new TreatyState { Id = 9306, Kind = TreatyKind.ResourceExport, FromCivilizationId = playerId, ToCivilizationId = targetId, Active = true, TurnsRemaining = 8 },
            new TreatyState { Id = 9307, Kind = TreatyKind.KnowledgeExchange, FromCivilizationId = playerId, ToCivilizationId = targetId, Active = true, TurnsRemaining = 8 }
        };
        var migrated = new GameSimulation(legacy).State;
        Assert(migrated.Treaties[0].TurnsRemaining == 0 && migrated.Treaties[1].TurnsRemaining == 5 &&
               migrated.Treaties[2].TurnsRemaining == 5 && migrated.Treaties[3].TurnsRemaining == 5 &&
               migrated.Treaties[4].TurnsRemaining == 3 && migrated.Treaties[5].TurnsRemaining == 5 &&
               migrated.Treaties[6].TurnsRemaining == 1,
            "loading older active agreements must normalize them to their actual contract duration, with trade routes open-ended");

        GameSimulation simulation = GameSimulation.NewCampaign(9031, Difficulty.Standard);
        GameState state = simulation.State;
        playerId = state.HumanCivilizationId;
        targetId = playerId == 1 ? 2 : 1;
        CityState own = FirstCity(state, playerId);
        CityState other = FirstCity(state, targetId);
        MakeAdjacent(own, other);
        CivilizationState actor = state.Civilization(playerId);
        CivilizationState target = state.Civilization(targetId);
        actor.InfluenceTenths = 100;
        target.Trusts[playerId] = 100;
        target.Relations[playerId] = 100;
        TreatyState route = new TreatyState
        {
            Id = 9310, Kind = TreatyKind.TradeRoute, FromCivilizationId = playerId, ToCivilizationId = targetId,
            RouteCityA = own.Id, RouteCityB = other.Id, Active = true
        };
        state.Treaties = new[] { route };
        CommandResult withoutCause = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ProposeTreaty, ActorId = playerId, TargetId = targetId,
            EntityId = own.Id, Sequence = other.Id, Value = (int)TreatyKind.Embargo
        });
        Assert(!withoutCause.Accepted && withoutCause.Code == "embargo_crisis",
            "an embargo must be unavailable without a recognized crisis");

        actor.Threats[targetId] = 60;
        CommandResult embargo = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ProposeTreaty, ActorId = playerId, TargetId = targetId,
            EntityId = own.Id, Sequence = other.Id, Value = (int)TreatyKind.Embargo
        });
        TreatyState embargoRecord = state.Treaties[state.Treaties.Length - 1];
        Assert(embargo.Accepted && route.Broken && !route.Active && embargoRecord.Active && embargoRecord.TurnsRemaining == 3,
            "a crisis-backed embargo must close only the selected route and last three turns");
        CommandResult reopen = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ProposeTreaty, ActorId = playerId, TargetId = targetId,
            EntityId = own.Id, Sequence = other.Id, Value = (int)TreatyKind.TradeRoute
        });
        Assert(!reopen.Accepted && reopen.Code == "route_embargoed",
            "a new trade agreement must not bypass an active route embargo");

        simulation.EndTurn();
        Assert(simulation.State.Treaties[1].TurnsRemaining == 2 && simulation.State.Treaties[1].Active,
            "the first turn of an embargo must decrement its visible duration");
        simulation.EndTurn();
        simulation.EndTurn();
        Assert(!simulation.State.Treaties[1].Active && simulation.State.Treaties[1].Fulfilled,
            "an embargo must expire after its third turn");
    }

    private static void CheckPeaceNegotiations()
    {
        GameSimulation occupationSimulation = GameSimulation.NewCampaign(9018, Difficulty.Standard);
        GameState occupationState = occupationSimulation.State;
        int occupationActorId = occupationState.HumanCivilizationId;
        int occupationTargetId = 1;
        CityState occupiedCity = FirstCity(occupationState, occupationTargetId);
        CityState homeCity = FirstCity(occupationState, occupationActorId);
        occupiedCity.OccupiedById = occupationActorId;
        occupiedCity.IntegrationTurns = 2;
        UnitState occupyingForce = new UnitState
        {
            Id = 9058, OwnerId = occupationActorId, CitySourceId = homeCity.Id, Kind = UnitKind.FieldForce,
            Q = occupiedCity.Q, R = occupiedCity.R, Strength = 80, Readiness = 80, SupplyBasisPoints = 8000
        };
        occupationState.Units = new[] { occupyingForce };
        occupationState.Conflicts = new[]
        {
            new ConflictState { Id = 9059, AggressorId = occupationActorId, DefenderId = occupationTargetId,
                OccupationCityId = occupiedCity.Id, Phase = 2, Active = true }
        };
        CommandResult statusQuo = occupationSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.MakePeace, ActorId = occupationActorId, TargetId = occupationTargetId,
            Value = (int)PeaceTermKind.StatusQuo
        });
        HexCell retreatCell = occupationState.Cell(occupyingForce.Q, occupyingForce.R);
        Assert(statusQuo.Accepted && occupiedCity.OccupiedById < 0 && retreatCell != null && retreatCell.OwnerId == occupationActorId,
            "signing peace must end occupation immediately and move forces out of foreign territory");

        GameSimulation reparationsSimulation = GameSimulation.NewCampaign(9014, Difficulty.Standard);
        GameState reparationsState = reparationsSimulation.State;
        int actorId = reparationsState.HumanCivilizationId;
        int targetId = 1;
        CivilizationState actor = reparationsState.Civilization(actorId);
        CivilizationState target = reparationsState.Civilization(targetId);
        actor.TreasuryTenths = 200;
        target.TreasuryTenths = 150;
        reparationsState.Units = new[]
        {
            new UnitState { Id = 9060, OwnerId = actorId, Kind = UnitKind.FieldForce, Strength = 100, Readiness = 100, SupplyBasisPoints = 10000 },
            new UnitState { Id = 9061, OwnerId = targetId, Kind = UnitKind.Militia, Strength = 30, Readiness = 100, SupplyBasisPoints = 10000 }
        };
        reparationsState.Conflicts = new[]
        {
            new ConflictState { Id = 9062, AggressorId = actorId, DefenderId = targetId, Phase = 2, Active = true }
        };
        CommandResult reparations = reparationsSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.MakePeace, ActorId = actorId, TargetId = targetId, Value = (int)PeaceTermKind.Reparations
        });
        Assert(reparations.Accepted && actor.TreasuryTenths == 250 && target.TreasuryTenths == 50,
            "a strong negotiator must receive up to ten treasury as reparations after paying the peace cost");
        Assert(reparationsState.Conflicts[0].AgreedPeaceTerm == PeaceTermKind.Reparations && reparationsState.Conflicts[0].Phase == 3,
            "a successful reparations offer must be recorded and enter the truce");

        GameSimulation borderSimulation = GameSimulation.NewCampaign(9015, Difficulty.Standard);
        GameState borderState = borderSimulation.State;
        actorId = borderState.HumanCivilizationId;
        CityState actorCity = FirstCity(borderState, actorId);
        CityState targetCity = FirstCity(borderState, targetId);
        HexCell disputed = null;
        for (int i = 0; i < borderState.Cells.Length; i++)
        {
            HexCell candidate = borderState.Cells[i];
            if (candidate.OwnerId != targetId || candidate.Terrain == TerrainKind.Water || candidate.Terrain == TerrainKind.Mountain) continue;
            bool cityCell = false;
            for (int c = 0; c < borderState.Cities.Length; c++)
                if (borderState.Cities[c].Q == candidate.Q && borderState.Cities[c].R == candidate.R) { cityCell = true; break; }
            if (!cityCell) { disputed = candidate; break; }
        }
        Assert(disputed != null, "test war must have a non-city cell that can be negotiated");
        actor = borderState.Civilization(actorId);
        actor.TreasuryTenths = 100;
        borderState.Units = new[] { new UnitState { Id = 9063, OwnerId = actorId, Kind = UnitKind.Militia, Strength = 70, Readiness = 100, SupplyBasisPoints = 10000 } };
        borderState.Conflicts = new[]
        {
            new ConflictState { Id = 9064, AggressorId = actorId, DefenderId = targetId, TargetQ = disputed.Q, TargetR = disputed.R,
                LegalGoal = "защита спорной границы", Phase = 2, Active = true }
        };
        CommandResult border = borderSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.MakePeace, ActorId = actorId, TargetId = targetId, Value = (int)PeaceTermKind.BorderAdjustment
        });
        Assert(border.Accepted && disputed.OwnerId == actorId,
            "a valid border goal can transfer only its disputed non-city hex under an accepted peace offer");

        GameSimulation routeSimulation = GameSimulation.NewCampaign(9016, Difficulty.Standard);
        GameState routeState = routeSimulation.State;
        actorId = routeState.HumanCivilizationId;
        actorCity = FirstCity(routeState, actorId);
        targetCity = FirstCity(routeState, targetId);
        MakeAdjacent(actorCity, targetCity);
        actor = routeState.Civilization(actorId);
        actor.TreasuryTenths = 100;
        TreatyState blockedRoute = new TreatyState
        {
            Id = 9065, Kind = TreatyKind.TradeRoute, FromCivilizationId = actorId, ToCivilizationId = targetId,
            RouteCityA = actorCity.Id, RouteCityB = targetCity.Id, Broken = true, Active = false
        };
        routeState.Treaties = new[] { blockedRoute };
        routeState.Conflicts = new[]
        {
            new ConflictState { Id = 9066, AggressorId = actorId, DefenderId = targetId,
                LegalGoal = "снятие торговой блокады", Phase = 2, Active = true }
        };
        CommandResult blockadePeace = routeSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.MakePeace, ActorId = actorId, TargetId = targetId, Value = (int)PeaceTermKind.StatusQuo
        });
        Assert(blockadePeace.Accepted && blockedRoute.Active && !blockedRoute.Broken,
            "a ceasefire signed to lift a blockade must restore the broken trade route");
    }

    private static void CheckAnnexation()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9032, Difficulty.Standard);
        GameState state = simulation.State;
        int actorId = state.HumanCivilizationId;
        int targetId = actorId == 1 ? 2 : 1;
        CivilizationState actor = state.Civilization(actorId);
        actor.Laws = Array.Empty<string>();
        actor.AdministrativeCapacity = 1;
        actor.InfluenceTenths = 200;
        actor.TreasuryTenths = 100;
        CityState occupied = FirstCity(state, targetId);
        occupied.OccupiedById = -1;
        occupied.WarDamageTurns = 0;
        occupied.IntegrationTurns = 0;
        CityYield peacefulOutput = EconomyCalculator.Calculate(state, occupied);
        occupied.OccupiedById = actorId;
        state.Conflicts = new[]
        {
            new ConflictState { Id = 9320, AggressorId = actorId, DefenderId = targetId, TargetQ = occupied.Q, TargetR = occupied.R,
                LegalGoal = "территориальный спор вокруг города", OccupationCityId = occupied.Id, WarTurn = 3, Phase = 2, Active = true }
        };
        GameCommand annex = new GameCommand
        {
            Kind = CommandKind.MakePeace, ActorId = actorId, TargetId = targetId,
            Value = (int)PeaceTermKind.AnnexOccupiedCity
        };
        CommandResult overCapacity = simulation.Issue(annex);
        Assert(!overCapacity.Accepted && overCapacity.Code == "peace_admin_capacity" && occupied.OwnerId == targetId,
            "annexation must be rejected without free administrative capacity and without partial mutation");

        actor.AdministrativeCapacity = 2;
        CommandResult peace = simulation.Issue(annex);
        HexCell cityCenter = state.Cell(occupied.Q, occupied.R);
        Assert(peace.Accepted && occupied.OwnerId == actorId && occupied.OccupiedById < 0 && cityCenter.OwnerId == actorId,
            "an occupied city must change owner only when an eligible territorial peace agreement is signed");
        Assert(actor.InfluenceTenths == 50 && occupied.IntegrationTurns == 5 && occupied.Trust == 35 && occupied.WarDamageTurns >= 5,
            "annexation must cost 15 influence and impose a five-turn integration and recovery period");
        Assert(EconomyCalculator.Calculate(state, occupied).Production < peacefulOutput.Production,
            "an annexed city must initially produce less than before the conflict");
        bool postwarPact = false;
        for (int i = 0; i < state.Treaties.Length; i++)
            if (state.Treaties[i].Kind == TreatyKind.NonAggression && state.Treaties[i].Active && state.Treaties[i].TurnsRemaining == 5 &&
                ((state.Treaties[i].FromCivilizationId == actorId && state.Treaties[i].ToCivilizationId == targetId) ||
                 (state.Treaties[i].FromCivilizationId == targetId && state.Treaties[i].ToCivilizationId == actorId))) postwarPact = true;
        Assert(postwarPact, "peace must create the five-turn non-aggression pact");
    }

    private static void CheckPrisonerExchange()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9033, Difficulty.Standard);
        GameState state = simulation.State;
        int actorId = state.HumanCivilizationId;
        int targetId = actorId == 1 ? 2 : 1;
        CivilizationState actor = state.Civilization(actorId);
        CivilizationState target = state.Civilization(targetId);
        actor.TreasuryTenths = 100;
        actor.PrisonersHeldByCivilizationTenths[targetId] = 7;
        target.PrisonersHeldByCivilizationTenths[actorId] = 4;
        int populationBefore = simulation.CivilizationPopulationTenths(actorId) + simulation.CivilizationPopulationTenths(targetId);
        state.Conflicts = new[] { new ConflictState { Id = 9330, AggressorId = actorId, DefenderId = targetId, Phase = 2, Active = true } };
        CommandResult peace = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MakePeace, ActorId = actorId, TargetId = targetId,
            Value = (int)PeaceTermKind.PrisonerExchange
        });
        Assert(peace.Accepted && actor.PrisonersHeldByCivilizationTenths[targetId] == 0 && target.PrisonersHeldByCivilizationTenths[actorId] == 0,
            "peace with prisoner exchange must release both sides' captured residents");
        Assert(actor.RefugeePopulationTenths >= 4 && target.RefugeePopulationTenths >= 7 &&
               simulation.CivilizationPopulationTenths(actorId) + simulation.CivilizationPopulationTenths(targetId) == populationBefore,
            "released prisoners must return to their home civilization's refugee reserve without creating or deleting population");
    }

    private static void CheckNeutralMediation()
    {
        GameSimulation acceptedSimulation = GameSimulation.NewCampaign(9034, Difficulty.Standard);
        int playerId = acceptedSimulation.State.HumanCivilizationId;
        int rivalId = playerId == 1 ? 2 : 1;
        acceptedSimulation.State.Civilization(playerId).WarStrain = 40;
        acceptedSimulation.State.Civilization(rivalId).WarStrain = 40;
        acceptedSimulation.State.Conflicts = new[]
        {
            new ConflictState { Id = 9340, AggressorId = playerId, DefenderId = rivalId, WarTurn = 7, Phase = 2, Active = true }
        };
        acceptedSimulation.EndTurn();
        GameState state = acceptedSimulation.State;
        ActiveEventState offer = null;
        for (int i = 0; i < state.Events.Length; i++)
            if (!state.Events[i].IsResolved && state.Events[i].Kind == EventKind.NeutralMediation) offer = state.Events[i];
        Assert(offer != null && offer.TargetCivilizationId == playerId,
            "a neutral mediator must offer negotiations to a human side after eight turns without an occupied city");
        int strainBeforeAcceptance = state.Civilization(playerId).WarStrain;
        CommandResult accept = acceptedSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.EventChoice, ActorId = playerId, EntityId = offer.Id, Text = "AcceptMediation"
        });
        ConflictState acceptedConflict = acceptedSimulation.State.Conflicts[0];
        Assert(accept.Accepted && acceptedConflict.Phase == 3 && acceptedConflict.TruceTurns == 3 &&
               acceptedSimulation.State.Civilization(playerId).WarStrain <= strainBeforeAcceptance - 10,
            "accepting a neutral offer must stop the war and lower both sides' tension (accepted=" + accept.Accepted +
            ", phase=" + acceptedConflict.Phase + ", truce=" + acceptedConflict.TruceTurns + ", strain=" + strainBeforeAcceptance +
            "->" + acceptedSimulation.State.Civilization(playerId).WarStrain + ", message=" + accept.Message + ")");

        GameSimulation rejectedSimulation = GameSimulation.NewCampaign(9035, Difficulty.Standard);
        playerId = rejectedSimulation.State.HumanCivilizationId;
        rivalId = playerId == 1 ? 2 : 1;
        rejectedSimulation.State.Civilization(playerId).WarStrain = 40;
        rejectedSimulation.State.Civilization(rivalId).WarStrain = 40;
        rejectedSimulation.State.Conflicts = new[]
        {
            new ConflictState { Id = 9350, AggressorId = playerId, DefenderId = rivalId, WarTurn = 7, Phase = 2, Active = true }
        };
        rejectedSimulation.EndTurn();
        state = rejectedSimulation.State;
        offer = null;
        for (int i = 0; i < state.Events.Length; i++)
            if (!state.Events[i].IsResolved && state.Events[i].Kind == EventKind.NeutralMediation) offer = state.Events[i];
        Assert(offer != null, "a second stalled war must also receive an offer");
        int strainBeforeReject = state.Civilization(playerId).WarStrain;
        CommandResult reject = rejectedSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.EventChoice, ActorId = playerId, EntityId = offer.Id, Text = "RejectMediation"
        });
        Assert(reject.Accepted && rejectedSimulation.State.Conflicts[0].Phase == 2 &&
               rejectedSimulation.State.Civilization(playerId).WarStrain > strainBeforeReject,
            "rejecting neutral mediation must let the conflict continue at increased cost");
        rejectedSimulation.EndTurn();
        rejectedSimulation.EndTurn();
        Assert(rejectedSimulation.State.Conflicts[0].Phase == 3,
            "a war still unresolved after two more turns must enter exhaustion truce");
    }

    private static void CheckHighDifficultyWarInitiative()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9017, Difficulty.Standard);
        GameState state = simulation.State;
        CivilizationState ai = state.Civilization(1);
        int playerId = state.HumanCivilizationId;
        CityState aiCity = FirstCity(state, ai.Id);
        CityState playerCity = FirstCity(state, playerId);
        MakeAdjacent(aiCity, playerCity);
        ai.Difficulty = Difficulty.Trial;
        ai.TreasuryTenths = 500;
        ai.Reputation = 80;
        ai.WarStrain = 0;
        ai.WarsStarted = 0;
        ai.StrategicGoal = "expansion";
        ai.Relations[playerId] = -100;
        ai.Threats[playerId] = 100;
        UnitState force = new UnitState
        {
            Id = 9070, OwnerId = ai.Id, CitySourceId = aiCity.Id, Kind = UnitKind.FieldForce,
            Q = aiCity.Q, R = aiCity.R, Strength = 100, Readiness = 100, SupplyBasisPoints = 10000, MovementLeft = 2
        };
        state.Units = new[] { force };
        state.Turn = 21;
        simulation.EndTurn();
        bool initiated = false;
        for (int i = 0; i < simulation.State.Conflicts.Length; i++)
            if (simulation.State.Conflicts[i].Active && simulation.State.Conflicts[i].AggressorId == ai.Id &&
                simulation.State.Conflicts[i].DefenderId == playerId && simulation.State.Conflicts[i].Phase == 0) initiated = true;
        Assert(initiated, "a hostile, well-armed Trial AI must start a visible crisis before war instead of remaining permanently passive");
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
        actor.Reputation = 100;
        int reputationBefore = actor.Reputation;
        CivilizationState guarantor = state.Civilization(2);
        int guarantorThreatBefore = guarantor.Threats[actor.Id];
        var guarantee = new TreatyState
        {
            Id = 9006, Kind = TreatyKind.Guarantee, FromCivilizationId = guarantor.Id,
            ToCivilizationId = target.Id, TurnsRemaining = 4, Active = true
        };
        state.Treaties = new[] { pact, guarantee };

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
        Assert(!guarantee.Active && guarantee.Broken && guarantor.Threats[actor.Id] >= guarantorThreatBefore + 25,
            "declaring war against a protected civilization must break the guarantee and raise the guarantor's threat assessment");
        Assert(actor.Reputation <= reputationBefore - 42, "breaking a pact and violating a guarantee must add distinct reputation penalties");
        Assert(target.Relations[actor.Id] <= targetRelationBefore - 20, "the target must register the broken promise");
    }

    private static HexCell FindOwnedCell(GameState state, int ownerId, CityState city, params HexCell[] excluded)
    {
        for (int i = 0; i < state.Cells.Length; i++)
        {
            HexCell cell = state.Cells[i];
            if (cell.OwnerId != ownerId || cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Mountain ||
                HexGrid.Distance(cell.Q, cell.R, city.Q, city.R) > 3) continue;
            bool skip = false;
            for (int e = 0; e < excluded.Length; e++) if (excluded[e] == cell) { skip = true; break; }
            if (!skip) return cell;
        }
        return null;
    }

    private static CityState FirstCity(GameState state, int ownerId)
    {
        for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == ownerId) return state.Cities[i];
        throw new InvalidOperationException("test city for civilization " + ownerId + " was not found");
    }

    private static void MakeAdjacent(CityState source, CityState recipient)
    {
        for (int direction = 0; direction < HexGrid.Directions.GetLength(0); direction++)
        {
            int q = source.Q + HexGrid.Directions[direction, 0];
            int r = source.R + HexGrid.Directions[direction, 1];
            if (q == recipient.Q && r == recipient.R) return;
        }
        recipient.Q = source.Q + HexGrid.Directions[0, 0];
        recipient.R = source.R + HexGrid.Directions[0, 1];
    }

    private static void CheckSettlerWagonSafety()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9006, Difficulty.Standard);
        GameState state = simulation.State;
        int playerId = state.HumanCivilizationId;
        int rivalId = 1;
        SetUpWar(state, playerId, rivalId);
        HexCell origin = state.Cell(5, 5);
        HexCell contested = state.Cell(6, 5);
        HexCell refuge = state.Cell(6, 6);
        Assert(origin != null && contested != null && refuge != null, "settler combat cells must exist");
        for (int i = 0; i < state.Cells.Length; i++)
            if (state.Cells[i].OwnerId == rivalId) state.Cells[i].OwnerId = -1;
        origin.OwnerId = playerId;
        origin.Terrain = TerrainKind.Plains;
        contested.OwnerId = rivalId;
        contested.Terrain = TerrainKind.Plains;
        refuge.OwnerId = rivalId;
        refuge.Terrain = TerrainKind.Plains;
        state.Units = new[]
        {
            new UnitState { Id = 9010, OwnerId = playerId, CitySourceId = state.Cities[0].Id, Kind = UnitKind.Militia,
                Q = origin.Q, R = origin.R, Strength = 70, Readiness = 100, SupplyBasisPoints = 10000, MovementLeft = 1 },
            new UnitState { Id = 9011, OwnerId = rivalId, CitySourceId = state.Cities[1].Id, Kind = UnitKind.SettlerWagon,
                Q = contested.Q, R = contested.R, PopulationTenths = 20, FoodCargoTenths = 20, Strength = 0, MovementLeft = 0 }
        };

        CommandResult intercept = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit, ActorId = playerId, EntityId = 9010, Q = contested.Q, R = contested.R
        });
        Assert(intercept.Accepted, "a military unit can intercept a wagon without fighting it as a soldier");
        UnitState protectedWagon = FindUnit(state, 9011);
        Assert(protectedWagon != null && protectedWagon.Q == refuge.Q && protectedWagon.R == refuge.R,
            "an intercepted wagon must retreat to the closest unoccupied owned cell");
        Assert(protectedWagon.PopulationTenths == 20 && protectedWagon.FoodCargoTenths == 20,
            "retreat must preserve the wagon population and its food cargo");

        GameSimulation refugeeSimulation = GameSimulation.NewCampaign(9007, Difficulty.Standard);
        GameState refugeeState = refugeeSimulation.State;
        SetUpWar(refugeeState, refugeeState.HumanCivilizationId, rivalId);
        HexCell refugeeOrigin = refugeeState.Cell(5, 5);
        HexCell wagonCell = refugeeState.Cell(6, 5);
        for (int i = 0; i < refugeeState.Cells.Length; i++)
            if (refugeeState.Cells[i].OwnerId == rivalId) refugeeState.Cells[i].OwnerId = -1;
        refugeeOrigin.OwnerId = refugeeState.HumanCivilizationId;
        refugeeOrigin.Terrain = TerrainKind.Plains;
        wagonCell.OwnerId = rivalId;
        wagonCell.Terrain = TerrainKind.Plains;
        refugeeState.Units = new[]
        {
            new UnitState { Id = 9012, OwnerId = refugeeState.HumanCivilizationId, CitySourceId = refugeeState.Cities[0].Id,
                Kind = UnitKind.Militia, Q = refugeeOrigin.Q, R = refugeeOrigin.R, Strength = 70, Readiness = 100,
                SupplyBasisPoints = 10000, MovementLeft = 1 },
            new UnitState { Id = 9013, OwnerId = rivalId, CitySourceId = refugeeState.Cities[1].Id, Kind = UnitKind.SettlerWagon,
                Q = wagonCell.Q, R = wagonCell.R, PopulationTenths = 20, Strength = 0 }
        };
        CommandResult evacuation = refugeeSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit, ActorId = refugeeState.HumanCivilizationId, EntityId = 9012,
            Q = wagonCell.Q, R = wagonCell.R
        });
        Assert(evacuation.Accepted && FindUnit(refugeeState, 9013) == null &&
               refugeeState.Civilization(rivalId).RefugeePopulationTenths == 20,
            "without a safe owned cell, wagon residents must be preserved as refugees rather than lost");

        GameSimulation civilianAttackSimulation = GameSimulation.NewCampaign(9008, Difficulty.Standard);
        GameState civilianState = civilianAttackSimulation.State;
        SetUpWar(civilianState, civilianState.HumanCivilizationId, rivalId);
        HexCell civilianOrigin = civilianState.Cell(5, 5);
        HexCell militaryTarget = civilianState.Cell(6, 5);
        civilianOrigin.OwnerId = civilianState.HumanCivilizationId;
        militaryTarget.OwnerId = civilianState.HumanCivilizationId;
        civilianOrigin.Terrain = TerrainKind.Plains;
        militaryTarget.Terrain = TerrainKind.Plains;
        civilianState.Units = new[]
        {
            new UnitState { Id = 9014, OwnerId = civilianState.HumanCivilizationId, CitySourceId = civilianState.Cities[0].Id,
                Kind = UnitKind.SettlerWagon, Q = civilianOrigin.Q, R = civilianOrigin.R, PopulationTenths = 20, MovementLeft = 2 },
            new UnitState { Id = 9015, OwnerId = rivalId, CitySourceId = civilianState.Cities[1].Id,
                Kind = UnitKind.Militia, Q = militaryTarget.Q, R = militaryTarget.R, Strength = 70, MovementLeft = 1 }
        };
        CommandResult civilianAttack = civilianAttackSimulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit, ActorId = civilianState.HumanCivilizationId, EntityId = 9014,
            Q = militaryTarget.Q, R = militaryTarget.R
        });
        Assert(!civilianAttack.Accepted && civilianAttack.Code == "settler_cannot_attack" && FindUnit(civilianState, 9014) != null,
            "a settler wagon must never enter a combat resolution as an attacker");
    }

    private static void CheckSettlerInterceptionChoices()
    {
        CheckSettlerInterceptionChoice(9041, "Retreat");
        CheckSettlerInterceptionChoice(9042, "Internment");
        CheckSettlerInterceptionChoice(9043, "ContinueJourney");
    }

    private static void CheckBattlePrisonerCapture()
    {
        GameSimulation simulation = GameSimulation.NewCampaign(9044, Difficulty.Standard);
        GameState state = simulation.State;
        int playerId = state.HumanCivilizationId;
        int rivalId = playerId == 1 ? 2 : 1;
        HexCell origin = state.Cell(5, 5);
        HexCell battle = state.Cell(6, 5);
        Assert(origin != null && battle != null, "prisoner battle cells must exist");
        for (int i = 0; i < state.Cells.Length; i++) state.Cells[i].Terrain = TerrainKind.Water;
        origin.Terrain = TerrainKind.Plains;
        origin.OwnerId = playerId;
        battle.Terrain = TerrainKind.Plains;
        battle.OwnerId = rivalId;
        state.Conflicts = new[] { new ConflictState { Id = 9440, AggressorId = playerId, DefenderId = rivalId, Phase = 2, Active = true } };
        int attackerId = 9441;
        int defenderId = 9442;
        state.Units = new[]
        {
            new UnitState { Id = attackerId, OwnerId = playerId, CitySourceId = FirstCity(state, playerId).Id,
                Kind = UnitKind.FieldForce, Q = origin.Q, R = origin.R, Strength = 100, InitialStrength = 100,
                PopulationTenths = 20, InitialPopulationTenths = 20, Readiness = 100, SupplyBasisPoints = 10000, MovementLeft = 2 },
            new UnitState { Id = defenderId, OwnerId = rivalId, CitySourceId = FirstCity(state, rivalId).Id,
                Kind = UnitKind.Militia, Q = battle.Q, R = battle.R, Strength = 10, InitialStrength = 30,
                PopulationTenths = 20, InitialPopulationTenths = 20, Readiness = 50, SupplyBasisPoints = 5000 }
        };
        int victimPopulationBefore = simulation.CivilizationPopulationTenths(rivalId);
        CommandResult attack = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit, ActorId = playerId, EntityId = attackerId, Q = battle.Q, R = battle.R
        });
        Assert(attack.Accepted && FindUnit(state, defenderId) == null && simulation.LastResolvedBattles.Length == 1,
            "a decisive battle with no retreat route must disband the surrounded defender");
        BattleResolutionEvent result = simulation.LastResolvedBattles[0];
        int captured = state.Civilization(playerId).PrisonersHeldByCivilizationTenths[rivalId];
        Assert(result.DefenderPrisonersTenths > 0 && captured == result.DefenderPrisonersTenths,
            "survivors from a surrounded defeated force must become recorded prisoners");
        Assert(victimPopulationBefore - simulation.CivilizationPopulationTenths(rivalId) == result.DefenderDeathsTenths,
            "capturing survivors must preserve population accounting apart from the battle's recorded deaths");
    }

    private static void CheckSettlerInterceptionChoice(int seed, string choice)
    {
        GameSimulation simulation = GameSimulation.NewCampaign(seed, Difficulty.Standard);
        GameState state = simulation.State;
        int playerId = state.HumanCivilizationId;
        int rivalId = -1;
        for (int i = 0; i < state.Civilizations.Length; i++) if (state.Civilizations[i].Id != playerId) { rivalId = state.Civilizations[i].Id; break; }
        CityState playerCity = FirstCity(state, playerId);
        CityState rivalCity = FirstCity(state, rivalId);
        HexCell origin = null;
        HexCell wagonCell = null;
        HexCell target = null;
        for (int i = 0; i < state.Cells.Length && origin == null; i++)
        {
            HexCell candidate = state.Cells[i];
            HexCell middle = state.Cell(candidate.Q + 1, candidate.R);
            HexCell end = state.Cell(candidate.Q + 2, candidate.R);
            if (middle == null || end == null || !IsPassable(candidate) || !IsPassable(middle) || !IsPassable(end)) continue;
            bool anotherCity = false;
            for (int c = 0; c < state.Cities.Length; c++)
            {
                CityState city = state.Cities[c];
                if (city == playerCity || city == rivalCity) continue;
                if ((city.Q == candidate.Q && city.R == candidate.R) || (city.Q == middle.Q && city.R == middle.R) ||
                    (city.Q == end.Q && city.R == end.R)) anotherCity = true;
            }
            if (anotherCity) continue;
            origin = candidate; wagonCell = middle; target = end;
        }
        Assert(origin != null, "a three-hex corridor must be available for the intercepted convoy scenario");
        rivalCity.Q = origin.Q; rivalCity.R = origin.R;
        playerCity.Q = target.Q; playerCity.R = target.R;
        HexCell[] corridor = { origin, wagonCell, target };
        for (int i = 0; i < corridor.Length; i++)
        {
            corridor[i].Terrain = TerrainKind.Plains;
            corridor[i].Improvement = ImprovementKind.None;
            corridor[i].DistrictCityId = -1;
            corridor[i].DistrictBuildingId = string.Empty;
        }
        origin.OwnerId = rivalId;
        wagonCell.OwnerId = playerId;
        target.OwnerId = playerId;
        int attackerId = state.NextEntityId++;
        int wagonId = state.NextEntityId++;
        state.Units = new[]
        {
            new UnitState { Id = attackerId, OwnerId = rivalId, CitySourceId = rivalCity.Id, Kind = UnitKind.FieldForce,
                Q = origin.Q, R = origin.R, Strength = 100, InitialStrength = 100, PopulationTenths = 10, InitialPopulationTenths = 10,
                Readiness = 100, SupplyBasisPoints = 10000, MovementLeft = 2 },
            new UnitState { Id = wagonId, OwnerId = playerId, CitySourceId = playerCity.Id, Kind = UnitKind.SettlerWagon,
                Q = wagonCell.Q, R = wagonCell.R, Strength = 0, PopulationTenths = 20, InitialPopulationTenths = 20,
                Readiness = 100, SupplyBasisPoints = 10000, MovementLeft = 2 }
        };
        state.Conflicts = new[] { new ConflictState { Id = state.NextEntityId++, AggressorId = rivalId, DefenderId = playerId, Phase = 2, Active = true } };

        simulation.EndTurn();
        state = simulation.State;
        UnitState wagon = FindUnit(state, wagonId);
        ActiveEventState pending = null;
        for (int i = 0; i < state.Events.Length; i++)
            if (!state.Events[i].IsResolved && state.Events[i].Kind == EventKind.SettlerInterception && state.Events[i].TargetUnitId == wagonId) pending = state.Events[i];
        Assert(pending != null && wagon != null && wagon.Q == wagonCell.Q && wagon.R == wagonCell.R,
            "the convoy owner's UI must receive a choice when an AI war order intercepts its wagon");
        CommandResult blockedMove = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.MoveUnit, ActorId = playerId, EntityId = wagonId, Q = target.Q, R = target.R
        });
        Assert(!blockedMove.Accepted && blockedMove.Code == "settler_decision_pending",
            "a convoy cannot move before its interception decision is resolved");
        CommandResult decision = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.EventChoice, ActorId = playerId, EntityId = pending.Id, Text = choice
        });
        Assert(decision.Accepted && simulation.State.Events[simulation.State.Events.Length - 1].IsResolved,
            "each convoy choice must resolve the interception event");
        wagon = FindUnit(simulation.State, wagonId);
        if (choice == "Retreat")
            Assert(wagon != null && (wagon.Q != wagonCell.Q || wagon.R != wagonCell.R), "retreat must move the convoy to a safe owned hex");
        else if (choice == "Internment")
        {
            Assert(wagon != null && wagon.MovementLeft == 0, "internment must remove the convoy's remaining movement for the turn");
            CommandResult stillInterned = simulation.Issue(new GameCommand
            {
                Kind = CommandKind.MoveUnit, ActorId = playerId, EntityId = wagonId, Q = target.Q, R = target.R
            });
            Assert(!stillInterned.Accepted && stillInterned.Code == "movement_spent", "an interned convoy must not continue in the same turn");
        }
        else
            Assert(wagon != null && wagon.PopulationTenths < 20 && wagon.PopulationTenths > 0,
                "a risky breakout must preserve the wagon while causing nonfatal population loss");
    }

    private static void SetUpWar(GameState state, int playerId, int rivalId)
    {
        state.Conflicts = new[]
        {
            new ConflictState { Id = 9020, AggressorId = playerId, DefenderId = rivalId, Phase = 2, Active = true }
        };
    }

    private static UnitState FindUnit(GameState state, int id)
    {
        for (int i = 0; i < state.Units.Length; i++) if (state.Units[i].Id == id) return state.Units[i];
        return null;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
