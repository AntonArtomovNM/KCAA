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
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public abstract class TelegramGameHandlerBase
    {
        protected readonly IPlayerProvider _playerProvider;
        protected readonly ILobbyProvider _lobbyProvider;
        protected readonly HttpClient _httpClient;
        protected readonly GameSettings _gameSettings;

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

        protected async Task SendCharactertSelection(ITelegramBotClient botClient, string lobbyId)
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

                await NextPlayerTurn(botClient, lobbyId);
                return;
            }

            var playerId = await response.Content.ReadAsStringAsync();

            var player = await _playerProvider.GetPlayerById(playerId);
            var lobby = await _lobbyProvider.GetLobbyById(player.LobbyId);

            foreach (var character in lobby.CharacterDeck.Where(c => c.Status == CharacterStatus.Awailable)) 
            {
                var buttons = new List<List<InlineKeyboardButton>>
                {
                    new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData($"Choose {character.CharacterBase.DisplayName}!", $"chooseCharacter_{lobbyId}_{character.Name}")
                    }
                };

                var responseMessage = await botClient.SendCharacter(
                    player.TelegramMetadata.ChatId,
                    character.CharacterBase,
                    character.CharacterBase.Description,
                    buttons);

                player.TelegramMetadata.CardMessageIds.Add(responseMessage.MessageId);
            }

            await _playerProvider.UpdatePlayer(player.Id, p => p.TelegramMetadata, player.TelegramMetadata);
        }

        protected async Task NextPlayerTurn(ITelegramBotClient botClient, string lobbyId)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _gameSettings.GameApiUrl + $"/{lobbyId}/next_turn");
            var response = await _httpClient.SendAsync(message);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error occurred during defining player's turn: {await response.Content.ReadAsStringAsync()}");

                return;
            }

            // Accepted here means the start of new turn cycle and new character selection
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                await SendCharactertSelection(botClient, lobbyId);

                return;
            }

            var content = await response.Content.ReadAsAsync<PlayerTurnDto>();

            var player = await _playerProvider.GetPlayerById(content.PlayerId);

            var tgMessage = GameMessages.GetPlayerTurnMessage(player.Coins, player.QuarterHand.Count, player.Score)
                + "\n\n" + string.Format(GameMessages.ChooseResourcesMessage, _gameSettings.CoinsPerTurn, _gameSettings.QuertersPerTurn);

            var buttons = new List<List<InlineKeyboardButton>>
            {
                new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{string.Concat(Enumerable.Repeat("🟡", _gameSettings.CoinsPerTurn))}",
                        $"takeRes_{lobbyId}_{content.Character.Name}_{ResourceType.Coin}_{_gameSettings.CoinsPerTurn}"),
                    InlineKeyboardButton.WithCallbackData(
                        $"{string.Concat(Enumerable.Repeat("🎴", _gameSettings.QuertersPerTurn))}",
                        $"takeRes_{lobbyId}_{content.Character.Name}_{ResourceType.Card}_{_gameSettings.QuertersPerTurn}"),
                }
            };

            var responseMessage = await botClient.SendCharacter(player.TelegramMetadata.ChatId, content.Character, tgMessage, buttons);
            player.TelegramMetadata.GameActionKeyboardId = responseMessage.MessageId;

            await _playerProvider.UpdatePlayer(player.Id, p => p.TelegramMetadata, player.TelegramMetadata);
        }
    }
}
