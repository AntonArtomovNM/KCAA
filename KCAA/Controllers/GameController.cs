using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Models;
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
        private readonly Random _random;

        public GameController(
            ILobbyProvider lobbyProvider, 
            IPlayerProvider playerProvider, 
            GameSettings gameSettings)
        {
            _lobbyProvider = lobbyProvider;
            _playerProvider = playerProvider;
            _gameSettings = gameSettings;
            _random = new Random();
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
                return BadRequest(string.Format(GameMessages.NotEnoughPlayersError, _gameSettings.MinPlayersAmount));
            }

            lobby.GenerateBasicQuarterDeck();
            lobby.GenerateCharacterDeck();
            
            await GiveStartingResources(lobby);
            StartCharacterSelection(lobby);

            await _lobbyProvider.SaveLobby(lobby);

            return Ok(GameMessages.GameStartMessage);
        }

        [HttpGet]
        [Route("{lobbyId}/character_selection")]
        public async Task<IActionResult> CharacterSelection(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            if (lobby == null)
            {
                return NotFound(GameMessages.LobbyNotFoundError);
            }

            if (lobby.Status != LobbyStatus.CharacterSelection)
            {
                return BadRequest(GameMessages.NotValidLobbyStateError);
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

        [HttpGet]
        [Route("{lobbyId}/play")]
        public async Task<IActionResult> GetNextPlayerTurn(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            if (lobby == null)
            {
                return NotFound(GameMessages.LobbyNotFoundError);
            }

            if (lobby.Status != LobbyStatus.Playing)
            {
                return BadRequest(GameMessages.NotValidLobbyStateError);
            }

            var character = lobby.CharacterDeck
                .Where(c => c.Status == CharacterStatus.Selected)
                .OrderBy(c => c.CharacterBase.Order)
                .FirstOrDefault();

            if (character == null)
            {
                return NotFound(GameMessages.CharacterNotFoundError);
            }

            var player = (await _playerProvider.GetPlayersByLobbyId(lobby.Id)).Find(p => p.CharacterHand.Contains(character.Name));

            if (player == null)
            {
                return NotFound(GameMessages.PlayerNotFoundError);
            }

            var turnDto = new PlayerTurnDto
            {
                PlayerId = player.Id,
                Character = character.CharacterBase
            };

            return Ok(turnDto);
        }

        private async Task GiveStartingResources(Lobby lobby)
        {
            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            foreach (var p in players)
            {
                for (int i = 0; i < _gameSettings.StartingQuertersAmount; i++)
                {
                    var quarter = lobby.DrawQuarter();
                    p.QuarterHand.Add(quarter);
                }
                p.Coins = _gameSettings.StartingCoinsAmount;

                await _playerProvider.SavePlayer(p);
            };
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
            characters = characters.Where(c => c.Status == CharacterStatus.Awailable).ToList();
            characters[_random.Next(characters.Count)].Status = status;
        }
    }
}
