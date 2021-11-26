using KCAA.Models;
using KCAA.Models.Cards;
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

        public static async Task<Message> SendCard(this ITelegramBotClient botClient, long chatId, Card card)
        {
            var cardStats = $@"{GetCardTitleByColor(card.DisplayName, card.Type)}
Cost: {GetCardCost(card.Cost)}
{card.Description}";

            //var cardPick = new InputOnlineFile(card.PhotoUri);
            var cardPick = new InputOnlineFile(@"https://i.pinimg.com/736x/b5/be/91/b5be91f0de3a5eaa5268a77a92a28d57--over-the-garden-wall-background-designs.jpg");

            Message resultMessage;
            try
            {
                resultMessage = await botClient.SendPhotoAsync(chatId, cardPick, cardStats);
            }
            catch(Exception ex)
            {
                resultMessage = await botClient.SendTextMessageAsync(chatId, cardStats);
                Console.WriteLine($"An error occurred during sending photo: {ex}");
            }

            return resultMessage;
        }

        public static async Task<Message> SendCharacter(this ITelegramBotClient botClient, long chatId, Character character)
        {
            var characterStats = $@"{GetCharacterTitleByColor(character.DisplayName, character.Type)}
{character.Description}";

            //var cardPick = new InputOnlineFile(card.PhotoUri);
            var characterPick = new InputOnlineFile(@"https://i.ytimg.com/an/ObLQxub_dIZmdjRq8pPCJQ/featured_channel.jpg?v=60d8a1d9");

            Message resultMessage;
            try
            {
                resultMessage = await botClient.SendPhotoAsync(chatId, characterPick, characterStats);
            }
            catch (Exception ex)
            {
                resultMessage = await botClient.SendTextMessageAsync(chatId, characterStats);
                Console.WriteLine($"An error occurred during sending photo: {ex}");
            }

            return resultMessage;
        }

        private static string GetCardTitleByColor(string title, ColorType type)
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

        private static string GetCardCost(int cost)
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

