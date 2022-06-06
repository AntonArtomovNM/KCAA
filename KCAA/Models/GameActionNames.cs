using System.Collections.Generic;
using KCAA.Helpers;

namespace KCAA.Models
{
    public static class GameActionNames
    {
        private static readonly Dictionary<string, string> _displayNames;

        public const string ChooseCharacter = "chooseCharacter";

        public const string BuildQuarter = "build";

        public const string EndTurn = "endTurn";

        public const string Cancel = "cancel";

        public const string Close = "close";

        public const string Done = "done";

        public const string TakeRevenue = "takeRevenue";

        public const string TakeResources = "takeRes";

        public const string Kill = "kill";

        public const string Steal = "steal";

        public const string ExchangeHands = "exchange";

        public const string DiscardQuarters = "discard";

        public const string DestroyQuarters = "destroy";

        public const string PutUnderMuseum = "museum";

        public const string RebuildScaffolding = "scaffolding";

        public const string DestroyArmory = "armory";

        static GameActionNames()
        {
            _displayNames = SetNames();
        }

        public static string GetActionDisplayName(string gameActionKey) => _displayNames.TryGetValue(gameActionKey, out string name) ? name : gameActionKey;

        private static Dictionary<string, string> SetNames() => new()
        {
            { EndTurn, "End turn" },
            { Cancel, "Cancel" },
            { Close, $"Close" },
            { Done, "Done" },
            { BuildQuarter, $"Build quarter {GameSymbols.PlacedQuarter}" },
            { ChooseCharacter, "Choose" },
            { TakeRevenue, "Take revenue" },
            { Kill, $"Kill {GameSymbols.Killed}" },
            { Steal, $"Steal {GameSymbols.Robbed}"},
            { ExchangeHands, $"Switch hands {GameSymbols.Exchange}" },
            { DiscardQuarters, $"Discard {GameSymbols.Exchange}" },
            { DestroyQuarters, $"Destroy {GameSymbols.Destroy}" },
            { PutUnderMuseum, $"Use Museum {GameSymbols.Museum}" },
            { RebuildScaffolding, $"Use Scaffolding {GameSymbols.Scaffolding}" },
            { DestroyArmory, $"Use Armory {GameSymbols.Destroy}" },
        };
    }
}
