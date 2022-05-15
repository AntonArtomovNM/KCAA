using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using KCAA.Services.Interfaces;
using Serilog;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramUnknownUpdateHandler : ITelegramUpdateHandler
    {
        public Task Handle(ITelegramBotClient botClient, Update update)
        {
            Log.Warning($"Unknown update type: {update.Type}");
            return Task.CompletedTask;
        }
    }
}
