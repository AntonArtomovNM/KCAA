using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using KCAA.Services.Interfaces;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramUnknownUpdateHandler : ITelegramUpdateHandler
    {
        public Task Handle(ITelegramBotClient botClient, Update update)
        {
            Console.WriteLine($"Unknown update type: {update.Type}");
            return Task.CompletedTask;
        }
    }
}
