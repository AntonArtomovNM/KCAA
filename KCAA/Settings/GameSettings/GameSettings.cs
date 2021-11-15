namespace KCAA.Settings.GameSettings
{
    public class GameSettings
    {
        public static string ConfigKey => "GameSettings";

        public string CardSettingsPath { get; set; }

        public string CharacterSettingsPath { get; set; }

        public int MaxPlayersAmount { get; set; }

        public int MinPlayersAmount { get; set; }
    }
}
