using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using KCAA.Services.Interfaces;
using Serilog;

namespace KCAA.Services.TelegramApi
{
    public class TelegramUpdateGateway : ITelegramUpdateGateway
    {
        private readonly ITelegramHandlerFactory _telegramHandlerFactory;

        public TelegramUpdateGateway(ITelegramHandlerFactory telegramHandlerFactory)
        {
            _telegramHandlerFactory = telegramHandlerFactory;
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var handler = _telegramHandlerFactory.GetHandler(update.Type);

            try
            {
                await handler.Handle(botClient, update);
            }
            catch (Exception exception)
            {
                await HandleErrorAsync(botClient, exception, cancellationToken);
            }
        }

        public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => "An error occured while handling a telegram update"
            };

            Log.Error(exception, ErrorMessage);
            return Task.CompletedTask;
        }
    }
}
