using System.Collections.Generic;
using Telegram.Bot.Types;

namespace KCAA.Settings
{
    public class TelegramSettings
    {
        public static string ConfigKey => "TelegramSettings";

        public string BotToken { get; set; }

        public string BotLink { get; set; }

        public IEnumerable<BotCommand> BotCommands { get; set; }
    }
}
