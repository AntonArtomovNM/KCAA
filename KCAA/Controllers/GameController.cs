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
            await StartCharacterSelection(lobby, players);

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
            players.AsParallel().WithDegreeOfParallelism(3).ForAll(p => p.GameActions.Add(GameAction.BuildQuarter));

            await _lobbyProvider.UpdateLobby(lobby, l => l.Status);
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
                .Where(c => c.Status == CharacterStatus.Selected)
                .OrderBy(c => c.CharacterBase.Order)
                .FirstOrDefault();

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            // if no characters left selected, the turn cycle is over and we need to start character selection again
            if (character == null)
            {
                await StartCharacterSelection(lobby, players);

                return Accepted("Starting next character selection");
            }

            var player = players.Find(p => p.CharacterHand.Contains(character.Name));

            if (player == null)
            {
                return NotFound(GameMessages.PlayerNotFoundError);
            }

            if (character.Name == CharacterNames.King)
            {
                await UpdateCharacterSelectionOrder(players, player);
            }

            switch (character.Effect)
            {
                case CharacterEffect.Killed:
                    character.Status = CharacterStatus.SecretlyRemoved;
                    await _lobbyProvider.UpdateLobby(lobby, l => l.CharacterDeck);
                    break;

                case CharacterEffect.Robbed:
                    await RobPlayer(players, player);
                    break;

                default:
                    break;
            }

            var turnDto = new PlayerTurnDto
            {
                PlayerId = player.Id,
                Character = character
            };

            return Ok(turnDto);
        }

        private void GiveStartingResources(Lobby lobby, IEnumerable<Player> players)
        {
            foreach (var player in players)
            {
                for (int i = 0; i < _gameSettings.StartingQuertersAmount; i++)
                {
                    var quarterName = lobby.DrawQuarter();
                    player.QuarterHand.Add(quarterName);
                }
                player.Coins = _gameSettings.StartingCoinsAmount;
            };
        }

        private async Task StartCharacterSelection(Lobby lobby, IEnumerable<Player> players)
        {
            //Clearing character statuses and effects
            lobby.CharacterDeck.AsParallel().WithDegreeOfParallelism(3).ForAll(c =>
            {
                c.Status = CharacterStatus.Awailable;
                c.Effect = CharacterEffect.None;
                c.BuiltQuarters = 0;
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

            await _lobbyProvider.SaveLobby(lobby);
            await _playerProvider.SavePlayers(players);
        }

        private void RemoveCharacter(List<Character> characters, CharacterStatus status)
        {
            characters = characters.Where(c => c.Status == CharacterStatus.Awailable).ToList();
            characters[_random.Next(characters.Count)].Status = status;
        }

        private async Task UpdateCharacterSelectionOrder(List<Player> players, Player newKing)
        {
            var oldKing = players.Find(p => p.HasCrown);

            if(oldKing == newKing)
            {
                return;
            }

            oldKing.HasCrown = false;
            newKing.HasCrown = true;

            await _playerProvider.UpdatePlayer(oldKing, p => p.HasCrown);
            await _playerProvider.UpdatePlayer(newKing, p => p.HasCrown);

            var updateCsorderTasks = players.Select(async p =>
            {
                p.CSOrder = p.CSOrder < newKing.CSOrder ? p.CSOrder + newKing.CSOrder : p.CSOrder - newKing.CSOrder;
                await _playerProvider.UpdatePlayer(p, x => x.CSOrder);
            });

            await Task.WhenAll(updateCsorderTasks);
        }

        private async Task RobPlayer(List<Player> players, Player player)
        {
            var thief = players.Find(p => p.CharacterHand.Contains(CharacterNames.Thief));
            thief.Coins += player.Coins;
            player.Coins = 0;

            await _playerProvider.UpdatePlayer(thief, x => x.Coins);
            await _playerProvider.UpdatePlayer(player, x => x.Coins);
        }
    }
}
