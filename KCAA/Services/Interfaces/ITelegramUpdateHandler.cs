using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace KCAA.Services.Interfaces
{
    public interface ITelegramUpdateHandler
    {
        Task Handle(ITelegramBotClient botClient, Update update);
    }
}
