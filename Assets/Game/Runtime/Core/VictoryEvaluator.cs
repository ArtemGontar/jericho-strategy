using System;
using System.Collections.Generic;

namespace Nasledie.Core
{
    public static class VictoryEvaluator
    {
        public static void Check(GameState state, List<string> lines)
        {
            if (state == null || state.IsFinished) return;
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                if (civ.IsDefeated) continue;

                if (QualityReady(state, civ)) civ.QualityVictoryTurns++;
                else civ.QualityVictoryTurns = 0;
                if (civ.QualityVictoryTurns >= 5)
                {
                    Finish(state, civ.Id, VictoryPath.QualityOfLife, lines, "пять ходов поддерживает высокий уровень жизни во всех крупных городах");
                    return;
                }

                if (civ.CultureFestivalTurns > 0)
                {
                    if (CulturalReady(state, civ))
                    {
                        Finish(state, civ.Id, VictoryPath.Cultural, lines, "провела культурный фестиваль, объединивший города и соседей");
                        return;
                    }
                    civ.CultureFestivalTurns--;
                    if (civ.CultureFestivalTurns == 0) lines.Add(civ.Name + ": культурная программа завершилась без достаточной поддержки. Её можно подготовить снова.");
                }

                if (civ.ScienceProjectTurns >= 4)
                {
                    Finish(state, civ.Id, VictoryPath.Scientific, lines, "завершила открытую научную программу для всех цивилизаций");
                    return;
                }

                if (civ.DiplomaticCoalitionTurns > 0)
                {
                    if (DiplomaticReady(state, civ))
                    {
                        Finish(state, civ.Id, VictoryPath.Diplomatic, lines, "собрала коалицию сотрудничества и получила поддержку мира");
                        return;
                    }
                    civ.DiplomaticCoalitionTurns--;
                    if (civ.DiplomaticCoalitionTurns == 0) lines.Add(civ.Name + ": дипломатическая коалиция распалась до голосования.");
                }
            }

            if (state.Turn >= state.MaximumTurn)
            {
                int winner = -1;
                int best = int.MinValue;
                for (int i = 0; i < state.Civilizations.Length; i++)
                {
                    CivilizationState civ = state.Civilizations[i];
                    if (civ.IsDefeated) continue;
                    int score = HeritageScore(state, civ.Id);
                    if (score > best) { best = score; winner = civ.Id; }
                }
                if (winner >= 0) Finish(state, winner, VictoryPath.HeritageScore, lines, "закончила кампанию с наибольшим наследием развития");
            }
        }

        public static int HeritageScore(GameState state, int civilizationId)
        {
            CivilizationState civ = state.Civilization(civilizationId);
            if (civ == null) return 0;
            int score = civ.CompletedTechnologies * 12 + Math.Min(150, civ.CulturePrestige) + civ.Reputation * 2;
            score += CivilizationPopulation(state, civilizationId) / 5;
            int cityCount = 0;
            int qualityTotal = 0;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civilizationId) continue;
                cityCount++;
                score += city.LocalCulturePrestige / 2 + city.BuildingIds.Length * 3;
                score += city.BuildingLevel("cultural_house") * 10 + city.BuildingLevel("archive") * 15 + city.BuildingLevel("university") * 12;
                int quality = city.Health + city.Safety + city.Education + city.Trust + city.Environment;
                qualityTotal += quality / 5;
                score += quality / 5;
                if (city.FoodShortageTurns > 0) score -= 12;
                if (city.OccupiedById >= 0) score -= 20;
            }
            score += cityCount * 20 + civ.InfluenceTenths / 10 + CountTradeRoutes(state, civilizationId) * 12;
            score += civ.ScienceProjectTurns * 8 + civ.DiplomaticCoalitionTurns * 4;
            score -= civ.WarStrain / 2 + civ.DebtTenths / 15 + civ.ConsecutiveDeficitTurns * 8;
            if (IsAtWar(state, civilizationId)) score -= 50;
            return Math.Max(0, score);
        }

        public static string PathName(VictoryPath path)
        {
            switch (path)
            {
                case VictoryPath.Scientific: return "Научный прорыв";
                case VictoryPath.Cultural: return "Культурное наследие";
                case VictoryPath.QualityOfLife: return "Благополучие";
                case VictoryPath.Diplomatic: return "Международное сотрудничество";
                case VictoryPath.HeritageScore: return "Наследие эпох";
                default: return "Путь не выбран";
            }
        }

        private static bool QualityReady(GameState state, CivilizationState civ)
        {
            int cities = 0;
            int health = 0, safety = 0, education = 0, trust = 0, environment = 0;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civ.Id) continue;
                cities++;
                health += city.Health;
                safety += city.Safety;
                education += city.Education;
                trust += city.Trust;
                environment += city.Environment;
                if (city.FoodShortageTurns > 0 || city.OccupiedById >= 0 || city.PopulationTenths < 40) return false;
            }
            if (cities < 3 || IsAtWar(state, civ.Id) || civ.ConsecutiveDeficitTurns > 0) return false;
            return health / cities >= 72 && safety / cities >= 70 && education / cities >= 55 && trust / cities >= 70 && environment / cities >= 75;
        }

        private static bool CulturalReady(GameState state, CivilizationState civ)
        {
            if (civ.CulturePrestige < 100 || civ.Reputation < 65) return false;
            int citiesWithObjects = 0;
            int distinctObjects = 0;
            var seen = new HashSet<string>();
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civ.Id) continue;
                if (city.CompletedCulturalIds.Length > 0) citiesWithObjects++;
                for (int c = 0; c < city.CompletedCulturalIds.Length; c++)
                    if (seen.Add(city.CompletedCulturalIds[c])) distinctObjects++;
            }
            if (citiesWithObjects < 2 || distinctObjects < 3) return false;
            int culturalSupport = 0;
            for (int i = 0; i < civ.CulturalInfluence.Length; i++) if (i != civ.Id && civ.CulturalInfluence[i] >= 25) culturalSupport++;
            return culturalSupport >= 2;
        }

        private static bool DiplomaticReady(GameState state, CivilizationState civ)
        {
            if (!state.HasResolvedGlobalCrisis || civ.Reputation < 70 || CountTradeRoutes(state, civ.Id) < 2 || IsAtWar(state, civ.Id)) return false;
            int partners = 0;
            for (int i = 0; i < civ.Relations.Length; i++)
                if (i != civ.Id && civ.Relations[i] >= 25 && civ.Trusts[i] >= 50) partners++;
            return partners >= 2;
        }

        private static int CivilizationPopulation(GameState state, int civilizationId)
        {
            int total = 0;
            for (int i = 0; i < state.Cities.Length; i++) if (state.Cities[i].OwnerId == civilizationId) total += state.Cities[i].PopulationTenths;
            for (int i = 0; i < state.Units.Length; i++) if (state.Units[i].OwnerId == civilizationId) total += state.Units[i].PopulationTenths;
            CivilizationState civ = state.Civilization(civilizationId);
            if (civ != null) total += civ.RefugeePopulationTenths;
            return total;
        }

        private static int CountTradeRoutes(GameState state, int civilizationId)
        {
            int count = 0;
            for (int i = 0; i < state.Treaties.Length; i++)
                if (state.Treaties[i].Active && state.Treaties[i].Kind == TreatyKind.TradeRoute &&
                    (state.Treaties[i].FromCivilizationId == civilizationId || state.Treaties[i].ToCivilizationId == civilizationId)) count++;
            return count;
        }

        private static bool IsAtWar(GameState state, int civilizationId)
        {
            for (int i = 0; i < state.Conflicts.Length; i++)
                if (state.Conflicts[i].Active && state.Conflicts[i].Phase == 2 &&
                    (state.Conflicts[i].AggressorId == civilizationId || state.Conflicts[i].DefenderId == civilizationId)) return true;
            return false;
        }

        private static void Finish(GameState state, int winnerId, VictoryPath path, List<string> lines, string reason)
        {
            CivilizationState winner = state.Civilization(winnerId);
            state.IsFinished = true;
            state.WinnerCivilizationId = winnerId;
            state.WinningPath = path;
            if (winner != null) winner.WinningPath = path;
            lines.Add((winner == null ? "Цивилизация" : winner.Name) + " победила по пути «" + PathName(path) + ": " + reason + ".");
        }
    }
}
