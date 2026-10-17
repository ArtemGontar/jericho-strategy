using System;
using System.Collections.Generic;

namespace Nasledie.Core
{
    [Serializable]
    public sealed class HeritageScoreBreakdown
    {
        public int QualityOfLife;
        public int EducationAndTechnology;
        public int CulturalContribution;
        public int Sustainability;
        public int TrustAndAgreements;
        public int Total;
    }

    public static class VictoryEvaluator
    {
        private sealed class WinnerCandidate
        {
            public int CivilizationId;
            public VictoryPath Path;
            public string Reason;
        }

        public static void Check(GameState state, List<string> lines)
        {
            if (state == null || state.IsFinished) return;
            var candidates = new List<WinnerCandidate>();
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState civ = state.Civilizations[i];
                if (civ == null || civ.IsDefeated) continue;

                if (QualityReady(state, civ)) civ.QualityVictoryTurns++;
                else civ.QualityVictoryTurns = 0;
                if (civ.QualityVictoryTurns >= 5)
                {
                    candidates.Add(new WinnerCandidate { CivilizationId = civ.Id, Path = VictoryPath.QualityOfLife, Reason = "пять ходов поддерживает высокий уровень жизни" });
                    continue;
                }

                if (civ.CultureFestivalTurns > 0)
                {
                    if (!CulturalReady(state, civ))
                    {
                        civ.CultureFestivalTurns = 0;
                        lines.Add(civ.Name + ": фестиваль обмена остановлен — условия культурной поддержки изменились.");
                    }
                    else
                    {
                        civ.CultureFestivalTurns--;
                        if (civ.CultureFestivalTurns == 0)
                        {
                            candidates.Add(new WinnerCandidate { CivilizationId = civ.Id, Path = VictoryPath.Cultural, Reason = "завершила двухходовый фестиваль общего наследия" });
                            continue;
                        }
                    }
                }

                if (ScienceReady(state, civ))
                {
                    candidates.Add(new WinnerCandidate { CivilizationId = civ.Id, Path = VictoryPath.Scientific, Reason = "завершила международную научную программу" });
                    continue;
                }

                if (civ.DiplomaticCoalitionTurns > 0)
                {
                    if (!DiplomaticReady(state, civ))
                    {
                        civ.DiplomaticCoalitionTurns = 0;
                        lines.Add(civ.Name + ": международная коалиция распалась — не выполнены условия голосования или сотрудничества.");
                    }
                    else
                    {
                        civ.DiplomaticCoalitionTurns--;
                        if (civ.DiplomaticCoalitionTurns == 0)
                            candidates.Add(new WinnerCandidate { CivilizationId = civ.Id, Path = VictoryPath.Diplomatic, Reason = "получила поддержку коалиции на двух последовательных ходах" });
                    }
                }
            }

            if (candidates.Count > 0)
            {
                FinishBestCandidates(state, candidates, lines);
                return;
            }

            if (state.Turn >= state.MaximumTurn)
            {
                var finalCandidates = new List<WinnerCandidate>();
                int best = int.MinValue;
                for (int i = 0; i < state.Civilizations.Length; i++)
                {
                    CivilizationState civ = state.Civilizations[i];
                    if (civ == null || civ.IsDefeated) continue;
                    int score = HeritageScore(state, civ.Id);
                    if (score > best) { best = score; finalCandidates.Clear(); }
                    if (score == best) finalCandidates.Add(new WinnerCandidate { CivilizationId = civ.Id, Path = VictoryPath.HeritageScore, Reason = "закончила кампанию с наибольшим наследием развития" });
                }
                if (finalCandidates.Count > 0) FinishBestCandidates(state, finalCandidates, lines);
            }
        }

        public static int CityQualityIndex(GameState state, CityState city)
        {
            if (state == null || city == null || city.PopulationTenths <= 0) return 0;
            CivilizationState owner = state.Civilization(city.OwnerId);
            if (owner == null) return 0;
            CityYield yield = EconomyCalculator.Calculate(state, city);

            int consumption = Math.Max(1, city.PopulationTenths);
            int foodScore;
            if (city.FoodShortageTurns > 0) foodScore = 0;
            else if (yield.FoodTenths - consumption >= 0 && city.FoodTenths >= consumption) foodScore = 100;
            else if (city.FoodTenths >= consumption) foodScore = 50;
            else foodScore = Math.Min(50, city.FoodTenths * 50 / consumption);

            int housingScore = Math.Min(100, city.HousingTenths * 100 / Math.Max(1, city.PopulationTenths));
            int workers = city.WorkerCount;
            int assigned = 0;
            if (city.Workers != null) for (int i = 0; i < city.Workers.Length; i++) assigned += Math.Max(0, city.Workers[i]);
            int employmentScore = workers <= 0 ? 100 : Math.Min(100, assigned * 100 / workers);
            if (owner.ConsecutiveDeficitTurns > 0) employmentScore = Math.Max(0, employmentScore - 10);

            int equalityScore = ServiceEqualityScore(state, owner.Id);
            int weighted = foodScore * 20 + housingScore * 15 + city.Health * 15 + employmentScore * 15 +
                city.Safety * 15 + city.Education * 10 + city.Environment * 5 + equalityScore * 5;
            return Clamp(weighted / 100, 0, 100);
        }

        public static HeritageScoreBreakdown HeritageBreakdown(GameState state, int civilizationId)
        {
            var result = new HeritageScoreBreakdown();
            CivilizationState civ = state == null ? null : state.Civilization(civilizationId);
            if (civ == null) return result;

            var cities = OwnedCities(state, civilizationId);
            int totalPopulation = 0;
            long weightedQuality = 0;
            int education = 0;
            int foodStable = 0;
            int distinctObjects = 0;
            var objects = new HashSet<string>();
            for (int i = 0; i < cities.Count; i++)
            {
                CityState city = cities[i];
                int population = Math.Max(0, city.PopulationTenths);
                totalPopulation += population;
                weightedQuality += (long)CityQualityIndex(state, city) * population;
                education += Clamp(city.Education, 0, 100);
                if (city.FoodShortageTurns == 0) foodStable++;
                if (city.CompletedCulturalIds != null)
                    for (int j = 0; j < city.CompletedCulturalIds.Length; j++)
                        if (objects.Add(city.CompletedCulturalIds[j])) distinctObjects++;
            }

            int averageQuality = totalPopulation > 0 ? (int)(weightedQuality / totalPopulation) : cities.Count == 0 ? 0 : AverageCityQuality(state, cities);
            result.QualityOfLife = averageQuality * Math.Min(4, cities.Count) / 4;

            int technologyProgressTotal = 0;
            int eraTechnologyCount = 0;
            for (int i = 0; i < RulesCatalog.Technologies.Length; i++)
            {
                TechnologyDefinition tech = RulesCatalog.Technologies[i];
                if (tech.Era != civ.Era) continue;
                eraTechnologyCount++;
                if (civ.Knows(tech.Id)) technologyProgressTotal += 100;
                else if (civ.ActiveResearchIndex == i && i < civ.TechnologyProgressTenths.Length)
                {
                    int costTenths = tech.Cost * 10 * (tech.KeyTechnology ? 125 : 100) / 100;
                    if (costTenths > 0) technologyProgressTotal += Clamp(civ.TechnologyProgressTenths[i] * 100 / costTenths, 0, 100);
                }
            }
            int technologyProgress = eraTechnologyCount == 0 ? 0 : Clamp(technologyProgressTotal / eraTechnologyCount, 0, 100);
            int averageEducation = cities.Count == 0 ? 0 : education / cities.Count;
            result.EducationAndTechnology = (technologyProgress * 60 + averageEducation * 40) / 100;

            int prestigeScore = Clamp(civ.CulturePrestige, 0, 100);
            int objectScore = Clamp(distinctObjects * 20, 0, 100);
            result.CulturalContribution = (prestigeScore * 70 + objectScore * 30) / 100;

            int stableFoodScore = cities.Count == 0 ? 0 : foodStable * 100 / cities.Count;
            int creditLimit = Math.Max(1, (20 + cities.Count * 5) * 10);
            int debt = Math.Max(Math.Max(0, civ.DebtTenths), Math.Max(0, -civ.TreasuryTenths));
            int treasuryScore = Clamp(100 - debt * 100 / creditLimit, 0, 100);
            int tradeScore = DistinctTradePartners(state, civilizationId) * 100 / 3;
            result.Sustainability = (stableFoodScore * 50 + treasuryScore * 25 + Clamp(tradeScore, 0, 100) * 25) / 100;

            int trustTotal = 0;
            int rivalCount = 0;
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState rival = state.Civilizations[i];
                if (rival == null || rival.Id == civilizationId) continue;
                int trust = civ.Trusts != null && rival.Id >= 0 && rival.Id < civ.Trusts.Length ? civ.Trusts[rival.Id] : 0;
                trustTotal += Clamp(trust, 0, 100);
                rivalCount++;
            }
            int averageTrust = rivalCount == 0 ? 0 : trustTotal / rivalCount;
            int agreementScore = TreatyFulfillmentScore(state, civilizationId);
            result.TrustAndAgreements = (averageTrust * 50 + Clamp(civ.Reputation, 0, 100) * 30 + agreementScore * 20) / 100;

            result.Total = Clamp((result.QualityOfLife * 25 + result.EducationAndTechnology * 20 +
                result.CulturalContribution * 20 + result.Sustainability * 20 + result.TrustAndAgreements * 15) / 100, 0, 100);
            return result;
        }

        public static int HeritageScore(GameState state, int civilizationId)
        {
            return HeritageBreakdown(state, civilizationId).Total;
        }

        public static bool HasCulturalVictoryStructures(GameState state, int civilizationId)
        {
            CityState culturalHouse = null;
            CityState archive = null;
            CityState exchangeHouse = null;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civilizationId) continue;
                if (city.BuildingLevel("cultural_house") > 0) culturalHouse = city;
                if (city.BuildingLevel("archive") > 0) archive = city;
                if (city.BuildingLevel("exchange_house") > 0) exchangeHouse = city;
            }
            return culturalHouse != null && archive != null && exchangeHouse != null &&
                culturalHouse.Id != archive.Id && culturalHouse.Id != exchangeHouse.Id && archive.Id != exchangeHouse.Id;
        }

        public static bool HasDiplomaticAssemblyRoutes(GameState state, int civilizationId, int cityId)
        {
            CityState assemblyCity = state == null ? null : state.City(cityId);
            if (assemblyCity == null || assemblyCity.OwnerId != civilizationId || assemblyCity.BuildingLevel("assembly") <= 0) return false;
            var partners = new HashSet<int>();
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || treaty.Kind != TreatyKind.TradeRoute) continue;
                if (treaty.RouteCityA == cityId && treaty.FromCivilizationId == civilizationId) partners.Add(treaty.ToCivilizationId);
                if (treaty.RouteCityB == cityId && treaty.ToCivilizationId == civilizationId) partners.Add(treaty.FromCivilizationId);
            }
            return partners.Count >= 2;
        }

        public static bool CanStartCulturalFestival(GameState state, int civilizationId)
        {
            CivilizationState civ = state == null ? null : state.Civilization(civilizationId);
            return civ != null && civ.CulturePrestige >= 100 && HasCulturalVictoryStructures(state, civilizationId) && CulturalInfluenceReady(civ);
        }

        public static bool CanStartScientificProgram(GameState state, int civilizationId, int cityId)
        {
            CivilizationState civ = state == null ? null : state.Civilization(civilizationId);
            CityState projectCity = state == null ? null : state.City(cityId);
            if (civ == null || projectCity == null || projectCity.OwnerId != civilizationId ||
                (projectCity.Project != null && projectCity.Project.Kind != ProjectKind.None)) return false;
            if (!civ.Knows("computing_networks") || !civ.Knows("open_science")) return false;
            int universityCities = 0;
            bool academy = false;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civilizationId) continue;
                if (city.Project != null && city.Project.Kind == ProjectKind.ScientificProject) return false;
                if (city.BuildingLevel("university") > 0) universityCities++;
                if (city.BuildingLevel("academy") > 0) academy = true;
            }
            return universityCities >= 2 && academy;
        }

        public static bool CanStartDiplomaticCoalition(GameState state, int civilizationId, int cityId)
        {
            CivilizationState civ = state == null ? null : state.Civilization(civilizationId);
            return civ != null && civ.Knows("international_assembly") && HasDiplomaticAssemblyRoutes(state, civilizationId, cityId) && DiplomaticSupportReady(state, civ);
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
            var cities = OwnedCities(state, civ.Id);
            if (cities.Count < 4 || civ.ConsecutiveDeficitTurns > 2) return false;
            int totalPopulation = 0;
            int qualifiedPopulation = 0;
            int lowest = 101;
            int highest = -1;
            for (int i = 0; i < cities.Count; i++)
            {
                CityState city = cities[i];
                if (city.Health < 60 || city.Safety < 60) return false;
                int index = CityQualityIndex(state, city);
                lowest = Math.Min(lowest, index);
                highest = Math.Max(highest, index);
                totalPopulation += Math.Max(0, city.PopulationTenths);
                if (index >= 75) qualifiedPopulation += Math.Max(0, city.PopulationTenths);
            }
            return totalPopulation > 0 && qualifiedPopulation * 100L >= totalPopulation * 80L && highest - lowest <= 20;
        }

        private static bool ScienceReady(GameState state, CivilizationState civ)
        {
            if (!civ.Knows("computing_networks") || !civ.Knows("open_science") || civ.ScienceProjectTurns < 4) return false;
            if (civ.LastTurnKnowledgeOutputTenths < 50 || civ.PreviousTurnKnowledgeOutputTenths < 50) return false;
            int universityCities = 0;
            bool hasAcademy = false;
            bool activeProject = false;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civ.Id) continue;
                if (city.BuildingLevel("university") > 0) universityCities++;
                if (city.BuildingLevel("academy") > 0) hasAcademy = true;
                if (city.Project != null && city.Project.Kind == ProjectKind.ScientificProject) activeProject = true;
            }
            return universityCities >= 2 && hasAcademy && activeProject;
        }

        private static bool CulturalReady(GameState state, CivilizationState civ)
        {
            return civ.CulturePrestige >= 100 && HasCulturalVictoryStructures(state, civ.Id) && CulturalInfluenceReady(civ);
        }

        private static bool CulturalInfluenceReady(CivilizationState civ)
        {
            int partners70 = 0;
            int partners40 = 0;
            for (int i = 0; i < civ.CulturalInfluence.Length; i++)
            {
                if (i == civ.Id) continue;
                if (civ.CulturalInfluence[i] >= 70) partners70++;
                if (civ.CulturalInfluence[i] >= 40) partners40++;
            }
            return partners70 >= 2 && partners40 >= 3;
        }

        private static bool DiplomaticReady(GameState state, CivilizationState civ)
        {
            return civ.DiplomaticCoalitionTurns > 0 && DiplomaticSupportReady(state, civ) &&
                AssemblyHasTwoPartners(state, civ.Id);
        }

        private static bool DiplomaticSupportReady(GameState state, CivilizationState civ)
        {
            if (state.Crisis == null || !state.Crisis.Resolved || !state.HasResolvedGlobalCrisis) return false;
            int participantCount = CountBits(state.ResolvedGlobalCrisisContributorMask);
            if (participantCount < 3 || (state.ResolvedGlobalCrisisContributorMask & (1 << civ.Id)) == 0) return false;
            int votes = 1;
            for (int i = 0; i < state.Civilizations.Length; i++)
            {
                CivilizationState partner = state.Civilizations[i];
                if (partner == null || partner.IsDefeated || partner.Id == civ.Id) continue;
                int relation = civ.Relations != null && partner.Id < civ.Relations.Length ? civ.Relations[partner.Id] : -100;
                int trust = civ.Trusts != null && partner.Id < civ.Trusts.Length ? civ.Trusts[partner.Id] : 0;
                if (relation >= 25 && trust >= 50) votes++;
            }
            return votes >= 3;
        }

        private static bool AssemblyHasTwoPartners(GameState state, int civilizationId)
        {
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i].OwnerId == civilizationId && state.Cities[i].BuildingLevel("assembly") > 0 &&
                    HasDiplomaticAssemblyRoutes(state, civilizationId, state.Cities[i].Id)) return true;
            return false;
        }

        private static int ServiceEqualityScore(GameState state, int civilizationId)
        {
            int min = 101;
            int max = -1;
            int count = 0;
            for (int i = 0; i < state.Cities.Length; i++)
            {
                CityState city = state.Cities[i];
                if (city.OwnerId != civilizationId) continue;
                int coverage = Clamp(EconomyCalculator.Calculate(state, city).ServiceCoverage, 0, 100);
                min = Math.Min(min, coverage);
                max = Math.Max(max, coverage);
                count++;
            }
            return count <= 1 ? 100 : 100 - Math.Max(0, max - min);
        }

        private static int TreatyFulfillmentScore(GameState state, int civilizationId)
        {
            int fulfilled = 0;
            int broken = 0;
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (treaty.FromCivilizationId != civilizationId && treaty.ToCivilizationId != civilizationId) continue;
                if (treaty.Fulfilled) fulfilled++;
                else if (treaty.Broken) broken++;
            }
            int resolved = fulfilled + broken;
            return resolved == 0 ? 50 : fulfilled * 100 / resolved;
        }

        private static int DistinctTradePartners(GameState state, int civilizationId)
        {
            var partners = new HashSet<int>();
            for (int i = 0; i < state.Treaties.Length; i++)
            {
                TreatyState treaty = state.Treaties[i];
                if (!treaty.Active || treaty.Kind != TreatyKind.TradeRoute) continue;
                if (treaty.FromCivilizationId == civilizationId) partners.Add(treaty.ToCivilizationId);
                else if (treaty.ToCivilizationId == civilizationId) partners.Add(treaty.FromCivilizationId);
            }
            return partners.Count;
        }

        private static int AverageCityQuality(GameState state, List<CityState> cities)
        {
            int total = 0;
            for (int i = 0; i < cities.Count; i++) total += CityQualityIndex(state, cities[i]);
            return cities.Count == 0 ? 0 : total / cities.Count;
        }

        private static List<CityState> OwnedCities(GameState state, int civilizationId)
        {
            var result = new List<CityState>();
            if (state.Cities == null) return result;
            for (int i = 0; i < state.Cities.Length; i++)
                if (state.Cities[i] != null && state.Cities[i].OwnerId == civilizationId) result.Add(state.Cities[i]);
            return result;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0) { count += value & 1; value >>= 1; }
            return count;
        }

        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }

        private static void FinishBestCandidates(GameState state, List<WinnerCandidate> candidates, List<string> lines)
        {
            int best = int.MinValue;
            var winners = new List<WinnerCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                int score = HeritageScore(state, candidates[i].CivilizationId);
                if (score > best) { best = score; winners.Clear(); }
                if (score == best) winners.Add(candidates[i]);
            }
            if (winners.Count == 0) return;
            WinnerCandidate primary = winners[0];
            state.IsFinished = true;
            state.WinnerCivilizationId = primary.CivilizationId;
            state.WinningPath = primary.Path;
            state.WinningCivilizationIds = new int[winners.Count];
            var winnerNames = new List<string>();
            for (int i = 0; i < winners.Count; i++)
            {
                WinnerCandidate candidate = winners[i];
                state.WinningCivilizationIds[i] = candidate.CivilizationId;
                CivilizationState civ = state.Civilization(candidate.CivilizationId);
                if (civ != null)
                {
                    civ.WinningPath = candidate.Path;
                    winnerNames.Add(civ.Name);
                }
            }
            if (winners.Count == 1)
                lines.Add(winnerNames[0] + " победила по пути «" + PathName(primary.Path) + ": " + primary.Reason + ".");
            else
                lines.Add("Совместная победа по пути «" + PathName(primary.Path) + "»: " + string.Join(", ", winnerNames.ToArray()) + " разделили первое место по счёту наследия.");
        }
    }
}
