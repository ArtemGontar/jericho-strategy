using System;
using System.Collections.Generic;

namespace Nasledie.Core
{
    public enum Era { Communities, Cities, ConnectedLands, Industry, Synthesis }
    public enum TerrainKind { Plains, Meadow, Forest, Hills, Mountain, River, Coast, Arid, Swamp, Water }
    public enum ResourceKind { None, Iron, Herbs, Salt, FertileSoil }
    public enum WorkerRole { Farmer, Artisan, Scholar, Merchant, Artist, CivilServant }
    public enum UnitClass { Infantry, Ranged, Cavalry, Siege, Scout, Civilian }
    // Keep the first four values stable: saves created before the era roster was added store these ids.
    public enum UnitKind
    {
        Militia, FieldForce, SiegeTeam, SettlerWagon,
        CommunitiesRanged, CommunitiesCavalry, CommunitiesScout,
        CitiesInfantry, CitiesRanged, CitiesCavalry, CitiesSiege, CitiesScout,
        ConnectedInfantry, ConnectedRanged, ConnectedCavalry, ConnectedSiege, ConnectedScout,
        IndustryInfantry, IndustryRanged, IndustryCavalry, IndustrySiege, IndustryScout,
        SynthesisInfantry, SynthesisRanged, SynthesisCavalry, SynthesisSiege, SynthesisScout
    }
    public enum ImprovementKind { None, Farm, Forestry, Workshop, Road, Bridge, Market, Fishery, IrrigatedFarm, Park, Fortification }
    public enum ProjectKind { None, Building, Improvement, Recruitment, EraTransition, ScientificProject }
    public enum VictoryPath { None, Scientific, Cultural, QualityOfLife, Diplomatic, HeritageScore }
    public enum TreatyKind { TradeRoute, NonAggression, OpenBorders, KnowledgeExchange, Guarantee, Embargo, Peace, ResourceExport, HumanitarianAid, Mediation }
    public enum PeaceTermKind { StatusQuo, Reparations, Demobilization, BorderAdjustment, AnnexOccupiedCity, PrisonerExchange }
    public enum EventKind { Drought, Epidemic, RoadCongestion, RefugeeFlow, ProcurementCorruption, Smog, EducationReform, BorderDispute, TradeShortage, CulturalExchange, MobilizationFatigue, SharedResearch, SettlerInterception, NeutralMediation }
    public enum CrisisKind { None, FoodShortage, Epidemic, TradeCollapse }
    public enum CommandKind { SetWorkers, AutoAssignWorkers, SetProject, ChooseTechnology, BuildImprovement, FoundCity, MoveUnit, Recruit, DeclareWar, ProposeTreaty, AcceptTreaty, MakePeace, SetTax, SetSpending, EnactLaw, EventChoice, ContributeToCrisis, BeginVictoryProject, Repair, UpgradeUnit }
    public enum TaxRate { Low, Normal, High }
    public enum Difficulty { Calm, Standard, Strong, Trial }
    public enum GamePace { Relaxed, Standard, Quick }

    [Serializable]
    public sealed class HexCell
    {
        public int Q;
        public int R;
        public TerrainKind Terrain;
        public ResourceKind Resource;
        public int OwnerId = -1;
        public ImprovementKind Improvement;
        public int ImprovementLevel;
        public int DistrictCityId = -1;
        public string DistrictBuildingId = "";
        public int DistrictLevel;
        public bool HasRoad;
        public int Damage;
        public int VisibleToMask;
        public int ExploredByMask;
        public int LastSeenTurn;

        public HexCell Copy()
        {
            return (HexCell)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class CityProject
    {
        public ProjectKind Kind;
        public string TargetId = "";
        public int Progress;
        public int Cost;
        public int ReservedFoodTenths;
        public int ReservedTreasuryTenths;
        public int ReservedInfluenceTenths;
        public int ReservedPopulationTenths;
        public int TargetQ;
        public int TargetR;
        public bool DistrictPlotReserved;
        public int TurnsActive;

        public CityProject Copy() { return (CityProject)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class CityState
    {
        public int Id;
        public int OwnerId;
        public string Name = "";
        public int Q;
        public int R;
        public int PopulationTenths;
        public int ReservedPopulationTenths;
        public int FoodTenths;
        public int FoodStoreCapacityTenths;
        public int FoodReserveTurns = 2;
        public int GrowthTenths;
        public int HousingTenths;
        public int Health = 60;
        public int Safety = 75;
        public int Education = 25;
        public int Trust = 65;
        public int Environment = 100;
        public int Attraction = 50;
        public int EfficiencyBasisPoints = 10000;
        public int AdministrativeLoad = 1;
        public int FoodShortageTurns;
        public int UnemploymentTurns;
        public int SiegeTurns;
        public int WarDamageTurns;
        public int LastNegativeEventTurn = -99;
        public int WarStrain;
        public int OccupiedById = -1;
        public int IntegrationTurns;
        public int[] Workers = new int[6];
        public int[] ServiceFunding = new int[3];
        public CityProject Project = new CityProject();
        public string[] BuildingIds = Array.Empty<string>();
        public int[] BuildingLevels = Array.Empty<int>();
        public string[] CompletedCulturalIds = Array.Empty<string>();
        public int LocalCulturePrestige;
        public int LocalAppeal;

        public int WorkerCount
        {
            get { return Math.Max(0, PopulationTenths - ReservedPopulationTenths) / 10; }
        }

        public int BuildingLevel(string id)
        {
            for (int i = 0; i < BuildingIds.Length; i++)
                if (BuildingIds[i] == id) return i < BuildingLevels.Length ? BuildingLevels[i] : 0;
            return 0;
        }

        public void AddBuilding(string id)
        {
            int index = Array.IndexOf(BuildingIds, id);
            if (index >= 0)
            {
                if (index < BuildingLevels.Length) BuildingLevels[index]++;
                return;
            }
            Array.Resize(ref BuildingIds, BuildingIds.Length + 1);
            Array.Resize(ref BuildingLevels, BuildingLevels.Length + 1);
            BuildingIds[BuildingIds.Length - 1] = id;
            BuildingLevels[BuildingLevels.Length - 1] = 1;
        }

        public CityState Copy()
        {
            var copy = (CityState)MemberwiseClone();
            copy.Workers = (int[])Workers.Clone();
            copy.ServiceFunding = (int[])ServiceFunding.Clone();
            copy.Project = Project == null ? new CityProject() : Project.Copy();
            copy.BuildingIds = (string[])BuildingIds.Clone();
            copy.BuildingLevels = (int[])BuildingLevels.Clone();
            copy.CompletedCulturalIds = (string[])CompletedCulturalIds.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class UnitState
    {
        public int Id;
        public int OwnerId;
        public int CitySourceId;
        public UnitKind Kind;
        public int Q;
        public int R;
        public int Strength;
        public int PopulationTenths;
        public int InitialStrength;
        public int InitialPopulationTenths;
        public int WoundedPopulationTenths;
        public int WoundedStrength;
        public int Readiness = 100;
        public int SupplyBasisPoints = 10000;
        public int MovementLeft;
        public int RecruitmentTurns;
        public int ReservedPopulationTenths;
        public int FoodCargoTenths;
        public int TargetCellQ;
        public int TargetCellR;
        public bool IsWounded;
        public int WoundedTurns;
        public bool IronEquipped;

        public UnitState Copy() { return (UnitState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class CivilizationState
    {
        public int Id;
        public string Name = "";
        public string NationId = "";
        public string Archetype = "";
        public bool IsHuman;
        public bool IsDefeated;
        public Era Era;
        public TaxRate TaxRate = TaxRate.Normal;
        public Difficulty Difficulty = Difficulty.Standard;
        public int TreasuryTenths;
        public int KnowledgeTenths;
        public int InfluenceTenths;
        public int CulturePrestige;
        public int RefugeePopulationTenths;
        public int Reputation = 50;
        public int AdministrativeCapacity = 2;
        public int CompletedTechnologies;
        public int PreviousTurnKnowledgeOutputTenths;
        public int LastTurnKnowledgeOutputTenths;
        public int StrategyHoldTurns;
        public string StrategicGoal = "quality";
        public int StrategicGoalScore;
        public string StrategicRunnerUpGoal = string.Empty;
        public int StrategicRunnerUpScore;
        public string[] KnownTechnologies = Array.Empty<string>();
        public string[] Laws = Array.Empty<string>();
        public int[] TechnologyProgressTenths = new int[25];
        public int[] Relations = new int[4];
        public int[] Trusts = new int[4];
        public int[] Threats = new int[4];
        public int[] CulturalInfluence = new int[4];
        public int[] TradeDependency = new int[4];
        public int[] PrisonersHeldByCivilizationTenths = new int[4];
        public int ActiveResearchIndex;
        public int QualityVictoryTurns;
        public int ScienceProjectTurns;
        public int CultureFestivalTurns;
        public int DiplomaticCoalitionTurns;
        public int PoliticalCrisisCooldown;
        public int LawCooldownTurns;
        public VictoryPath WinningPath;
        public int WarStrain;
        public int WarsStarted;
        public int DebtTenths;
        public int ConsecutiveDeficitTurns;

        public bool Knows(string technologyId)
        {
            return Array.IndexOf(KnownTechnologies, technologyId) >= 0;
        }

        public void AddTechnology(string technologyId)
        {
            if (Knows(technologyId)) return;
            Array.Resize(ref KnownTechnologies, KnownTechnologies.Length + 1);
            KnownTechnologies[KnownTechnologies.Length - 1] = technologyId;
            CompletedTechnologies++;
        }

        public bool HasLaw(string lawId) { return Array.IndexOf(Laws, lawId) >= 0; }

        public CivilizationState Copy()
        {
            var copy = (CivilizationState)MemberwiseClone();
            copy.KnownTechnologies = (string[])KnownTechnologies.Clone();
            copy.Laws = (string[])Laws.Clone();
            copy.TechnologyProgressTenths = (int[])TechnologyProgressTenths.Clone();
            copy.Relations = (int[])Relations.Clone();
            copy.Trusts = (int[])Trusts.Clone();
            copy.Threats = (int[])Threats.Clone();
            copy.CulturalInfluence = (int[])CulturalInfluence.Clone();
            copy.TradeDependency = (int[])TradeDependency.Clone();
            copy.PrisonersHeldByCivilizationTenths = PrisonersHeldByCivilizationTenths == null
                ? Array.Empty<int>() : (int[])PrisonersHeldByCivilizationTenths.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class TreatyState
    {
        public int Id;
        public TreatyKind Kind;
        public int FromCivilizationId;
        public int ToCivilizationId;
        public int TurnsRemaining;
        public int RouteCityA;
        public int RouteCityB;
        public int FoodPerTurnTenths;
        public int TreasuryPerTurnTenths;
        public ResourceKind ExportedResource;
        public bool Active;
        public bool Pending;
        public bool Fulfilled;
        public bool Broken;

        public TreatyState Copy() { return (TreatyState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class ConflictState
    {
        public int Id;
        public int AggressorId;
        public int DefenderId;
        public int TargetQ;
        public int TargetR;
        public string LegalGoal = "";
        public int Phase; // 0 crisis, 1 mobilization, 2 active war, 3 truce, 4 recovery
        public int WarTurn;
        public int TruceTurns;
        public int RecoveryTurns;
        public int OccupationCityId = -1;
        public PeaceTermKind AgreedPeaceTerm;
        public int PeaceRequesterId = -1;
        public bool MediationOffered;
        public bool Active;

        public ConflictState Copy() { return (ConflictState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class ActiveEventState
    {
        public int Id;
        public EventKind Kind;
        public int TargetCityId = -1;
        public int TargetUnitId = -1;
        public int TargetCivilizationId = -1;
        public int OtherCivilizationId = -1;
        public int TurnsRemaining = 1;
        public int Severity;
        public bool IsResolved;
        public string[] Choices = Array.Empty<string>();

        public ActiveEventState Copy()
        {
            var copy = (ActiveEventState)MemberwiseClone();
            copy.Choices = (string[])Choices.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class CrisisState
    {
        public CrisisKind Kind;
        public int Threat;
        public int TurnsActive;
        public int ContributorsMask;
        public bool Resolved;
        public string PublicText = "";

        public CrisisState Copy() { return (CrisisState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class TurnReport
    {
        public int Turn;
        public string[] Entries = Array.Empty<string>();
        public int FoodDeltaTenths;
        public int TreasuryDeltaTenths;
        public int KnowledgeDeltaTenths;
        public int InfluenceDeltaTenths;
        public int CultureDelta;
        public int PopulationDeltaTenths;
        public int DeathsTenths;
        public int EmigrationTenths;
        public int BattleCount;
        public int TreatyCount;
    }

    /// <summary>Transient, presentation-only detail for a battle resolved by one command or turn.</summary>
    public sealed class BattleResolutionEvent
    {
        public int AttackerId;
        public int DefenderId;
        public int AttackerOwnerId;
        public int DefenderOwnerId;
        public UnitKind AttackerKind;
        public UnitKind DefenderKind;
        public int CellQ;
        public int CellR;
        public int AttackerLoss;
        public int DefenderLoss;
        public int AttackerDeathsTenths;
        public int DefenderDeathsTenths;
        public int AttackerWoundedTenths;
        public int DefenderWoundedTenths;
        public int AttackerPrisonersTenths;
        public int DefenderPrisonersTenths;
        public int RetreatQ;
        public int RetreatR;
        public int HumanVisibilityMask;
        public bool AttackerWon;
        public bool DefenderRetreated;
        public bool DefenderDestroyed;
        public bool PlayerFacing;
    }

    [Serializable]
    public sealed class GameState
    {
        public int SaveVersion = 11;
        public int RulesVersion = 2;
        public string RulesetHash = string.Empty;
        public int Seed;
        public int Turn = 1;
        public int MaximumTurn = 80;
        public GamePace Pace = GamePace.Standard;
        public int NextEntityId = 1;
        public int HumanCivilizationId;
        public bool IsFinished;
        public bool HasResolvedGlobalCrisis;
        public int ResolvedGlobalCrisisContributorMask;
        public VictoryPath WinningPath;
        public int WinnerCivilizationId = -1;
        public int[] WinningCivilizationIds = Array.Empty<int>();
        public int SelectedCityId = -1;
        public int SelectedUnitId = -1;
        public int[] SelectedMapOffset = new int[2];
        public HexCell[] Cells = Array.Empty<HexCell>();
        public CivilizationState[] Civilizations = Array.Empty<CivilizationState>();
        public CityState[] Cities = Array.Empty<CityState>();
        public UnitState[] Units = Array.Empty<UnitState>();
        public TreatyState[] Treaties = Array.Empty<TreatyState>();
        public ConflictState[] Conflicts = Array.Empty<ConflictState>();
        public ActiveEventState[] Events = Array.Empty<ActiveEventState>();
        public CrisisState Crisis = new CrisisState();
        public TurnReport[] Reports = Array.Empty<TurnReport>();
        public string[] EventLog = Array.Empty<string>();
        public string[] CommandJournal = Array.Empty<string>();
        public bool CommandJournalTruncated;
        public string[] TurnStateHashes = Array.Empty<string>();
        public int LastPlayerBalanceTenths;
        public int BattlesThisTurn;
        public int TreatiesThisTurn;

        public CivilizationState Civilization(int id)
        {
            for (int i = 0; i < Civilizations.Length; i++) if (Civilizations[i].Id == id) return Civilizations[i];
            return null;
        }

        public CityState City(int id)
        {
            for (int i = 0; i < Cities.Length; i++) if (Cities[i].Id == id) return Cities[i];
            return null;
        }

        public HexCell Cell(int q, int r)
        {
            for (int i = 0; i < Cells.Length; i++) if (Cells[i].Q == q && Cells[i].R == r) return Cells[i];
            return null;
        }

        public GameState Copy()
        {
            var copy = (GameState)MemberwiseClone();
            copy.SelectedMapOffset = (int[])SelectedMapOffset.Clone();
            copy.WinningCivilizationIds = WinningCivilizationIds == null ? Array.Empty<int>() : (int[])WinningCivilizationIds.Clone();
            copy.Cells = new HexCell[Cells.Length];
            for (int i = 0; i < Cells.Length; i++) copy.Cells[i] = Cells[i].Copy();
            copy.Civilizations = new CivilizationState[Civilizations.Length];
            for (int i = 0; i < Civilizations.Length; i++) copy.Civilizations[i] = Civilizations[i].Copy();
            copy.Cities = new CityState[Cities.Length];
            for (int i = 0; i < Cities.Length; i++) copy.Cities[i] = Cities[i].Copy();
            copy.Units = new UnitState[Units.Length];
            for (int i = 0; i < Units.Length; i++) copy.Units[i] = Units[i].Copy();
            copy.Treaties = new TreatyState[Treaties.Length];
            for (int i = 0; i < Treaties.Length; i++) copy.Treaties[i] = Treaties[i].Copy();
            copy.Conflicts = new ConflictState[Conflicts.Length];
            for (int i = 0; i < Conflicts.Length; i++) copy.Conflicts[i] = Conflicts[i].Copy();
            copy.Events = new ActiveEventState[Events.Length];
            for (int i = 0; i < Events.Length; i++) copy.Events[i] = Events[i].Copy();
            copy.Crisis = Crisis == null ? new CrisisState() : Crisis.Copy();
            copy.Reports = (TurnReport[])Reports.Clone();
            copy.EventLog = (string[])EventLog.Clone();
            copy.CommandJournal = CommandJournal == null ? Array.Empty<string>() : (string[])CommandJournal.Clone();
            copy.TurnStateHashes = TurnStateHashes == null ? Array.Empty<string>() : (string[])TurnStateHashes.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class GameCommand
    {
        public CommandKind Kind;
        public int ActorId;
        public int EntityId;
        public int TargetId;
        public int Q;
        public int R;
        public int Value;
        public string Text = "";
        public string SecondaryText = "";
        public int Sequence;
    }

    public sealed class CommandResult
    {
        public bool Accepted;
        public string Code;
        public string Message;
        public static CommandResult Ok(string message = "") { return new CommandResult { Accepted = true, Code = "ok", Message = message }; }
        public static CommandResult Reject(string code, string message) { return new CommandResult { Accepted = false, Code = code, Message = message }; }
    }

    public sealed class GameActionResult
    {
        public bool Success;
        public string Code;
        public string Message;
        public static GameActionResult Ok(string message = "") { return new GameActionResult { Success = true, Code = "ok", Message = message }; }
        public static GameActionResult Fail(string code, string message) { return new GameActionResult { Success = false, Code = code, Message = message }; }
    }
}
