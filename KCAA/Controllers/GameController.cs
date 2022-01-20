using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Helpers;
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

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            GiveStartingResources(lobby, players);
            StartCharacterSelection(lobby, players);

            await _lobbyProvider.SaveLobby(lobby);
            await _playerProvider.SavePlayers(players);

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
            players.AsParallel().WithDegreeOfParallelism(5).ForAll(p => p.GameActions.Add(GameAction.BuildQuarter));

            await _lobbyProvider.UpdateLobby(lobbyId, l => l.Status, lobby.Status);
            await _playerProvider.SavePlayers(players);
            return Accepted();
        }

        [HttpPost]
        [Route("{lobbyId}/next_turn")]
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
                .Where(c => c.Status == CharacterStatus.Selected && c.Effect != CharacterEffect.Killed)
                .OrderBy(c => c.CharacterBase.Order)
                .FirstOrDefault();

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            // if no characters left selected, the turn cycle is over and we need to start character selection again
            if (character == null)
            {
                StartCharacterSelection(lobby, players);
                await _lobbyProvider.SaveLobby(lobby);
                await _playerProvider.SavePlayers(players);

                return Accepted("Starting next character selection");
            }

            var player = players.Find(p => p.CharacterHand.Contains(character.Name));

            if (player == null)
            {
                return NotFound(GameMessages.PlayerNotFoundError);
            }

            if (character.Effect == CharacterEffect.Robbed)
            {
                var thief = players.Find(p => p.CharacterHand.Contains(CharacterNames.Thief));
                thief.Coins += player.Coins;
                player.Coins = 0;

                await _playerProvider.UpdatePlayer(thief.Id, x => x.Coins, thief.Coins);
                await _playerProvider.UpdatePlayer(player.Id, x => x.Coins, player.Coins);
            }

            var turnDto = new PlayerTurnDto
            {
                PlayerId = player.Id,
                Character = character.CharacterBase
            };

            return Ok(turnDto);
        }

        private void GiveStartingResources(Lobby lobby, IEnumerable<Player> players)
        {
            foreach (var player in players)
            {
                for (int i = 0; i < _gameSettings.StartingQuertersAmount; i++)
                {
                    var quarter = lobby.DrawQuarter();
                    player.QuarterHand.Add(quarter);
                }
                player.Coins = _gameSettings.StartingCoinsAmount;
            };
        }

        private void StartCharacterSelection(Lobby lobby, IEnumerable<Player> players)
        {
            //Clearing character statuses and effects
            lobby.CharacterDeck.AsParallel().WithDegreeOfParallelism(3).ForAll(c =>
            {
                c.Status = CharacterStatus.Awailable;
                c.Effect = CharacterEffect.None;
            });

            //Deleating selected character and actions for players
            players.AsParallel().WithDegreeOfParallelism(3).ForAll(p =>
            {
                p.CharacterHand.Clear();
                p.GameActions.Clear();
            });

            //Removing some characters from the pool
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
