using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace KCAA.Extensions
{
    public static class TelegramBotClientExtentions
    {
        public static async Task<Message> SendInlineKeyboard(this ITelegramBotClient botClient, long chatId, string message, IEnumerable<IEnumerable<InlineKeyboardButton>> buttons)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(buttons);

            return await botClient.SendTextMessageAsync(chatId, message, replyMarkup: inlineKeyboard);
        }
    }
}

