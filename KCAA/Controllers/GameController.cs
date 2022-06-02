using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Helpers;
using KCAA.Models;
using KCAA.Models.Characters;
using KCAA.Models.MongoDB;
using KCAA.Models.Quarters;
using KCAA.Services.Interfaces;
using KCAA.Settings.GameSettings;
using Microsoft.AspNetCore.Mvc;

namespace KCAA.Controllers
{
    public class GameController : Controller
    {
        private readonly ILobbyProvider _lobbyProvider;
        private readonly IPlayerProvider _playerProvider;
        private readonly ICardFactory<Quarter> _quarterFactory;
        private readonly GameSettings _gameSettings;
        private readonly Random _random;

        public GameController(
            ILobbyProvider lobbyProvider,
            IPlayerProvider playerProvider,
            GameSettings gameSettings,
            ICardFactory<Quarter> quarterFactory)
        {
            _lobbyProvider = lobbyProvider;
            _playerProvider = playerProvider;
            _gameSettings = gameSettings;
            _random = new Random();
            _quarterFactory = quarterFactory;
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

            return Ok(GameMessages.FarewellMessage);
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
            lobby.QuarterDeck.AddRange(_quarterFactory.GetFilteredNames(q => q.Type == ColorType.Purple));

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            GiveStartingResources(lobby, players);
            await StartCharacterSelection(lobby, players);

            return Ok();
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
            await _lobbyProvider.UpdateLobby(lobby, l => l.Status);

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

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id, loadPlacedQuarters: true);

            // if no characters left selected, the turn cycle is over and we need to start character selection again or end the game
            if (character == null)
            {
                if (players.Any(p => p.PlacedQuarters.Count >= _gameSettings.QuartersToWin))
                {
                    return Accepted(string.Empty, GameMessages.GameEndedMessage);
                }

                await StartCharacterSelection(lobby, players);

                return Accepted(string.Empty, "Starting next character selection");
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

            var turnDto = new PlayerTurnDto
            {
                PlayerId = player.Id,
                Character = character
            };

            switch (character.Effect)
            {
                case CharacterEffect.Killed:
                    character.Status = CharacterStatus.SecretlyRemoved;
                    await _lobbyProvider.UpdateLobby(lobby, l => l.CharacterDeck);

                    return Ok(turnDto);

                case CharacterEffect.Robbed:
                    await RobPlayer(players, player);
                    break;

                default:
                    break;
            }

            character.Status = CharacterStatus.Playing;
            await _lobbyProvider.UpdateLobby(lobby, l => l.CharacterDeck);

            await SetPlayerActions(character, player);

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

            if (oldKing == newKing)
            {
                return;
            }

            oldKing.HasCrown = false;
            newKing.HasCrown = true;

            await _playerProvider.UpdatePlayer(oldKing, p => p.HasCrown);
            await _playerProvider.UpdatePlayer(newKing, p => p.HasCrown);

            var csorderDelta = newKing.CSOrder;
            var updateCsorderTasks = players.Select(async p =>
            {
                p.CSOrder = p.CSOrder < csorderDelta ? p.CSOrder + csorderDelta : p.CSOrder - csorderDelta;
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

        private async Task SetPlayerActions(Character character, Player player) 
        {
            if (!string.IsNullOrWhiteSpace(character.CharacterBase.GameAction))
            {
                player.GameActions.Add(character.CharacterBase.GameAction);
            }

            if (character.CharacterBase.Type != ColorType.None)
            {
                player.GameActions.Add(GameActionNames.TakeRevenue);
            }

            player.GameActions.Add(GameActionNames.BuildQuarter);

            foreach (var placerQuarter in player.PlacedQuarters)
            {
                var gameAction = placerQuarter.QuarterBase.GameAction;

                if (!string.IsNullOrWhiteSpace(gameAction))
                {
                    player.GameActions.Add(gameAction);
                }
            }

            await _playerProvider.UpdatePlayer(player, p => p.GameActions);
        }
    }
}
