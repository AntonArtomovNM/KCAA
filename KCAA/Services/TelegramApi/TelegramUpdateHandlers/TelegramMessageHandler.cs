﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using KCAA.Extensions;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using KCAA.Settings;
using KCAA.Settings.GameSettings;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramMessageHandler : ITelegramUpdateHandler
    {
        private readonly ILobbyProvider _lobbyProvider;
        private readonly IPlayerProvider _playerProvider;
        private readonly TelegramSettings _telegramSettings;
        private readonly GameSettings _gameSettings;

        public TelegramMessageHandler(
            ILobbyProvider lobbyProvider, 
            IPlayerProvider playerProvider, 
            TelegramSettings telegramSettings,
            GameSettings gameSettings) 
        {
            _lobbyProvider = lobbyProvider;
            _playerProvider = playerProvider;
            _telegramSettings = telegramSettings;
            _gameSettings = gameSettings;
        }

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            var message = update.Message;

            Console.WriteLine($"Receive message type: {message.Type}\nChat id: {message.Chat.Id}\nUsername: {message.From.Username}\nUser id: {message.From.Id}\n{message.Text}");

            if (message.Type != MessageType.Text)
            {
                return;
            }

            var text = message.Text.Split(' ', '@');
            var action = text.First() switch
            {
                "/create_lobby" => CreateGameLobby(botClient, message.Chat),
                "/cancel_lobby" => CancelGameLobby(botClient, message.Chat),
                "/start" => HandleBotStart(botClient, text.Last(), message.Chat),
                "/help" => DisplayCommands(botClient, message.Chat.Id),
                _ => Task.CompletedTask
            };
            await action;
        }

        private async Task CreateGameLobby(ITelegramBotClient botClient, Chat chat)
        {
            //if it's a user chat
            if (chat.Id > 0)
            {
                await botClient.SendTextMessageAsync(chat.Id, BotMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = _lobbyProvider.GetLobbyByChatId(chat.Id);

            if (lobby != null)
            {
                await botClient.SendTextMessageAsync(chat.Id, BotMessages.LobbyAlreadyCreatedError);
                return;
            }

            lobby = new Lobby
            {
                ChatId = chat.Id
            };

            await _lobbyProvider.CreateLobby(lobby);

            var buttons = new[]
            {
                new []
                {
                    InlineKeyboardButton.WithUrl("Join", $"{_telegramSettings.BotLink}?start={chat.Id}")
                }
            };
            await botClient.SendInlineKeyboard(chat.Id, BotMessages.LobbyRegistrationMessage, buttons);
        }

        private async Task CancelGameLobby(ITelegramBotClient botClient, Chat chat)
        {
            //if it's a user chat
            if (chat.Id > 0)
            {
                await botClient.SendTextMessageAsync(chat.Id, BotMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = _lobbyProvider.GetLobbyByChatId(chat.Id);

            if (lobby == null)
            {
                await botClient.SendTextMessageAsync(chat.Id, BotMessages.LobbyNotFoundError);
                return;
            }

            if (lobby.Status != LobbyStatus.Configuring)
            {
                await botClient.SendTextMessageAsync(chat.Id, BotMessages.GameIsRunningError);
                return;
            }

            await _lobbyProvider.DeleteLobby(lobby);

            var players = _playerProvider.GetPlayersByLobbyId(lobby.Id);

            foreach (var player in players)
            {
                player.LobbyId = Guid.Empty;
            }

            await _playerProvider.SavePlayers(players);

            await botClient.SendTextMessageAsync(chat.Id, BotMessages.LobbyCanceledMessage);
        }

        private async Task HandleBotStart(ITelegramBotClient botClient, string payload, Chat chat)
        {
            var responce = BotMessages.GreetingsMessage;

            if (long.TryParse(payload, out long groupChatId))
            {
                var lobby = _lobbyProvider.GetLobbyByChatId(groupChatId);

                if (lobby != null)
                {
                    responce = await JoinLobby(botClient, chat, groupChatId, lobby);
                }
            }

            await botClient.SendTextMessageAsync(chat.Id, responce);
        }

        private async Task<string> JoinLobby(ITelegramBotClient botClient, Chat playerChat, long groupChatId, Lobby lobby)
        {
            if (lobby.Status != LobbyStatus.Configuring)
            {
                return BotMessages.GameIsRunningError;
            }

            var existingPlayer = _playerProvider.GetPlayerByChatId(playerChat.Id);

            if (existingPlayer != null && existingPlayer.LobbyId != Guid.Empty)
            {
                return BotMessages.LobbyAlreadyJoinedError;
            }

            if (lobby.PlayersCount >= _gameSettings.MaxPlayersAmount)
            {
                return BotMessages.LobbyIsFullError;
            }

            var player = new Player
            {
                Id = existingPlayer?.Id ?? Guid.Empty,
                Name = string.IsNullOrWhiteSpace(playerChat.Username) ? playerChat.FirstName : playerChat.Username,
                LobbyId = lobby.Id,
                ChatId = playerChat.Id
            };

            await _playerProvider.SavePlayer(player);

            lobby.PlayersCount++;
            await _lobbyProvider.SaveLobby(lobby);

            var groupChat = await botClient.GetChatAsync(groupChatId);

            return string.Format(BotMessages.LobbyJoinedMessage, groupChat.Title);
        }

        private async Task DisplayCommands(ITelegramBotClient botClient, long chatId)
        {
            var commands = await botClient.GetMyCommandsAsync();
            var usage = string.Join("\n", commands.Select(c => $"/{c.Command} - {c.Description}"));

            await botClient.SendTextMessageAsync(chatId, usage);
        }
    }
}