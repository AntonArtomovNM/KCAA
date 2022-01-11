using System;
using System.Collections.Generic;
using System.Linq;
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

            lobby.QuarterDeck = GenerateQuarterDeck();
            lobby.CharacterDeck = GenerateCharacterDeck();
            //TODO: Add cards to players
            StartCharacterSelection(lobby);

            await _lobbyProvider.SaveLobby(lobby);

            return Ok(GameMessages.GameStartMessage);
        }

        [HttpPost]
        [Route("{lobbyId}/character_selection")]
        public async Task<IActionResult> CharacterSelection(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            if (lobby == null)
            {
                return NotFound(GameMessages.LobbyNotFoundError);
            }

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            var player = players.Where(p => !p.CharacterHand.Any()).OrderBy(p => p.CSOrder).FirstOrDefault();

            if (player != null)
            {
                return Ok(player.Id);
            }

            lobby.Status = LobbyStatus.Playing;
            await _lobbyProvider.UpdateLobby(lobbyId, l => l.Status, lobby.Status);
            return Accepted();
        }

        private void StartCharacterSelection(Lobby lobby)
        {
            lobby.Status = LobbyStatus.CharacterSelection;
            RemoveCharacter(lobby.CharacterDeck, CharacterStatus.SecretlyRemoved);

            switch (lobby.PlayersCount)
            {
                case < 5:
                    RemoveCharacter(lobby.CharacterDeck, CharacterStatus.Removed);
                    RemoveCharacter(lobby.CharacterDeck, CharacterStatus.Removed);
                    break;
                case < 7:
                    RemoveCharacter(lobby.CharacterDeck, CharacterStatus.Removed);
                    break;
            }
        }


        private void RemoveCharacter(List<Character> characters, CharacterStatus status)
        {
            var rand = new Random();
            characters = characters.Where(c => c.Status == CharacterStatus.Awailable).ToList();
            characters[rand.Next(characters.Count)].Status = status;
        }

        private List<string> GenerateQuarterDeck()
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

        private List<Character> GenerateCharacterDeck()
        {
            return new List<Character>
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
