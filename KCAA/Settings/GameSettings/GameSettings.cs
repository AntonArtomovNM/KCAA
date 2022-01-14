namespace KCAA.Settings.GameSettings
{
    public class GameSettings
    {
        public static string ConfigKey => "GameSettings";

        public string GameApiUrl { get; set; }

        public string QuarterSettingsPath { get; set; }

        public string CharacterSettingsPath { get; set; }

        public int MaxPlayersAmount { get; set; }

        public int MinPlayersAmount { get; set; }

        public int StartingCoinsAmount { get; set; }

        public int StartingQuertersAmount { get; set; }

        public int CoinsPerTurn { get; set; }

        public int QuertersPerTurn { get; set; }
    }
}
