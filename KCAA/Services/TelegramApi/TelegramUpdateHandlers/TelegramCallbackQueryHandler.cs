using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Models;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using KCAA.Settings.GameSettings;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramCallbackQueryHandler : TelegramGameHandlerBase, ITelegramUpdateHandler
    {
        public TelegramCallbackQueryHandler(
            ILobbyProvider lobbyProvider, 
            IPlayerProvider playerProvider,
            GameSettings gameSettings)
            : base(playerProvider, lobbyProvider,gameSettings)
        {
        }

        private ITelegramBotClient _botClient;

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            _botClient = botClient;
            var callbackQuery = update.CallbackQuery;

            Console.WriteLine($"Receive callback query\nChat id: {callbackQuery.Message.Chat.Id}\nUsername: {callbackQuery.From.Username}\nUser id: {callbackQuery.From.Id}\n{callbackQuery.Data}");

            var data = callbackQuery.Data.Split('_');

            var action = data.First() switch
            {
                "chooseCharacter" => HandleChooseCharacter(callbackQuery.Message.Chat.Id, data),
                "takeResources" =>  HandleTakeResources(callbackQuery.Message.Chat.Id, data),
                _ => Task.CompletedTask
            };
            await action;
        }

        private async Task HandleChooseCharacter(long chatId, string[] data)
        {
            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;
            var characterName = data[2];

            player.CharacterHand.Add(characterName);
            var character = lobby.CharacterDeck.Find(x => x.Name == characterName);
            character.Status = CharacterStatus.Selected;

            var deleteMessageTasks = player.TelegramMetadata.CharacterInfoMessageIds.Select(x => _botClient.DeleteMessageAsync(chatId, x));
            await Task.WhenAll(deleteMessageTasks);
            player.TelegramMetadata.CharacterInfoMessageIds = new List<int>();

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.CharacterDeck, lobby.CharacterDeck);

            await _botClient.SendTextMessageAsync(chatId, $"{character.CharacterBase.DisplayName} selected");

            await SendCharactertSelection(_botClient, lobby.Id);
        }

        private async Task HandleTakeResources(long chatId, string[] data)
        {
            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;

            var characterName = data[2];
            var resourceType = Enum.Parse(typeof(ResourceType), data[3]);
            var amount = int.Parse(data[4]);

            switch (resourceType)
            {
                case ResourceType.Card:
                    for (int i = 0; i < amount; i++)
                    {
                        player.QuarterHand.Add(lobby.DrawQuarter());
                    }
                    break;
                default:
                    player.Coins += amount;
                    break;
            }

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.QuarterDeck, lobby.QuarterDeck);
        }

        private async Task<(Player, Lobby)> TryGetPlayerAndLobby(long chatId, string lobbyId) 
        {
            var player = await _playerProvider.GetPlayerByChatId(chatId);

            if (player == null)
            {
                throw new KeyNotFoundException($"Player with chat id {chatId} not found");
            }

            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            if (lobby == null)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.IdrakError);

                throw new KeyNotFoundException($"Lobby with id {lobbyId} not found");
            }

            return (player, lobby);
        }
    }
}
