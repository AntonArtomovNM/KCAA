using KCAA.Models;
using KCAA.Models.Quarters;
using KCAA.Models.Characters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

namespace KCAA.Extensions
{
    public static class TelegramBotClientExtentions
    {
        public static async Task<Message> PutTextMessage(this ITelegramBotClient botClient, long chatId, int messageId, string text)
        {
            Message message;
            try
            {
                message = await botClient.EditMessageTextAsync(chatId, messageId, text);
            }
            catch
            {
                message = await botClient.SendTextMessageAsync(chatId, text);
            }

            return message;
        }

        public static async Task<Message> PutInlineKeyboard(this ITelegramBotClient botClient, long chatId, int messageId, string text, IEnumerable<IEnumerable<InlineKeyboardButton>> buttons)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(buttons);

            Message message;
            try
            {
                message = await botClient.EditMessageTextAsync(chatId, messageId, text, replyMarkup: inlineKeyboard);
            }
            catch
            {
                message = await botClient.SendTextMessageAsync(chatId, text, replyMarkup: inlineKeyboard);
            }

            return message;
        }

        public static async Task DisplayBotCommands(this ITelegramBotClient botClient, long chatId)
        {
            var commands = await botClient.GetMyCommandsAsync();
            var usage = string.Join("\n", commands.Select(c => $"/{c.Command} - {c.Description}"));

            await botClient.SendTextMessageAsync(chatId, usage);
        }

        public static async Task<Message> SendQuarter(this ITelegramBotClient botClient, long chatId, Quarter quarter)
        {
            var quarterStats = $@"{GetQuarterTitleByColor(quarter.DisplayName, quarter.Type)}
Cost: {GetQuarterCost(quarter.Cost)}
{quarter.Description}";

            var cardPick = new InputOnlineFile(quarter.PhotoUri);

            Message resultMessage;
            try
            {
                resultMessage = await botClient.SendPhotoAsync(chatId, cardPick, quarterStats);
            }
            catch(Exception ex)
            {
                resultMessage = await botClient.SendTextMessageAsync(chatId, quarterStats);
                Console.WriteLine($"An error occurred during sending photo: {ex}");
            }

            return resultMessage;
        }

        public static async Task<Message> SendCharacter(this ITelegramBotClient botClient, long chatId, CharacterBase character, IEnumerable<InlineKeyboardButton> buttons = null)
        {
            var characterStats = $@"{GetCharacterTitleByColor(character.DisplayName, character.Type)}
{character.Description}";

            var cardPick = new InputOnlineFile(character.PhotoUri);

            InlineKeyboardMarkup inlineKeyboard = null;
            if (buttons != null) 
            {
                inlineKeyboard = new InlineKeyboardMarkup(buttons);
            }

            Message resultMessage;
            try
            {
                resultMessage = await botClient.SendPhotoAsync(chatId, cardPick, characterStats, replyMarkup: inlineKeyboard);
            }
            catch (Exception ex)
            {
                resultMessage = await botClient.SendTextMessageAsync(chatId, characterStats, replyMarkup: inlineKeyboard);
                Console.WriteLine($"An error occurred during sending photo: {ex}");
            }

            return resultMessage;
        }

        public static async Task<Message[]> SendCardGroup(this ITelegramBotClient botClient, long chatId, IEnumerable<CardObject> cards)
        {
            var mediaGroup = cards.Select(c => new InputMediaPhoto(new InputMedia(c.PhotoUri)) { Caption = c.DisplayName });

            return await botClient.SendMediaGroupAsync(chatId, mediaGroup);
        }

        private static string GetQuarterTitleByColor(string title, ColorType type)
        {
            return type switch
            {
                ColorType.Yellow => $"🟨👑 {title} 👑🟨",
                ColorType.Blue => $"🟦🕍 {title} 🕍🟦",
                ColorType.Green => $"🟩💰 {title} 💰🟩",
                ColorType.Red => $"🟥⚔️ {title} ⚔️🟥",
                ColorType.Purple => $"🟪✨ {title} ✨🟪",
                _ => $"⬜️👁‍🗨 {title} 👁‍🗨⬜️"
            };
        }

        private static string GetQuarterCost(int cost)
        {
            return string.Concat(Enumerable.Repeat("🟡", cost));
        }

        private static string GetCharacterTitleByColor(string title, ColorType type)
        {
            return type switch
            {
                ColorType.Yellow => $"💛👑 {title} 👑💛",
                ColorType.Blue => $"💙🕍 {title} 🕍💙",
                ColorType.Green => $"💚💰 {title} 💰💚",
                ColorType.Red => $"❤⚔️ {title} ⚔️❤",
                ColorType.Purple => $"💜✨ {title} ✨💜",
                _ => $"🤍👁‍🗨 {title} 👁‍🗨🤍"
            };
        }
    }
}

