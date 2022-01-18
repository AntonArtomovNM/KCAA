using KCAA.Services.TelegramApi;
using System.Collections.Generic;

namespace KCAA.Models
{
    public static class GameAction
    {
        private static readonly Dictionary<string, string> _displayNames;

        public const string BuildQuarter = "build";

        public const string EndTurn = "endTurn";

        public const string Kill = "kill";

        static GameAction()
        {
            _displayNames = SetNames();
        }

        public static string GetActionDisplayName(string gameActionKey) => _displayNames.TryGetValue(gameActionKey, out string name) ? name : gameActionKey;

        private static Dictionary<string, string> SetNames() => new()
        {
            { EndTurn, "End turn" },
            { Kill, $"Kill {GameSymbolConstants.Killed}" },
            { BuildQuarter, "Build quarter" }
        };
    }
}
