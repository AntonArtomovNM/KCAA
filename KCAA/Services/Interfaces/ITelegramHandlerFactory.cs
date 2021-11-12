using Telegram.Bot.Types.Enums;

namespace KCAA.Services.Interfaces
{
    public interface ITelegramHandlerFactory
    {
        ITelegramUpdateHandler GetHandler(UpdateType type);
    }
}
