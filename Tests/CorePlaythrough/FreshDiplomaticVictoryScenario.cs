using System;
using System.Collections.Generic;
using Nasledie.Core;

internal static class FreshDiplomaticVictoryScenario
{
    private static readonly string[][] ResearchOrder =
    {
        new[] { "writing", "stone", "pottery" },
        new[] { "coin_markets", "civic_schools", "roads" },
        new[] { "trade_league", "printing", "engineering" },
        new[] { "mechanization", "electricity", "free_press" },
        new[] { "international_assembly" }
    };

    public static void Run(Action<string> output)
    {
        int[] seeds = { 1028, 1000, 1017, 1029 };
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
                MoveSettlers(simulation, playerId);
                EstablishDiplomaticRelations(simulation, playerId);
                TryStartDiplomaticCoalition(simulation, playerId);

                simulation.EndTurn();
                turns++;
                if (!simulation.State.IsFinished)
                    Require(simulation.State.Turn == turnBefore + 1, "diplomacy campaign stalled on turn " + turnBefore);
            }

            CivilizationState player = simulation.State.Civilization(playerId);
            int assemblyCityId = -1;
            for (int cityIndex = 0; cityIndex < simulation.State.Cities.Length; cityIndex++)
            {
                CityState city = simulation.State.Cities[cityIndex];
                if (city.OwnerId == playerId && city.BuildingLevel("assembly") > 0) assemblyCityId = city.Id;
            }
            int partnerCount = 0;
            int livePartners = 0;
            for (int partner = 0; partner < simulation.State.Civilizations.Length; partner++)
            {
                if (partner == playerId || simulation.State.Civilization(partner).IsDefeated) continue;
                if (player.Relations[partner] >= 25 && player.Trusts[partner] >= 50) partnerCount++;
                if (HasActiveRoute(simulation.State, playerId, partner, assemblyCityId)) livePartners++;
            }

            Require(simulation.State.IsFinished, "fresh diplomatic campaign did not finish before the turn limit; era " + player.Era +
                ", technologies " + string.Join(",", player.KnownTechnologies) + ", relations " + string.Join("/", player.Relations) +
                ", crisis mask " + simulation.State.ResolvedGlobalCrisisContributorMask);
            Require(simulation.State.WinnerCivilizationId == playerId, "the human did not win the fresh diplomatic campaign; winner " +
                simulation.State.WinnerCivilizationId + " path " + VictoryEvaluator.PathName(simulation.State.WinningPath) + " turn " + simulation.State.Turn +
                ", player era " + player.Era + " techs " + string.Join(",", player.KnownTechnologies) + " relations " + string.Join("/", player.Relations) +
                ", influence " + player.InfluenceTenths + ", treasury " + player.TreasuryTenths + ", assembly " + assemblyCityId +
                ", qualified votes " + partnerCount + ", live routes " + livePartners + ", crisis " +
                (simulation.State.Crisis == null ? "none" : simulation.State.Crisis.Kind + "/" + simulation.State.Crisis.Resolved) +
                ", resolved mask " + simulation.State.ResolvedGlobalCrisisContributorMask + ", projects " + ProjectSummary(simulation.State, playerId) +
                ", routes " + RouteSummary(simulation.State, playerId) + ", market slots " +
                (1 + CountBuilding(simulation.State, playerId, "market") + (player.Knows("trade_league") ? 1 : 0)));
            Require(simulation.State.WinningPath == VictoryPath.Diplomatic, "fresh campaign ended via " + VictoryEvaluator.PathName(simulation.State.WinningPath) +
                " on turn " + simulation.State.Turn + " instead of the diplomatic victory; era " + player.Era + ", techs " +
                string.Join(",", player.KnownTechnologies) + ", relations " + string.Join("/", player.Relations) + ", influence " +
                player.InfluenceTenths + ", assembly " + assemblyCityId + ", votes " + partnerCount + ", routes " + livePartners +
                ", crisis mask " + simulation.State.ResolvedGlobalCrisisContributorMask + ", projects " + ProjectSummary(simulation.State, playerId) +
                ", active routes " + RouteSummary(simulation.State, playerId));
            Require(simulation.State.Turn <= simulation.State.MaximumTurn, "diplomatic victory went beyond the configured turn limit");
            Require(player.Knows("international_assembly") && assemblyCityId >= 0, "diplomatic victory skipped its final technology or assembly building");
            Require(partnerCount >= 2 && livePartners >= 2, "diplomatic victory did not secure two qualified votes and live assembly routes");
            Require(CountBits(simulation.State.ResolvedGlobalCrisisContributorMask) >= 3 &&
                (simulation.State.ResolvedGlobalCrisisContributorMask & (1 << playerId)) != 0,
                "diplomatic victory did not include the human and at least two rivals in the resolved crisis");
            output("PASS: fresh campaign seed " + seeds[i] + " won diplomatically on turn " + simulation.State.Turn + " with " + partnerCount + " allied votes.");
        }
        output("PASS: the diplomatic victory is reachable from a new campaign within the configured turn limit.");
    }

    private static void ResolveCrisis(GameSimulation simulation, int playerId)
    {
        CrisisState crisis = simulation.State.Crisis;
        CivilizationState player = simulation.State.Civilization(playerId);
        if (crisis == null || crisis.Kind == CrisisKind.None || crisis.Resolved || crisis.TurnsActive < 2) return;

        int participants = CountBits(crisis.ContributorsMask);
        int amount;
        if (participants < 3 && crisis.Threat <= 20) return;
        if (participants >= 3) amount = (crisis.Threat + 3) / 4;
        else amount = 5;
        string resource = null;
        int reserve = 0;
        for (int i = 0; i < simulation.State.Cities.Length; i++)
            if (simulation.State.Cities[i].OwnerId == playerId) reserve = Math.Max(reserve, simulation.State.Cities[i].FoodTenths);
        if (player.KnowledgeTenths >= amount * 10) resource = "knowledge";
        else if (reserve >= amount * 10) resource = "food";
        else if (player.InfluenceTenths >= amount * 10) resource = "influence";
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
        int capitalId = simulation.FindCapital(playerId);
        for (int i = 0; i < simulation.State.Cities.Length; i++)
        {
            CityState city = simulation.State.Cities[i];
            if (city.OwnerId != playerId) continue;
            string focus;
            if (city.FoodTenths < city.PopulationTenths * 12 / 10) focus = "food";
            else if (player.TreasuryTenths < 0) focus = "trade";
            else if (city.Project != null && (city.Project.Kind == ProjectKind.EraTransition || city.Project.TargetId == "council" ||
                city.Project.TargetId == "school" || city.Project.TargetId == "assembly")) focus = "industry";
            else if (city.Id == capitalId && city.Project != null && city.Project.Kind != ProjectKind.None) focus = "industry";
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
        int settlers = 0;
        for (int i = 0; i < state.Units.Length; i++)
            if (state.Units[i].OwnerId == playerId && state.Units[i].Kind == UnitKind.SettlerWagon) settlers++;
        bool hasCouncil = false, hasEraTransition = false, hasWorkshop = false, hasMarket = false, hasAssembly = false, hasSchool = false;
        int technologiesThisEra = 0;
        for (int i = 0; i < state.Cities.Length; i++)
        {
            CityState city = state.Cities[i];
            if (city.OwnerId != playerId) continue;
            hasCouncil |= city.BuildingLevel("council") > 0;
            hasEraTransition |= city.Project != null && city.Project.Kind == ProjectKind.EraTransition;
            hasWorkshop |= city.BuildingLevel("workshop") > 0 || (city.Project != null && city.Project.TargetId == "workshop");
            hasMarket |= city.BuildingLevel("market") > 0 || (city.Project != null && city.Project.TargetId == "market");
            hasAssembly |= city.BuildingLevel("assembly") > 0 || (city.Project != null && city.Project.TargetId == "assembly");
            hasSchool |= city.BuildingLevel("school") > 0 || (city.Project != null && city.Project.TargetId == "school");
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
            if (city.Id == capitalId && cityCount + settlers < 2 && hasMarket && capital.PopulationTenths >= 80 && capital.FoodTenths >= 60)
            {
                CommandResult recruit = simulation.Issue(new GameCommand { Kind = CommandKind.Recruit, ActorId = playerId, EntityId = city.Id, Value = (int)UnitKind.SettlerWagon });
                if (recruit.Accepted) { settlers++; continue; }
            }
            string project = null;
            if (city.Id == capitalId)
            {
                if (!hasCouncil && player.Knows("writing")) { project = "council"; hasCouncil = true; }
                else if (player.Knows("stone") && city.BuildingLevel("workshop") == 0 && !hasWorkshop) { project = "workshop"; hasWorkshop = true; }
                else if (player.Knows("coin_markets") && city.BuildingLevel("market") == 0 && !hasMarket) { project = "market"; hasMarket = true; }
                else if (!hasEraTransition && technologiesThisEra >= 3 && hasCouncil && (int)player.Era < 4 && player.InfluenceTenths >= 100)
                { project = "era_transition"; hasEraTransition = true; }
                else if (player.Knows("international_assembly") && !hasAssembly) { project = "assembly"; hasAssembly = true; }
            }
            else if (!hasSchool && player.Knows("civic_schools") && city.BuildingLevel("school") == 0)
            {
                project = "school";
                hasSchool = true;
            }
            if (project != null) simulation.Issue(new GameCommand { Kind = CommandKind.SetProject, ActorId = playerId, EntityId = city.Id, Text = project });
        }
    }

    private static void TryStartDiplomaticCoalition(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        if (!player.Knows("international_assembly") || player.InfluenceTenths < 200) return;
        for (int i = 0; i < simulation.State.Cities.Length; i++)
        {
            CityState city = simulation.State.Cities[i];
            if (city.OwnerId != playerId || city.BuildingLevel("assembly") == 0 || !HasTwoDurableAssemblyRoutes(simulation.State, playerId, city.Id) ||
                !VictoryEvaluator.CanStartDiplomaticCoalition(simulation.State, playerId, city.Id)) continue;
            CommandResult result = simulation.Issue(new GameCommand { Kind = CommandKind.BeginVictoryProject, ActorId = playerId, EntityId = city.Id, Value = (int)VictoryPath.Diplomatic });
            if (!result.Accepted) throw new InvalidOperationException("diplomatic coalition launch was rejected: " + result.Code);
            return;
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

    private static void EstablishDiplomaticRelations(GameSimulation simulation, int playerId)
    {
        CivilizationState player = simulation.State.Civilization(playerId);
        int assemblyCityId = simulation.FindCapital(playerId);
        if (CountCities(simulation.State, playerId) < 2 || !player.Knows("coin_markets") || CountBuilding(simulation.State, playerId, "market") == 0) return;
        for (int partner = 0; partner < simulation.State.Civilizations.Length; partner++)
        {
            if (partner == playerId || simulation.State.Civilization(partner).IsDefeated) continue;
            if (HasActiveRoute(simulation.State, playerId, partner, assemblyCityId) || player.InfluenceTenths < 10) continue;
            int routeCount = CountRoutes(simulation.State, playerId);
            int routeSlots = 1 + CountBuilding(simulation.State, playerId, "market") + (player.Knows("trade_league") ? 1 : 0);
            if (routeCount >= routeSlots) return;
            CommandResult result = ProposeDiplomaticTreaty(simulation, playerId, partner, TreatyKind.TradeRoute, assemblyCityId);
            if (result.Code == "route_blocked") continue;
            if (!result.Accepted && result.Code != "route_slots")
                throw new InvalidOperationException("diplomatic strategy could not propose a route to partner " + partner + ": " + result.Code);
            return;
        }

        if (CountRoutes(simulation.State, playerId) >= 2 || player.InfluenceTenths < 20) return;
        for (int partner = 0; partner < simulation.State.Civilizations.Length; partner++)
        {
            if (partner == playerId || simulation.State.Civilization(partner).IsDefeated ||
                HasOpenBorders(simulation.State, playerId, partner)) continue;
            CommandResult result = ProposeDiplomaticTreaty(simulation, playerId, partner, TreatyKind.OpenBorders, assemblyCityId);
            if (result.Accepted) return;
        }
    }

    private static bool HasOpenBorders(GameState state, int first, int second)
    {
        for (int i = 0; i < state.Treaties.Length; i++)
        {
            TreatyState treaty = state.Treaties[i];
            if ((treaty.Active || treaty.Pending) && treaty.Kind == TreatyKind.OpenBorders &&
                ((treaty.FromCivilizationId == first && treaty.ToCivilizationId == second) ||
                 (treaty.FromCivilizationId == second && treaty.ToCivilizationId == first))) return true;
        }
        return false;
    }

    private static CommandResult ProposeDiplomaticTreaty(GameSimulation simulation, int playerId, int partnerId, TreatyKind kind, int sourceCityId)
    {
        int partnerCapital = simulation.FindCapital(partnerId);
        if (partnerCapital < 0) return CommandResult.Reject("route_cities", "У партнёра нет столицы.");
        return simulation.Issue(new GameCommand
        {
            Kind = CommandKind.ProposeTreaty,
            ActorId = playerId,
            TargetId = partnerId,
            Value = (int)kind,
            EntityId = sourceCityId,
            Sequence = partnerCapital
        });
    }

    private static int CountRoutes(GameState state, int playerId)
    {
        int count = 0;
        for (int i = 0; i < state.Treaties.Length; i++)
            if (state.Treaties[i].Active && state.Treaties[i].Kind == TreatyKind.TradeRoute &&
                (state.Treaties[i].FromCivilizationId == playerId || state.Treaties[i].ToCivilizationId == playerId)) count++;
        return count;
    }

    private static int CountBuilding(GameState state, int playerId, string buildingId)
    {
        int count = 0;
        for (int i = 0; i < state.Cities.Length; i++)
            if (state.Cities[i].OwnerId == playerId && state.Cities[i].BuildingLevel(buildingId) > 0) count++;
        return count;
    }

    private static bool HasDurableAssemblyRoute(GameState state, int playerId, int partnerId, int assemblyCityId)
    {
        for (int i = 0; i < state.Treaties.Length; i++)
        {
            TreatyState treaty = state.Treaties[i];
            if (treaty.Active && treaty.Kind == TreatyKind.TradeRoute && treaty.FromCivilizationId == playerId &&
                treaty.ToCivilizationId == partnerId && treaty.RouteCityA == assemblyCityId && treaty.TurnsRemaining >= 3) return true;
        }
        return false;
    }

    private static bool HasTwoDurableAssemblyRoutes(GameState state, int playerId, int assemblyCityId)
    {
        int partners = 0;
        for (int partner = 0; partner < state.Civilizations.Length; partner++)
            if (partner != playerId && HasDurableAssemblyRoute(state, playerId, partner, assemblyCityId)) partners++;
        return partners >= 2;
    }

    private static bool HasActiveRoute(GameState state, int playerId, int partnerId, int assemblyCityId)
    {
        for (int i = 0; i < state.Treaties.Length; i++)
        {
            TreatyState treaty = state.Treaties[i];
            if (treaty.Active && treaty.Kind == TreatyKind.TradeRoute && treaty.FromCivilizationId == playerId &&
                treaty.ToCivilizationId == partnerId && treaty.RouteCityA == assemblyCityId) return true;
        }
        return false;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0) { count += value & 1; value >>= 1; }
        return count;
    }

    private static string ProjectSummary(GameState state, int playerId)
    {
        string result = "";
        for (int i = 0; i < state.Cities.Length; i++)
        {
            CityState city = state.Cities[i];
            if (city.OwnerId != playerId || city.Project == null || city.Project.Kind == ProjectKind.None) continue;
            if (result.Length > 0) result += ";";
            result += city.Name + ":" + city.Project.TargetId + " " + city.Project.Progress + "/" + city.Project.Cost;
        }
        return result.Length == 0 ? "none" : result;
    }

    private static string RouteSummary(GameState state, int playerId)
    {
        string result = "";
        for (int i = 0; i < state.Treaties.Length; i++)
        {
            TreatyState treaty = state.Treaties[i];
            if (!treaty.Active || treaty.Kind != TreatyKind.TradeRoute ||
                (treaty.FromCivilizationId != playerId && treaty.ToCivilizationId != playerId)) continue;
            if (result.Length > 0) result += ";";
            result += treaty.FromCivilizationId + "→" + treaty.ToCivilizationId + "(" + treaty.TurnsRemaining + ")";
        }
        return result.Length == 0 ? "none" : result;
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

