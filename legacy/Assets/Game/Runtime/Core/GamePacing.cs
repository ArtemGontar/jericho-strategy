namespace Nasledie.Core
{
    /// <summary>
    /// Campaign length and world-crisis cadence. The economy still resolves one turn at a time;
    /// pace changes how many turns a player has and how often the shared crisis cycle appears.
    /// </summary>
    public static class GamePacing
    {
        public static int MaximumTurn(GamePace pace)
        {
            switch (pace)
            {
                case GamePace.Relaxed: return 120;
                case GamePace.Quick: return 60;
                default: return 80;
            }
        }

        public static int FirstGlobalCrisisTurn(GamePace pace)
        {
            switch (pace)
            {
                case GamePace.Relaxed: return 24;
                case GamePace.Quick: return 12;
                default: return 16;
            }
        }

        public static int GlobalCrisisInterval(GamePace pace)
        {
            switch (pace)
            {
                case GamePace.Relaxed: return 18;
                case GamePace.Quick: return 9;
                default: return 12;
            }
        }

        public static bool IsGlobalCrisisTurn(GamePace pace, int turn)
        {
            int first = FirstGlobalCrisisTurn(pace);
            int interval = GlobalCrisisInterval(pace);
            return turn >= first && (turn - first) % interval == 0;
        }
    }
}
