using System;
using System.Collections.Generic;
using Nasledie.Core;

internal static class FreshScienceVictoryScenario
{
    private static readonly string[][] ResearchOrder =
    {
        new[] { "writing", "stone", "pottery" },
        new[] { "civic_schools", "roads", "medicine"},
        new[] { "university", "engineering", "printing" },
        new[] { "mechanization", "electricity", "mass_education" },
        new[] { "computing_networks", "open_science" }
    };

    public static void Run(Action<string> output)
    {
        int[] seeds = { 1028 };
        for (int i = 0; i < seeds.Length; i++)
        {
            GameSimulation simulation = GameSimulation.NewCampaign(seeds[i], Difficulty.Calm);
            int playerId = simulation.State.HumanCivilizationId;
            int turns = 0;
            while (!simulation.State.IsFinished && turns < simulation.State.MaximumTurn)
            {
                int turnBefore = simulation.State.Turn;
                ResolveCrisis(simulation, playerId);
                ChooseResearch(simulation, playerId);
                AssignWorkers(simulation, playerId);
                PlanProjects(simulation, playerId);
                TryStartScientificProject(simulation, playerId);
                MoveSettlers(simulation, playerId);
                EstablishTradeRoute(simulation, playerId);

                simulation.EndTurn();
                turns++;
                if (!simulation.State.IsFinished)
                    Require(simulation.State.Turn == turnBefore + 1, "science campaign stalled on turn " + turnBefore);

            }

            CivilizationState player = simulation.State.Civilization(playerId);
            int universityCities = 0;
            int activeScienceProjects = 0;
            bool hasAcademy = false;
            for (int cityIndex = 0; cityIndex < simulation.State.Cities.Length; cityIndex++)
            {
                CityState city = simulation.State.Cities[cityIndex];
                if (city.OwnerId != playerId) continue;
                if (city.BuildingLevel("university") > 0) universityCities++;
                if (city.BuildingLevel("academy") > 0) hasAcademy = true;
                if (city.Project != null && city.Project.Kind == ProjectKind.ScientificProject) activeScienceProjects++;
            }

            Require(simulation.State.IsFinished, "fresh science campaign did not finish before the turn limit");
            Require(simulation.State.WinnerCivilizationId == playerId, "the human did not win the fresh science campaign");
            Require(simulation.State.WinningPath == VictoryPath.Scientific, "fresh campaign did not use the scientific victory");
            Require(simulation.State.Turn <= simulation.State.MaximumTurn, "science victory went beyond the configured turn limit");
            Require(player.Knows("computing_networks") && player.Knows("open_science"), "science victory skipped a required final technology");
            Require(universityCities >= 2 && hasAcademy, "science victory lacked its two universities or academy");
            Require(player.ScienceProjectTurns == 4 && activeScienceProjects == 1, "science project did not complete exactly once");
            Require(player.LastTurnKnowledgeOutputTenths >= 50 && player.PreviousTurnKnowledgeOutputTenths >= 50,
                "science victory ignored the two-turn knowledge output requirement");
            output("PASS: fresh campaign seed 1028 won by science on turn " + simulation.State.Turn + " with two cities.");
        }
        output("PASS: the scientific victory is reachable from a new campaign within the configured turn limit.");
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
        else if (player.InfluenceTenths >= amount * 10) resource = "influence";
        else
        {
            int reserve = 0;
            for (int i = 0; i < simulation.State.Cities.Length; i++)
                if (simulation.State.Cities[i].OwnerId == playerId) reserve = Math.Max(reserve, simulation.State.Cities[i].FoodTenths);
            if (reserve >= amount * 10) resource = "food";
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

    private static void AssignWorkers(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        for (int i = 0; i < simulation.State.Cities.Length; i++)
        {
            CityState city = simulation.State.Cities[i];
            if (city.OwnerId != playerId) continue;
            CityYield yield = EconomyCalculator.Calculate(simulation.State, city);
            string focus;
            if (city.Project != null && city.Project.Kind != ProjectKind.None) focus = "industry";
            else if (city.FoodTenths < city.PopulationTenths * 2 && yield.FoodTenths < city.PopulationTenths) focus = "food";
            else if (player.TreasuryTenths < 2000) focus = "trade";
            else focus = "science";

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
        int settlers = 0, universities = 0;
        bool hasCouncil = false, hasEraTransition = false, hasAcademy = false;
        int technologiesThisEra = 0;
        for (int i = 0; i < state.Units.Length; i++) if (state.Units[i].OwnerId == playerId && state.Units[i].Kind == UnitKind.SettlerWagon) settlers++;
        for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == playerId && state.Cities[i].Project.Kind == ProjectKind.Recruitment && state.Cities[i].Project.TargetId == UnitKind.SettlerWagon.ToString()) settlers++;
        for (int i = 0; i < state.Cities.Length; i++)
        {
            CityState city = state.Cities[i];
            if (city.OwnerId != playerId) continue;
            hasCouncil |= city.BuildingLevel("council") > 0;
            hasEraTransition |= city.Project != null && city.Project.Kind == ProjectKind.EraTransition;
            hasAcademy |= city.BuildingLevel("academy") > 0 || (city.Project != null && city.Project.TargetId == "academy");
            if (city.BuildingLevel("university") > 0 || (city.Project != null && city.Project.TargetId == "university")) universities++;
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
            if (city.Id == capitalId && cityCount + settlers < 2 && capital.PopulationTenths >= 80 && capital.FoodTenths >= 60)
            {
                CommandResult recruit = simulation.Issue(new GameCommand { Kind = CommandKind.Recruit, ActorId = playerId, EntityId = city.Id, Value = (int)UnitKind.SettlerWagon });
                if (recruit.Accepted) continue;
            }
            string project = null;
            if (!hasCouncil && player.Knows("writing")) { project = "council"; hasCouncil = true; }
            else if (city.Id == capitalId && player.Knows("stone") && city.BuildingLevel("workshop") == 0) project = "workshop";
            else if (!hasEraTransition && technologiesThisEra >= 3 && hasCouncil && (int)player.Era < 4 && player.InfluenceTenths >= 100 && city.Id == capitalId)
            { project = "era_transition"; hasEraTransition = true; }
            else if (player.Knows("university") && universities < 2 && city.BuildingLevel("university") == 0) { project = "university"; universities++; }
            else if (player.Knows("computing_networks") && !hasAcademy) { project = "academy"; hasAcademy = true; }
            if (project != null) simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = playerId, EntityId = city.Id, Text = project });
        }
    }

    private static void TryStartScientificProject(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        if (!player.Knows("computing_networks") || !player.Knows("open_science") || player.KnowledgeTenths < 300 || player.InfluenceTenths < 80) return;
        for (int i = 0; i < simulation.State.Cities.Length; i++)
        {
            CityState city = simulation.State.Cities[i];
            if (city.OwnerId != playerId || !VictoryEvaluator.CanStartScientificProgram(simulation.State, playerId, city.Id)) continue;
            CommandResult result = simulation.Issue(new GameCommand { Kind = CommandKind.BeginVictoryProject, ActorId = playerId, EntityId = city.Id, Value = (int)VictoryPath.Scientific });
            if (result.Accepted) return;
        }
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

    private static void EstablishTradeRoute(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        if (player.InfluenceTenths < 10) return;
        for (int i = 0; i < simulation.State.Treaties.Length; i++)
        {
            TreatyState treaty = simulation.State.Treaties[i];
            if ((treaty.Active || treaty.Pending) && treaty.Kind == TreatyKind.TradeRoute &&
                (treaty.FromCivilizationId == playerId || treaty.ToCivilizationId == playerId)) return;
        }
        for (int partner = 0; partner < simulation.State.Civilizations.Length; partner++)
        {
            if (partner == playerId || simulation.State.Civilization(partner).IsDefeated) continue;
            int partnerCapital = simulation.FindCapital(partner);
            if (partnerCapital < 0) continue;
            simulation.Issue(new GameCommand
            {
                Kind = CommandKind.ProposeTreaty,
                ActorId = playerId,
                TargetId = partner,
                Value = (int)TreatyKind.TradeRoute,
                EntityId = simulation.FindCapital(playerId),
                Sequence = partnerCapital
            });
            return;
        }
    }

    private static int CountCities(GameState state, int playerId)
    {
        int count = 0;
        for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == playerId) count++;
        return count;
    }

    private static int CellKey(int q, int r) { return q * 100 + r; }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

