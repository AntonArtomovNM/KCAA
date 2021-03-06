using KCAA.Extensions;
using KCAA.Models;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using KCAA.Settings.GameSettings;
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
using Telegram.Bot.Types.Enums;
using Serilog;
using KCAA.Models.Quarters;
using System;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public abstract class TelegramGameHandlerBase
    {
        protected readonly IPlayerProvider _playerProvider;
        protected readonly ILobbyProvider _lobbyProvider;
        protected readonly ICardFactory<Quarter> _quarterFactory;
        protected readonly HttpClient _httpClient;
        protected readonly GameSettings _gameSettings;

        protected ITelegramBotClient _botClient;

        protected TelegramGameHandlerBase(
            IPlayerProvider playerProvider,
            ILobbyProvider lobbyProvider,
            ICardFactory<Quarter> quarterFactory,
            GameSettings gameSettings)
        {
            _playerProvider = playerProvider;
            _lobbyProvider = lobbyProvider;
            _gameSettings = gameSettings;
            _quarterFactory = quarterFactory;
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
                    Log.Error(await response.Content.ReadAsStringAsync(), "Error occurred during character selection");
                    return;
                }

                await NextPlayerTurn(lobbyId);
                return;
            }

            var playerId = await response.Content.ReadAsStringAsync();

            var player = await _playerProvider.GetPlayerById(playerId, loadPlacedQuarters: true);
            var lobby = await _lobbyProvider.GetLobbyById(player.LobbyId);
            var tgMetadata = player.TelegramMetadata;

            await DisplayPlayersData(lobby);

            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionPerformedId);
            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionErrorId);

            foreach (var character in lobby.CharacterDeck.Where(c => c.Status == CharacterStatus.Awailable)) 
            {
                var characterBase = character.CharacterBase;
                var characterDescription = characterBase.Description;

                if (characterBase.Type != ColorType.None)
                {
                    var rightTypeQuartersCount = player.PlacedQuarters.Where(q => q.QuarterBase.Type == characterBase.Type || q.Name == QuarterNames.SchoolOfMagic).Count();
                    characterDescription = string.Format(characterDescription, rightTypeQuartersCount);
                } 
                else if (characterBase.Name == CharacterNames.Beggar)
                {
                    var rightCostQuartersCount = player.PlacedQuarters.Where(q => q.QuarterBase.Cost == 1).Count();
                    characterDescription = string.Format(characterDescription, rightCostQuartersCount);
                }

                var buttons = new List<List<InlineKeyboardButton>>
                {
                    new()
                    {
                        InlineKeyboardButton.WithCallbackData(
                            $"{GameActionNames.GetActionDisplayName(GameActionNames.ChooseCharacter)} {characterBase.DisplayName}!", 
                            $"{GameActionNames.ChooseCharacter}_{lobbyId}_{character.Name}")
                    }
                };

                var responseMessage = await _botClient.SendCharacter(
                    tgMetadata.ChatId,
                    characterBase,
                    characterDescription,
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
                Log.Error(await response.Content.ReadAsStringAsync(), "Error occurred during defining player's turn");

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
                    await DisplayRemovedCharacters(lobbyId);

                    await NextCharactertSelection(lobbyId);
                }

                return;
            }

            var turnDto = await response.Content.ReadAsAsync<PlayerTurnDto>();
            var character = turnDto.Character;
            var player = await _playerProvider.GetPlayerById(turnDto.PlayerId);
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);
            var tgMetadata = player.TelegramMetadata;

            await DisplayPlayersData(lobby);

            if (character.Effect != CharacterEffect.Killed)
            {
                await _botClient.SendTextMessageAsync(
                    lobby.TelegramMetadata.ChatId,
                    string.Format(GameMessages.PlayerTurnPublicMessage, player.Name, character.CharacterBase.DisplayName),
                    parseMode: ParseMode.Html);
            }

            if (character.CharacterBase.Type == ColorType.Yellow)
            {
                await _botClient.SendTextMessageAsync(
                    lobby.TelegramMetadata.ChatId,
                    string.Format(GameMessages.CrownMessage, GameSymbols.Crown, player.Name),
                    parseMode: ParseMode.Html);
            }

            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionPerformedId);
            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.ActionErrorId);

            switch (character.Effect)
            {
                case CharacterEffect.Killed:
                    await SendActionPerformedMessage(player, GameMessages.KilledPersonalMessage);
                    await _botClient.SendTextMessageAsync(
                        lobby.TelegramMetadata.ChatId, 
                        string.Format(GameMessages.SkippedTurnMessage, player.Name, character.CharacterBase.DisplayName),
                        parseMode: ParseMode.Html);

                    await NextPlayerTurn(lobbyId);
                    return;

                case CharacterEffect.Robbed:
                    await SendActionPerformedMessage(player, string.Format(GameMessages.RobbedPersonalMessage));
                    break;

                default:
                    break;
            }

            await SendChooseResourses(lobbyId, player, turnDto.Character.CharacterBase);
        }

        protected async Task EndGame(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);
            var players = await _playerProvider.GetPlayersByLobbyId(lobbyId, loadPlacedQuarters: true);

            var chatId = lobby.TelegramMetadata.ChatId;

            // additional scores
            foreach(var player in players)
            {
                var secretHideout = _quarterFactory.GetCard(QuarterNames.SecretHideout);
                var capitol = _quarterFactory.GetCard(QuarterNames.Capitol);
                var mapRoom = _quarterFactory.GetCard(QuarterNames.MapRoom);
                var wishingWell = _quarterFactory.GetCard(QuarterNames.WishingWell);
                var monument = _quarterFactory.GetCard(QuarterNames.Monument);
                var imperialTreasury = _quarterFactory.GetCard(QuarterNames.ImperialTreasury);

                if (player.QuarterHand.Any(q => q == secretHideout.Name))
                {
                    await SpecialCardEndGameBonus(player, secretHideout);
                }

                if (player.PlacedQuarters.Any(q => q.Name == capitol.Name))
                {
                    var types = Enum.GetValues<ColorType>();
                    for (var i = 1; i < types.Length; i++)
                    {
                        var type = types[i];

                        if (player.PlacedQuarters.Where(q => q.QuarterBase.Type == type).Count()>=3)
                        {
                            await SpecialCardEndGameBonus(player, capitol);
                            break;
                        }
                    }
                }

                if (player.PlacedQuarters.Any(q => q.Name == monument.Name) && player.HasCrown)
                {
                    await SpecialCardEndGameBonus(player, monument);
                }

                if (player.PlacedQuarters.Any(q => q.Name == mapRoom.Name))
                {
                    mapRoom.BonusScore = player.QuarterHand.Count();
                    await SpecialCardEndGameBonus(player, mapRoom);
                }

                if (player.PlacedQuarters.Any(q => q.Name == wishingWell.Name))
                {
                    wishingWell.BonusScore = player.PlacedQuarters.Where(q => q.QuarterBase.Type == ColorType.Purple).Count();
                    await SpecialCardEndGameBonus(player, wishingWell);
                }

                if (player.PlacedQuarters.Any(q => q.Name == imperialTreasury.Name))
                {
                    imperialTreasury.BonusScore = player.Coins;
                    await SpecialCardEndGameBonus(player, imperialTreasury);
                }

                if (DoesHaveAllQuarterTypes(player.PlacedQuarters))
                {
                    var allTypesBonus = _gameSettings.AllTypesBonus;
                    player.Score += allTypesBonus;
                    await _botClient.SendTextMessageAsync(
                        player.TelegramMetadata.ChatId,
                        string.Format(GameMessages.AllTypesBonusMessage, allTypesBonus),
                        parseMode: ParseMode.Html);
                }
            }

            await DisplayPlayerScore(lobby, players);

            await CancelGame(chatId, lobby, players);
        }

        private async Task SpecialCardEndGameBonus(Player player, Quarter quarterName)
        {
            player.Score += quarterName.BonusScore;
            await _botClient.SendTextMessageAsync(
                player.TelegramMetadata.ChatId,
                string.Format(GameMessages.SpecialQuarterBonusMessage, quarterName.BonusScore, quarterName.DisplayName),
                parseMode: ParseMode.Html);
        }

        protected async Task CancelGame(long chatId, Lobby lobby, List<Player> players)
        {
            var message = new HttpRequestMessage(HttpMethod.Delete, _gameSettings.GameApiUrl + $"/{lobby.Id}");
            var response = await _httpClient.SendAsync(message);
            var responseMessage = await response.Content.ReadAsStringAsync();

            await _botClient.TryDeleteMessage(chatId, lobby.TelegramMetadata.LobbyInfoMessageId);

            if (response.IsSuccessStatusCode && lobby.Status != LobbyStatus.Configuring)
            {
                await DeleteMessagesForPlayers(players);
            }

            await _botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, responseMessage);
        }

        protected async Task SendActionPerformedMessage(Player player, string message)
        {
            var responseMessage = await _botClient.PutMessage(player.TelegramMetadata.ChatId, player.TelegramMetadata.ActionPerformedId, message);
            player.TelegramMetadata.ActionPerformedId = responseMessage.MessageId;
            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.ActionPerformedId);
        }

        protected async Task DisplayRemovedCharacters(string lobbyId)
        {
            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            var removed = lobby.CharacterDeck.Where(c => c.Status == CharacterStatus.Removed);
            var names = string.Join("</b> and <b>", removed.Select(r => r.CharacterBase.DisplayName));

            await _botClient.SendTextMessageAsync(
                lobby.TelegramMetadata.ChatId, 
                $"[{GameSymbols.Character}] <b>{names}</b> {GameMessages.CharactersRemovedMessage}", 
                parseMode: ParseMode.Html);
        }

        private async Task SendChooseResourses(string lobbyId, Player player, CharacterBase character)
        {

            var tgMessage = $"\n{GameMessages.GetPlayerInfoMessage(player)}\n\n{GameMessages.ChooseResourcesMessage}";

            var coinsAmount = _gameSettings.CoinsPerTurn;
            var cardsAmount = _gameSettings.QuartersPerTurn;

            if (player.PlacedQuarters.Any(q => q.Name == QuarterNames.Goldmine))
            {
                coinsAmount++;
            }

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
                var bonusCards = _gameSettings.QuartersPerTurn * 2;
                cardsAmount += bonusCards;
                additionalResourses = string.Concat(Enumerable.Repeat(GameSymbols.Card, bonusCards));
            }

            var buttons = new List<List<InlineKeyboardButton>>
            {
                new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{coinsString}{additionalResourses}",
                        $"{GameActionNames.TakeResources}_{lobbyId}_{character.Name}_{ResourceType.Coin}_{coinsAmount}"),
                    InlineKeyboardButton.WithCallbackData(
                        $"{cardsString}{additionalResourses}",
                        $"{GameActionNames.TakeResources}_{lobbyId}_{character.Name}_{ResourceType.Card}_{cardsAmount}"),
                }
            };

            var responseMessage = await _botClient.SendCharacter(
                player.TelegramMetadata.ChatId,
                character,
                tgMessage,
                new InlineKeyboardMarkup(buttons),
                usePhotoWithDescription: true);

            player.TelegramMetadata.GameActionKeyboardId = responseMessage.MessageId;

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.GameActionKeyboardId);
        }

        private async Task DisplayPlayersData(Lobby lobby)
        {
            var players = (await _playerProvider.GetPlayersByLobbyId(lobby.Id, loadPlacedQuarters: true)).OrderBy(p => p.CSOrder);

            var builder = new StringBuilder();

            foreach(var player in players)
            {
                var characters = lobby.CharacterDeck.Where(c => player.CharacterHand.Contains(c.Name));

                builder.Append(player.Name + " ");
                builder.AppendLine(GameMessages.GetPlayerCharactersInfo(characters, player, loadNames: false));
                builder.AppendLine(GameMessages.GetPlayerInfoMessage(player));

                foreach (var quarter in player.PlacedQuarters)
                {
                    builder.Append(GameMessages.GetPlacedQuarterInfo(quarter));
                    builder.AppendLine();
                }

                builder.AppendLine();
            }

            var tgMetadata = lobby.TelegramMetadata;

            await _botClient.TryDeleteMessage(tgMetadata.ChatId, tgMetadata.LobbyInfoMessageId);
            var messageId = (await _botClient.SendTextMessageAsync(tgMetadata.ChatId, builder.ToString(), parseMode: ParseMode.Html)).MessageId;

            lobby.TelegramMetadata.LobbyInfoMessageId = messageId;
            await _lobbyProvider.UpdateLobby(lobby, l => l.TelegramMetadata.LobbyInfoMessageId);
        }

        private async Task DisplayPlayerScore(Lobby lobby, IEnumerable<Player> players)
        {
            players = players.OrderByDescending(p => p.FullScore).ThenByDescending(p => p.QuarterHand.Count);
            var winner = players.First();

            var builder = new StringBuilder();

            builder.AppendLine(string.Format(GameMessages.WinnerPublicMessage, winner.Name));
            builder.AppendLine();

            var place = 1;
            foreach (var player in players)
            {
                builder.AppendLine($"{place++}. {player.Name}:");
                builder.AppendLine(GameMessages.GetPlayerInfoMessage(player));
                builder.AppendLine();
            }

            // send results to the group
            await _botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, builder.ToString());
            // send results to the winner
            await _botClient.SendTextMessageAsync(winner.TelegramMetadata.ChatId, GameMessages.WinnerPersonalMessage);
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
                messageIds.AddRange(p.TelegramMetadata.TableIds);
                messageIds.Add(p.TelegramMetadata.GameActionKeyboardId);
                messageIds.Add(p.TelegramMetadata.ActionErrorId);
                messageIds.Add(p.TelegramMetadata.ActionPerformedId);

                await _botClient.TryDeleteMessages(chatId, messageIds);

                //Deleting reply keyboard and sending final message
                await _botClient.SendTextMessageAsync(chatId, GameMessages.FarewellMessage, replyMarkup: new ReplyKeyboardRemove());
            });

            await Task.WhenAll(deleteTasks);
        }

        private bool DoesHaveAllQuarterTypes(IEnumerable<PlacedQuarter> quarters)
        {
            var types = Enum.GetValues<ColorType>();
            var hasIndulgence = quarters.Any(q => q.Name == QuarterNames.GhostNeighbourhood) 
                && quarters.Any(q => q.QuarterBase.Type == ColorType.Purple && q.Name != QuarterNames.GhostNeighbourhood);

            // starting with 1 because there is no 0 type quarters
            for (var i = 1; i < types.Length; i++)
            {
                var type = types[i];

                if (!quarters.Any(q => q.QuarterBase.Type == type))
                {
                    if (hasIndulgence)
                    {
                        hasIndulgence = false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
