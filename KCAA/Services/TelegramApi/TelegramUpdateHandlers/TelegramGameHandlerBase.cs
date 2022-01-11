using KCAA.Extensions;
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
            var message = new HttpRequestMessage(HttpMethod.Post, _gameSettings.GameApiUrl + $"/{lobbyId}/character_selection");
            var response = await _httpClient.SendAsync(message);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error occurred during character selection: {response.Content}");
                }

                 return;
            }

            var playerId = await response.Content.ReadAsStringAsync();

            var player = await _playerProvider.GetPlayerById(playerId);
            var lobby = await _lobbyProvider.GetLobbyById(player.LobbyId);

            foreach (var character in lobby.CharacterDeck.Where(c => c.Status == CharacterStatus.Awailable)) 
            {
                var buttons = new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData($"Choose {character.CharacterBase.DisplayName}!", $"chooseCharacter_{character.Name}")
                };

                var responseMessage = await botClient.SendCharacter(player.TelegramMetadata.ChatId, character.CharacterBase, buttons);

                player.TelegramMetadata.CharacterInfoMessageIds.Add(responseMessage.MessageId);
            }

            await _playerProvider.SavePlayer(player);
        }
    }
}
