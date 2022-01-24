using KCAA.Extensions;
using KCAA.Models;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using KCAA.Settings.GameSettings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using KCAA.Helpers;
using KCAA.Models.Characters;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public abstract class TelegramGameHandlerBase
    {
        protected readonly IPlayerProvider _playerProvider;
        protected readonly ILobbyProvider _lobbyProvider;
        protected readonly HttpClient _httpClient;
        protected readonly GameSettings _gameSettings;

        protected ITelegramBotClient _botClient;

        protected TelegramGameHandlerBase(
            IPlayerProvider playerProvider, 
            ILobbyProvider lobbyProvider, 
            GameSettings gameSettings)
        {
            _playerProvider = playerProvider;
            _lobbyProvider = lobbyProvider;
            _gameSettings = gameSettings;
            _httpClient = new HttpClient();
        }

        protected async Task NextCharactertSelection(string lobbyId)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, _gameSettings.GameApiUrl + $"/{lobbyId}/character_selection");
            var response = await _httpClient.SendAsync(message);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error occurred during character selection: {await response.Content.ReadAsStringAsync()}");
                    return;
                }

                await NextPlayerTurn(lobbyId);
                return;
            }

            var playerId = await response.Content.ReadAsStringAsync();

            var player = await _playerProvider.GetPlayerById(playerId);
            var lobby = await _lobbyProvider.GetLobbyById(player.LobbyId);
            var tgMetadata = player.TelegramMetadata;

            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionPerformedId);
            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionErrorId);

            foreach (var character in lobby.CharacterDeck.Where(c => c.Status == CharacterStatus.Awailable)) 
            {
                var buttons = new List<List<InlineKeyboardButton>>
                {
                    new()
                    {
                        InlineKeyboardButton.WithCallbackData($"Choose {character.CharacterBase.DisplayName}!", $"chooseCharacter_{lobbyId}_{character.Name}")
                    }
                };

                var responseMessage = await _botClient.SendCharacter(
                    tgMetadata.ChatId,
                    character.CharacterBase,
                    character.CharacterBase.Description,
                    new InlineKeyboardMarkup(buttons));

                player.TelegramMetadata.CardMessageIds.Add(responseMessage.MessageId);
            }

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);
        }

        protected async Task NextPlayerTurn(string lobbyId)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _gameSettings.GameApiUrl + $"/{lobbyId}/next_turn");
            var response = await _httpClient.SendAsync(message);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error occurred during defining player's turn: {await response.Content.ReadAsStringAsync()}");

                return;
            }

            // Accepted here means the start of new turn cycle and new character selection or the game has ended
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var responseMessage = await response.Content.ReadAsStringAsync();

                if (responseMessage == GameMessages.GameEndedMessage)
                {
                    await EndGame(lobbyId);
                }
                else
                {
                    await NextCharactertSelection(lobbyId);
                }

                return;
            }

            var content = await response.Content.ReadAsAsync<PlayerTurnDto>();
            var player = await _playerProvider.GetPlayerById(content.PlayerId);
            var tgMetadata = player.TelegramMetadata;

            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionPerformedId);
            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionErrorId);

            switch (content.Character.Effect)
            {
                case CharacterEffect.Killed:
                    await SendActionPerformedMessage(player, GameMessages.KilledMessage);
                    await NextPlayerTurn(lobbyId);
                    return;

                case CharacterEffect.Robbed:
                    await SendActionPerformedMessage(player, GameMessages.RobbedMessage);
                    break;

                default:
                    break;
            }

            await SendChooseResourses(lobbyId, player, content.Character.CharacterBase);
        }

        protected async Task CancelGame(long chatId, Lobby lobby, List<Player> players)
        {
            var message = new HttpRequestMessage(HttpMethod.Delete, _gameSettings.GameApiUrl + $"/{lobby.Id}");
            var response = await _httpClient.SendAsync(message);
            var responseMessage = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                await _botClient.TryDeleteMessage(chatId, lobby.TelegramMetadata.LobbyInfoMessageId);

                if (lobby.Status != LobbyStatus.Configuring)
                {
                    await DeleteMessagesForPlayers(players);
                }
            }

            await _botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, responseMessage);
        }

        protected async Task SendActionPerformedMessage(Player player, string message)
        {
            var responseMessage = await _botClient.PutMessage(player.TelegramMetadata.ChatId, player.TelegramMetadata.ActionPerformedId, message);
            player.TelegramMetadata.ActionPerformedId = responseMessage.MessageId;
            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.ActionPerformedId);
        }

        protected async Task<Player> DisplayPlayerScore(Lobby lobby, IEnumerable<Player> players)
        {
            players = players.OrderByDescending(p => p.Score);

            var builder = new StringBuilder();

            builder.AppendLine($"The scoreboard{GameSymbols.Score}:");
            builder.AppendLine();

            var place = 1;
            foreach (var player in players)
            {
                builder.AppendLine($"{place++}) {player.Name}:");
                builder.AppendLine(GameMessages.GetPlayerInfoMessage(player));
                builder.AppendLine();
            }

            var tgMetadata = lobby.TelegramMetadata;
            var messageId = (await _botClient.PutMessage(tgMetadata.ChatId, tgMetadata.ScoreboardId, builder.ToString())).MessageId;

            lobby.TelegramMetadata.ScoreboardId = messageId;
            await _lobbyProvider.UpdateLobby(lobby, l => l.TelegramMetadata.ScoreboardId);

            return players.FirstOrDefault();
        }

        private async Task EndGame(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);
            var players = await _playerProvider.GetPlayersByLobbyId(lobbyId);

            var chatId = lobby.TelegramMetadata.ChatId;

            var winner = await DisplayPlayerScore(lobby, players);
            await _botClient.SendTextMessageAsync(chatId, string.Format(GameMessages.WinnerMessage, winner.Name));

            await CancelGame(chatId, lobby, players);
        }

        private async Task SendChooseResourses(string lobbyId, Player player, CharacterBase character)
        {

            var tgMessage = $"\n{GameMessages.GetPlayerInfoMessage(player)}\n\n{GameMessages.ChooseResourcesMessage}";

            var coinsAmount = _gameSettings.CoinsPerTurn;
            var cardsAmount = _gameSettings.QuertersPerTurn;

            var coinsString = string.Concat(Enumerable.Repeat(GameSymbols.Coin, coinsAmount));
            var cardsString = string.Concat(Enumerable.Repeat(GameSymbols.Card, cardsAmount));

            var additionalResourses = "";

            if (character.Name == CharacterNames.Merchant)
            {
                var bonusGold = _gameSettings.CoinsPerTurn / 2;
                coinsAmount += bonusGold;
                additionalResourses = string.Concat(Enumerable.Repeat(GameSymbols.Coin, bonusGold));
            }

            if (character.Name == CharacterNames.Architect)
            {
                var bonusCards = _gameSettings.QuertersPerTurn * 2;
                cardsAmount += bonusCards;
                additionalResourses = string.Concat(Enumerable.Repeat(GameSymbols.Card, bonusCards));
            }

            var buttons = new List<List<InlineKeyboardButton>>
            {
                new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{coinsString}{additionalResourses}",
                        $"takeRes_{lobbyId}_{character.Name}_{ResourceType.Coin}_{coinsAmount}"),
                    InlineKeyboardButton.WithCallbackData(
                        $"{cardsString}{additionalResourses}",
                        $"takeRes_{lobbyId}_{character.Name}_{ResourceType.Card}_{cardsAmount}"),
                }
            };

            var responseMessage = await _botClient.SendCharacter(
                player.TelegramMetadata.ChatId,
                character,
                tgMessage,
                new InlineKeyboardMarkup(buttons));

            player.TelegramMetadata.GameActionKeyboardId = responseMessage.MessageId;

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.GameActionKeyboardId);
        }

        private async Task DeleteMessagesForPlayers(IEnumerable<Player> players)
        {
            var deleteTasks = players.Select(async p =>
            {
                var chatId = p.TelegramMetadata.ChatId;

                //Deleting old messages
                var messageIds = new List<int>();
                messageIds.AddRange(p.TelegramMetadata.CardMessageIds);
                messageIds.AddRange(p.TelegramMetadata.MyHandIds);
                messageIds.Add(p.TelegramMetadata.GameActionKeyboardId);
                messageIds.Add(p.TelegramMetadata.ActionErrorId);
                messageIds.Add(p.TelegramMetadata.ActionPerformedId);

                await _botClient.TryDeleteMessages(chatId, messageIds);

                //Deleting reply keyboard and sending final message
                await _botClient.SendTextMessageAsync(chatId, GameMessages.FarewellMessage, replyMarkup: new ReplyKeyboardRemove());
            });

            await Task.WhenAll(deleteTasks);
        }
    }
}
