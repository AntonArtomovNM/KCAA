﻿using KCAA.Models;
using KCAA.Models.Quarters;
using KCAA.Models.Characters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

namespace KCAA.Extensions
{
    public static class TelegramBotClientExtentions
    {
        public static async Task<Message> PutMessage(this ITelegramBotClient botClient, long chatId, int messageId, string text, InlineKeyboardMarkup inlineKeyboard = null)
        {
            Message message;
            try
            {
                message = await botClient.EditMessageTextAsync(chatId, messageId, text, replyMarkup: inlineKeyboard);
            }
            catch
            {
                await botClient.TryDeleteMessage(chatId, messageId);
                message = await botClient.SendTextMessageAsync(chatId, text, replyMarkup: inlineKeyboard);
            }

            return message;
        }

        public static async Task TryDeleteMessage(this ITelegramBotClient botClient, long chatId, int messageId)
        {
            //handling case when message is already deleted
            try
            {
                await botClient.DeleteMessageAsync(chatId, messageId);
            }
            catch { }
        }
        public static async Task TryDeleteMessages(this ITelegramBotClient botClient, long chatId, IEnumerable<int> messageIds)
        {
            var deleteTasks = messageIds.AsParallel().WithDegreeOfParallelism(3).Select(id => botClient.TryDeleteMessage(chatId, id));
            await Task.WhenAll(deleteTasks);
        }


        public static async Task DisplayBotCommands(this ITelegramBotClient botClient, long chatId)
        {
            var commands = await botClient.GetMyCommandsAsync();
            var usage = string.Join("\n", commands.Select(c => $"/{c.Command} - {c.Description}"));

            await botClient.SendTextMessageAsync(chatId, usage);
        }

        public static async Task<Message> SendQuarter(this ITelegramBotClient botClient, long chatId, Quarter quarter, InlineKeyboardMarkup inlineKeyboard = null)
        {
            var tgmessage = $@"{GetQuarterTitleByColor(quarter.DisplayName, quarter.Type)}
Cost: {GetQuarterCost(quarter.Cost)}
{quarter.Description}";

            var photo = new InputOnlineFile(quarter.PhotoUri);

            return await SendMessageWithPhoto(botClient, chatId, inlineKeyboard, photo, tgmessage);
        }

        public static async Task<Message> SendCharacter(this ITelegramBotClient botClient, long chatId, CharacterBase character, string text, InlineKeyboardMarkup inlineKeyboard = null)
        {
            var tgmessage = $@"{GetCharacterTitleByColor(character.DisplayName, character.Type)}
{text}";
            var photo = new InputOnlineFile(character.PhotoUri);

            return await SendMessageWithPhoto(botClient, chatId, inlineKeyboard, photo, tgmessage);
        }

        public static async Task<IEnumerable<int>> SendCardGroup(this ITelegramBotClient botClient, long chatId, IEnumerable<CardObject> cards, string captionExtention = "")
        {
            if (cards == null || !cards.Any()) 
            {
                return new int[0];
            }

            var mediaGroup = cards.Select(c => new InputMediaPhoto(new InputMedia(c.PhotoUri))
            {
                Caption = $"{c.DisplayName} ({captionExtention})"
            }) ;

            return (await botClient.SendMediaGroupAsync(chatId, mediaGroup)).Select(m => m.MessageId);
        }

        private static async Task<Message> SendMessageWithPhoto(ITelegramBotClient botClient, long chatId, InlineKeyboardMarkup inlineKeyboard, InputOnlineFile photo, string tgmessage)
        {
            Message resultMessage;
            try
            {
                resultMessage = await botClient.SendPhotoAsync(chatId, photo, tgmessage, replyMarkup: inlineKeyboard);
            }
            catch (Exception ex)
            {
                resultMessage = await botClient.SendTextMessageAsync(chatId, tgmessage, replyMarkup: inlineKeyboard);
                Console.WriteLine($"An error occurred during sending photo: {ex}");
            }

            return resultMessage;
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
            return string.Concat(Enumerable.Repeat(GameSymbols.Coin, cost));
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

