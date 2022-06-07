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

        public int QuartersPerTurn { get; set; }

        public int QuartersToWin { get; set; }

        public int CoinsPerLaboratoryUse { get; set; }

        public int CoinsPerForgeUse { get; set; }

        public int QuartersPerForgeUse { get; set; }

        public int FullBuildBonus { get; set; }

        public int AllTypesBonus { get; set; }
    }
}
