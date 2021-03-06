using KCAA.Models;
using System.Linq;

namespace KCAA.Helpers
{
    public static class GameSymbols
    {
        public static string Coin => "🟡";

        public static string Card => "🎴";

        public static string PlacedQuarter => "🌆";

        public static string Character => "🎭";

        public static string UnknowCharacter => "👤";

        public static string Score => "🏆";

        public static string Crown => "👑";

        public static string Cancel => "🚫";

        public static string Done => "☑️";

        public static string Close => "❌";

        public static string Killed => "🗡️";

        public static string Robbed => "💸";

        public static string Exchange => "🔄";

        public static string Destroy => "⚔️";

        public static string Museum => "🏛";

        public static string Scaffolding => "🪜";

        public static string Forge => "⚒️";

        public static string Laboratory => "⚗️";

        public static string Tab => "     ";

        public static string GetColorByType(ColorType type)
        {
            return type switch
            {
                ColorType.Yellow => "🟨",
                ColorType.Blue => "🟦",
                ColorType.Green => "🟩",
                ColorType.Red => "🟥",
                ColorType.Purple => "🟪",
                _ => "⬜️"
            };
        }

        public static string GetCostInCoins(int cost)
        {
            return string.Concat(Enumerable.Repeat(Coin, cost));
        }
    }
}
