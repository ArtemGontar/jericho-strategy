using System;
using System.Collections.Generic;
using Nasledie.Core;

internal static class FreshCulturalVictoryScenario
{
    private static readonly string[][] ResearchOrder =
    {
        new[] { "writing", "stone", "pottery" },
        new[] { "coin_markets", "civic_schools", "roads" },
        new[] { "trade_league", "printing", "engineering" },
        new[] { "mechanization", "electricity", "mass_education" },
        new[] { "computing_networks", "open_science" }
    };

    public static void Run(Action<string> output)
    {
        int[] seeds = { 1028, 1000, 1017, 1029 };
        for (int i = 0; i < seeds.Length; i++)
        {
            GameSimulation simulation = GameSimulation.NewCampaign(seeds[i], Difficulty.Calm);
            int playerId = simulation.State.HumanCivilizationId;
            bool[] openedBorders = new bool[simulation.State.Civilizations.Length];
            int turns = 0;
            while (!simulation.State.IsFinished && turns < simulation.State.MaximumTurn)
            {
                int turnBefore = simulation.State.Turn;
                ResolveCrisis(simulation, playerId);
                ManageBudget(simulation, playerId);
                ChooseResearch(simulation, playerId);
                AssignWorkers(simulation, playerId);
                PlanProjects(simulation, playerId);
                MoveSettlers(simulation, playerId);
                EstablishOpenBorders(simulation, playerId, openedBorders);
                EstablishTradeRoutes(simulation, playerId);
                TryStartCulturalFestival(simulation, playerId);

                simulation.EndTurn();
                turns++;
                if (!simulation.State.IsFinished)
                    Require(simulation.State.Turn == turnBefore + 1, "culture campaign stalled on turn " + turnBefore);

            }

            CivilizationState player = simulation.State.Civilization(playerId);
            Require(simulation.State.IsFinished, "fresh cultural campaign did not finish before the turn limit; prestige " + player.CulturePrestige +
                ", influence " + string.Join("/", player.CulturalInfluence) + ", cities " + CountCities(simulation.State, playerId) +
                ", canStart " + VictoryEvaluator.CanStartCulturalFestival(simulation.State, playerId) + ", stored influence " + player.InfluenceTenths +
                ", treasury " + player.TreasuryTenths + ", culture house/archive/exchange " + CountBuilding(simulation.State, playerId, "cultural_house") + "/" +
                CountBuilding(simulation.State, playerId, "archive") + "/" + CountBuilding(simulation.State, playerId, "exchange_house"));
            Require(simulation.State.WinnerCivilizationId == playerId, "the human did not win the fresh cultural campaign");
            Require(simulation.State.WinningPath == VictoryPath.Cultural, "fresh campaign ended via " + VictoryEvaluator.PathName(simulation.State.WinningPath) +
                " on turn " + simulation.State.Turn + " before the cultural festival; prestige " + player.CulturePrestige +
                ", influence " + string.Join("/", player.CulturalInfluence) + ", cities " + CountCities(simulation.State, playerId) +
                ", canStart " + VictoryEvaluator.CanStartCulturalFestival(simulation.State, playerId) + ", stored influence " + player.InfluenceTenths +
                ", treasury " + player.TreasuryTenths + ", culture house/archive/exchange " + CountBuilding(simulation.State, playerId, "cultural_house") + "/" +
                CountBuilding(simulation.State, playerId, "archive") + "/" + CountBuilding(simulation.State, playerId, "exchange_house"));
            Require(simulation.State.Turn <= simulation.State.MaximumTurn, "cultural victory went beyond the configured turn limit");
            Require(player.CulturePrestige >= 100 && VictoryEvaluator.HasCulturalVictoryStructures(simulation.State, playerId),
                "cultural victory skipped its prestige or three distinct city landmarks");
            output("PASS: fresh campaign seed " + seeds[i] + " won by culture on turn " + simulation.State.Turn + " with " + CountCities(simulation.State, playerId) + " cities.");
        }
        output("PASS: the cultural victory is reachable from a new campaign within the configured turn limit.");
    }

    private static void ResolveCrisis(GameSimulation simulation, int playerId)
    {
        CrisisState crisis = simulation.State.Crisis;
        CivilizationState player = simulation.State.Civilization(playerId);
        if (crisis == null || crisis.Kind == CrisisKind.None || crisis.Resolved || crisis.TurnsActive < 2 ||
            (crisis.ContributorsMask & (1 << playerId)) != 0) return;

        int amount = (crisis.Threat + 3) / 4;
        string resource = null;
        if (player.KnowledgeTenths >= amount * 10) resource = "knowledge";
        else
        {
            int reserve = 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
                if (simulation.State.Cities[i].OwnerId == playerId) reserve = Math.Max(reserve, simulation.State.Cities[i].FoodTenths);
            if (reserve >= amount * 10) resource = "food";
            else if (player.InfluenceTenths >= amount * 10 + 200) resource = "influence";
        }
        if (resource == null) return;

        CommandResult result = simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ContributeToCrisis,
            ActorId = playerId,
            Text = resource,
            Value = amount
        });
        Require(result.Accepted, "the campaign could not contribute " + resource + " to an active crisis");
    }

    private static void ChooseResearch(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        string[] choices = ResearchOrder[(int)player.Era];
        for (int i = 0; i < choices.Length; i++)
        {
            if (player.Knows(choices[i])) continue;
            CommandResult result = simulation.Issue(new GameCommand
            {
                Kind = CommandKind.ChooseTechnology,
                ActorId = playerId,
                Text = choices[i]
            });
            if (result.Accepted) return;
        }
    }

    private static void ManageBudget(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        if (player.TreasuryTenths < 500 && player.TaxRate != TaxRate.High)
            simulation.Issue(new GameCommand { Kind = CommandKind.SetTax, ActorId = playerId, Value = (int)TaxRate.High });
        else if (player.TreasuryTenths >= 1200 && player.TaxRate == TaxRate.High)
            simulation.Issue(new GameCommand { Kind = CommandKind.SetTax, ActorId = playerId, Value = (int)TaxRate.Normal });
    }

    private static void AssignWorkers(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        int capitalId = simulation.FindCapital(playerId);
        int cityCount = CountCities(simulation.State, playerId);
        bool needsCultureTechnologies = !player.Knows("trade_league") || !player.Knows("printing");
            bool protectReadyFestival = player.CulturePrestige >= 90 && player.TreasuryTenths >= 100;
            bool needsFestivalFunding = player.TreasuryTenths < 800 && !protectReadyFestival;
        for (int i = 0; i < simulation.State.Cities.Length; i++)
        {
            CityState city = simulation.State.Cities[i];
            if (city.OwnerId != playerId) continue;
            CityYield yield = EconomyCalculator.Calculate(simulation.State, city);
            string focus;
            bool needsFestivalFunds = player.CulturePrestige >= 100 && player.TreasuryTenths < 1000;
            bool culturalConstruction = city.Project != null && (city.Project.TargetId == "cultural_house" || city.Project.TargetId == "archive" || city.Project.TargetId == "exchange_house");
            if (culturalConstruction) focus = "industry";
            else if (city.FoodTenths < city.PopulationTenths * 2 && yield.FoodTenths < city.PopulationTenths) focus = "food";
            else if (needsCultureTechnologies && (city.Id != capitalId || cityCount == 1)) focus = "science";
            else if (needsFestivalFunds) focus = "trade";
            else if (needsFestivalFunding && city.Id == capitalId) focus = "trade";
            else focus = "culture";

            simulation.Issue(new GameCommand
            {
                Kind = CommandKind.AutoAssignWorkers,
                ActorId = playerId,
                EntityId = city.Id,
                Text = focus
            });
        }
    }

    private static void PlanProjects(GameSimulation simulation, int playerId)
    {
        GameState state = simulation.State;
        CivilizationState player = state.Civilization(playerId);
        int capitalId = simulation.FindCapital(playerId);
        CityState capital = state.City(capitalId);
        int cityCount = CountCities(state, playerId);
        int settlers = 0;
        bool hasCouncil = false, hasEraTransition = false, hasWorkshop = false, hasMarket = false;
        bool hasCulturalHouse = false, hasArchive = false, hasExchangeHouse = false;
        int technologiesThisEra = 0;
        for (int i = 0; i < state.Units.Length; i++) if (state.Units[i].OwnerId == playerId && state.Units[i].Kind == UnitKind.SettlerWagon) settlers++;
        for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == playerId && state.Cities[i].Project.Kind == ProjectKind.Recruitment && state.Cities[i].Project.TargetId == UnitKind.SettlerWagon.ToString()) settlers++;
        for (int i = 0; i < state.Cities.Length; i++)
        {
            CityState city = state.Cities[i];
            if (city.OwnerId != playerId) continue;
            hasCouncil |= city.BuildingLevel("council") > 0;
            hasEraTransition |= city.Project != null && city.Project.Kind == ProjectKind.EraTransition;
            hasWorkshop |= city.BuildingLevel("workshop") > 0 || (city.Project != null && city.Project.TargetId == "workshop");
            hasMarket |= city.BuildingLevel("market") > 0 || (city.Project != null && city.Project.TargetId == "market");
            hasCulturalHouse |= city.BuildingLevel("cultural_house") > 0 || (city.Project != null && city.Project.TargetId == "cultural_house");
            hasArchive |= city.BuildingLevel("archive") > 0 || (city.Project != null && city.Project.TargetId == "archive");
            hasExchangeHouse |= city.BuildingLevel("exchange_house") > 0 || (city.Project != null && city.Project.TargetId == "exchange_house");
        }
        for (int i = 0; i < player.KnownTechnologies.Length; i++)
        {
            TechnologyDefinition definition = RulesCatalog.Technology(player.KnownTechnologies[i]);
            if (definition != null && definition.Era == player.Era) technologiesThisEra++;
        }
        for (int i = 0; i < state.Cities.Length; i++)
        {
            CityState city = state.Cities[i];
            if (city.OwnerId != playerId || city.Project == null || city.Project.Kind != ProjectKind.None) continue;
            if (city.Id == capitalId && cityCount + settlers < 3 && capital.PopulationTenths >= 80 && capital.FoodTenths >= 60)
            {
                CommandResult recruit = simulation.Issue(new GameCommand { Kind = CommandKind.Recruit, ActorId = playerId, EntityId = city.Id, Value = (int)UnitKind.SettlerWagon });
                if (recruit.Accepted) continue;
            }
            string project = null;
            if (!hasCouncil && player.Knows("writing")) { project = "council"; hasCouncil = true; }
            else if (city.Id == capitalId && player.Knows("writing") && !hasCulturalHouse) { project = "cultural_house"; hasCulturalHouse = true; }
            else if (city.Id == capitalId && player.Knows("stone") && !hasWorkshop) { project = "workshop"; hasWorkshop = true; }
            else if (city.Id == capitalId && player.Knows("coin_markets") && !hasMarket) { project = "market"; hasMarket = true; }
            else if (!hasEraTransition && technologiesThisEra >= 3 && hasCouncil && (int)player.Era < (int)Era.ConnectedLands && player.InfluenceTenths >= 100 && city.Id == capitalId)
            { project = "era_transition"; hasEraTransition = true; }
            else if (city.Id != capitalId && player.Knows("printing") && !hasArchive && IsNone(city, "cultural_house")) { project = "archive"; hasArchive = true; }
            else if (player.Knows("trade_league") && !hasExchangeHouse && city.Id != capitalId && IsNone(city, "archive") && IsNone(city, "cultural_house") &&
                     (city.Project == null || city.Project.TargetId != "archive")) { project = "exchange_house"; hasExchangeHouse = true; }
            if (project != null) simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = playerId, EntityId = city.Id, Text = project });
        }
    }

    private static void TryStartCulturalFestival(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        if (player.CultureFestivalTurns > 0 || player.InfluenceTenths < 200 || player.TreasuryTenths < 100 ||
            !VictoryEvaluator.CanStartCulturalFestival(simulation.State, playerId)) return;
        CommandResult result = simulation.Issue(new GameCommand { Kind = CommandKind.BeginVictoryProject, ActorId = playerId, EntityId = simulation.FindCapital(playerId), Value = (int)VictoryPath.Cultural });
        if (!result.Accepted) throw new InvalidOperationException("culture festival start was rejected: " + result.Code);
    }

    private static void MoveSettlers(GameSimulation simulation, int playerId)
    {
        GameState state = simulation.State;
        for (int i = 0; i < state.Units.Length; i++)
        {
            UnitState settler = state.Units[i];
            if (settler.OwnerId != playerId || settler.Kind != UnitKind.SettlerWagon) continue;
            HexCell target = FindSettlementSite(state, settler, playerId);
            if (target == null) continue;
            while (settler.MovementLeft > 0 && HexGrid.Distance(settler.Q, settler.R, target.Q, target.R) > 0)
            {
                HexCell step = FindPathStep(state, playerId, settler, target);
                if (step == null) break;
                CommandResult move = simulation.Issue(new GameCommand
                {
                    Kind = CommandKind.MoveUnit,
                    ActorId = playerId,
                    EntityId = settler.Id,
                    Q = step.Q,
                    R = step.R
                });
                if (!move.Accepted) break;
            }
            if (settler.Q == target.Q && settler.R == target.R)
            {
                CommandResult found = simulation.Issue(new GameCommand
                {
                    Kind = CommandKind.FoundCity,
                    ActorId = playerId,
                    EntityId = settler.Id
                });
                if (found.Accepted) i--;
            }
        }
    }

    private static HexCell FindSettlementSite(GameState state, UnitState settler, int playerId)
    {
        HexCell best = null;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < state.Cells.Length; i++)
        {
            HexCell cell = state.Cells[i];
            if (cell.OwnerId >= 0 || cell.Terrain == TerrainKind.Mountain || cell.Terrain == TerrainKind.Water || cell.Terrain == TerrainKind.Swamp) continue;
            bool spaced = true;
            for (int j = 0; j < state.Cities.Length; j++)
                if (HexGrid.Distance(cell.Q, cell.R, state.Cities[j].Q, state.Cities[j].R) < 3) { spaced = false; break; }
            if (!spaced) continue;
            int distance = HexGrid.Distance(cell.Q, cell.R, settler.Q, settler.R);
            if (distance < bestDistance) { best = cell; bestDistance = distance; }
        }
        return best;
    }

    private static HexCell FindPathStep(GameState state, int playerId, UnitState unit, HexCell target)
    {
        var queue = new Queue<HexCell>();
        var parents = new Dictionary<int, int>();
        int start = CellKey(unit.Q, unit.R);
        int goal = CellKey(target.Q, target.R);
        HexCell startCell = state.Cell(unit.Q, unit.R);
        if (startCell == null) return null;
        queue.Enqueue(startCell);
        parents[start] = start;
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
                if (parents.ContainsKey(key)) continue;
                HexCell next = state.Cell(q, r);
                if (next == null || next.Terrain == TerrainKind.Mountain || next.Terrain == TerrainKind.Water ||
                    (next.OwnerId >= 0 && next.OwnerId != playerId)) continue;
                parents[key] = currentKey;
                queue.Enqueue(next);
            }
        }
        if (!parents.ContainsKey(goal)) return null;
        int cursor = goal;
        while (parents[cursor] != start)
        {
            cursor = parents[cursor];
            if (cursor == start) return null;
        }
        return state.Cell(cursor / 100, cursor % 100);
    }

    private static void EstablishTradeRoutes(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        if (player.InfluenceTenths < 10 || VictoryEvaluator.CanStartCulturalFestival(simulation.State, playerId)) return;
        for (int partner = 0; partner < simulation.State.Civilizations.Length; partner++)
        {
            if (partner == playerId || simulation.State.Civilization(partner).IsDefeated) continue;
            bool hasRoute = false;
            for (int i = 0; i < simulation.State.Treaties.Length; i++)
            {
                TreatyState treaty = simulation.State.Treaties[i];
                if ((treaty.Active || treaty.Pending) && treaty.Kind == TreatyKind.TradeRoute && treaty.FromCivilizationId == playerId && treaty.ToCivilizationId == partner)
                { hasRoute = true; break; }
            }
            if (hasRoute) continue;
            int partnerCapital = simulation.FindCapital(partner);
            if (partnerCapital < 0) continue;
            CommandResult result = simulation.Issue(new GameCommand
            {
                Kind = CommandKind.ProposeTreaty,
                ActorId = playerId,
                TargetId = partner,
                Value = (int)TreatyKind.TradeRoute,
                EntityId = simulation.FindCapital(playerId),
                Sequence = partnerCapital
            });
            if (result.Accepted) return;
        }
    }

    private static void EstablishOpenBorders(GameSimulation simulation, int playerId, bool[] openedBorders)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        if (VictoryEvaluator.CanStartCulturalFestival(simulation.State, playerId)) return;
        if (player.InfluenceTenths < 20 || CountBuilding(simulation.State, playerId, "cultural_house") == 0) return;
        for (int partner = 0; partner < simulation.State.Civilizations.Length; partner++)
        {
            if (partner == playerId || openedBorders[partner] || simulation.State.Civilization(partner).IsDefeated) continue;
            int partnerCapital = simulation.FindCapital(partner);
            if (partnerCapital < 0) continue;
            bool alreadyProposed = false;
            for (int i = 0; i < simulation.State.Treaties.Length; i++)
            {
                TreatyState treaty = simulation.State.Treaties[i];
                if ((treaty.Active || treaty.Pending) && treaty.Kind == TreatyKind.OpenBorders &&
                    treaty.FromCivilizationId == playerId && treaty.ToCivilizationId == partner) { alreadyProposed = true; break; }
            }
            if (alreadyProposed) { openedBorders[partner] = true; continue; }
            CommandResult result = simulation.Issue(new GameCommand
            {
                Kind = CommandKind.ProposeTreaty,
                ActorId = playerId,
                TargetId = partner,
                Value = (int)TreatyKind.OpenBorders,
                EntityId = simulation.FindCapital(playerId),
                Sequence = partnerCapital
            });
            if (result.Accepted) openedBorders[partner] = true;
            return;
        }
    }

    private static int CountCities(GameState state, int playerId)
    {
        int count = 0;
        for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == playerId) count++;
        return count;
    }

    private static bool IsNone(CityState city, string building)
    {
        return city.BuildingLevel(building) == 0 && (city.Project == null || city.Project.TargetId != building);
    }

    private static int CountBuilding(GameState state, int playerId, string id)
    {
        int count = 0;
        for (int i = 0; i < state.Cities.Length; i++)
            if (state.Cities[i].OwnerId == playerId && state.Cities[i].BuildingLevel(id) > 0) count++;
        return count;
    }

    private static int CellKey(int q, int r) { return q * 100 + r; }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

