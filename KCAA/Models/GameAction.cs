using System.Collections.Generic;
using KCAA.Helpers;

namespace KCAA.Models
{
    public static class GameAction
    {
        private static readonly Dictionary<string, string> _displayNames;

        public const string BuildQuarter = "build";

        public const string EndTurn = "endTurn";

        public const string Kill = "kill";

        public const string Steal = "steal";

        static GameAction()
        {
            _displayNames = SetNames();
        }

        public static string GetActionDisplayName(string gameActionKey) => _displayNames.TryGetValue(gameActionKey, out string name) ? name : gameActionKey;

        private static Dictionary<string, string> SetNames() => new()
        {
            { EndTurn, "End turn" },
            { BuildQuarter, "Build quarter" },
            { Kill, $"Kill {GameSymbolConstants.Killed}" },
            { Steal, "Steal"}
        };
    }
}
