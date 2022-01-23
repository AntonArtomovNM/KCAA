using System.Collections.Generic;
using KCAA.Helpers;

namespace KCAA.Models
{
    public static class GameAction
    {
        private static readonly Dictionary<string, string> _displayNames;

        public const string BuildQuarter = "build";

        public const string EndTurn = "endTurn";

        public const string Cancel = "cancel";

        public const string Done = "done";

        public const string TakeRevenue = "takeRevenue";

        public const string Kill = "kill";

        public const string Steal = "steal";

        public const string ExchangeHands = "exchange";

        public const string DiscardQuarters = "discard";

        static GameAction()
        {
            _displayNames = SetNames();
        }

        public static string GetActionDisplayName(string gameActionKey) => _displayNames.TryGetValue(gameActionKey, out string name) ? name : gameActionKey;

        private static Dictionary<string, string> SetNames() => new()
        {
            { EndTurn, "End turn" },
            { Cancel, "Cancel" },
            { Done, "Done" },
            { BuildQuarter, "Build quarter" },
            { TakeRevenue, "Take revenue" },
            { Kill, $"Kill {GameSymbols.Killed}" },
            { Steal, $"Steal {GameSymbols.Robbed}"},
            { ExchangeHands, $"Exchange hands {GameSymbols.Exchange}" },
            { DiscardQuarters, $"Discard {GameSymbols.Exchange}" },
        };
    }
}
