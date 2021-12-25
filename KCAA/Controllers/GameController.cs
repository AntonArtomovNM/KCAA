using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models;
using KCAA.Models.Characters;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using KCAA.Services.TelegramApi;
using KCAA.Settings.GameSettings;
using Microsoft.AspNetCore.Mvc;

namespace KCAA.Controllers
{
    public class GameController : Controller
    {
        private readonly ILobbyProvider _lobbyProvider;
        private readonly IPlayerProvider _playerProvider;
        private readonly GameSettings _gameSettings;

        public GameController(ILobbyProvider lobbyProvider, IPlayerProvider playerProvider, GameSettings gameSettings)
        {
            _lobbyProvider = lobbyProvider;
            _playerProvider = playerProvider;
            _gameSettings = gameSettings;
        }

        [HttpDelete]
        [Route("{lobbyId}")]
        public async Task<IActionResult> DeleteLobby(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            if (lobby == null)
            {
                return NotFound(GameMessages.LobbyNotFoundError);
            }

            await _lobbyProvider.DeleteLobby(lobby);

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            foreach (var player in players)
            {
                player.LobbyId = Guid.Empty.ToString();
            }

            await _playerProvider.SavePlayers(players);

            return Ok(GameMessages.LobbyCanceledMessage);
        }

        [HttpPost]
        [Route("{lobbyId}/start")]
        public async Task<IActionResult> StartGame(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            if (lobby == null)
            {
                return NotFound(GameMessages.LobbyNotFoundError);
            }
            if (lobby.Status != LobbyStatus.Configuring)
            {
                return BadRequest(GameMessages.GameIsRunningError);
            }
            if (lobby.PlayersCount < _gameSettings.MinPlayersAmount)
            {
                return BadRequest(string.Format(GameMessages.NotEnoughPlayers, _gameSettings.MinPlayersAmount));
            }

            lobby.Status = LobbyStatus.CharacterSelection;
            lobby.CardDeck = GenerateCardDeck();
            lobby.CharacterDeck = GenerateCharacterDeck();

            await _lobbyProvider.SaveLobby(lobby);

            return Ok(GameMessages.GameStartMessage);
        }

        private IEnumerable<string> GenerateCardDeck()
        {
            var deck = new List<string>();

            for (int i = 1; i < 5; i++)
            {
                for (int j = 1; j <= 5; j++)
                {
                    deck.Add($"{Enum.GetName(typeof(ColorType), i)}{j}");
                }
            }

            return deck;
        }

        private IEnumerable<CharacterDto> GenerateCharacterDeck()
        {
            return new List<CharacterDto>
            {
                new (CharacterNames.Assassin),
                new (CharacterNames.Thief),
                new (CharacterNames.Magician),
                new (CharacterNames.King),
                new (CharacterNames.Bishop),
                new (CharacterNames.Merchant),
                new (CharacterNames.Architect),
                new (CharacterNames.Warlord),
                new (CharacterNames.Beggar)
            };
        }
    }
}
