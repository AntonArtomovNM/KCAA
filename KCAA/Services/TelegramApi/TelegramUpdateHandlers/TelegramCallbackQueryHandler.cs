﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KCAA.Extensions;
using KCAA.Helpers;
using KCAA.Models;
using KCAA.Models.Characters;
using KCAA.Models.MongoDB;
using KCAA.Models.Quarters;
using KCAA.Services.Interfaces;
using KCAA.Settings.GameSettings;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramCallbackQueryHandler : TelegramGameHandlerBase, ITelegramUpdateHandler
    {
        private readonly ICardFactory<Quarter> _quarterFactory;

        public TelegramCallbackQueryHandler(
            ILobbyProvider lobbyProvider,
            IPlayerProvider playerProvider,
            GameSettings gameSettings, 
            ICardFactory<Quarter> quarterFactory)
            : base(playerProvider, lobbyProvider, gameSettings)
        {
            _quarterFactory = quarterFactory;
        }

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            _botClient = botClient;
            var callbackQuery = update.CallbackQuery;
            var chatId = callbackQuery.Message.Chat.Id;

            Console.WriteLine($"Receive callback query\nChat id: {chatId}\nUsername: {callbackQuery.From.Username}\nUser id: {callbackQuery.From.Id}\n{callbackQuery.Data}");

            var data = callbackQuery.Data.Split('_');

            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{GameMessages.LobbyOrPlayerNotFoundError}: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;

            var command = data.First();
            var characterName = data.ElementAtOrDefault(2);
            

            var action = command switch
            {
                "ga" => HandleChooseGameAction(callbackQuery.Message.Chat, player, lobby, characterName, data[3], data.ElementAtOrDefault(4)),
                GameActionNames.ChooseCharacter => HandleChooseCharacter(chatId, player, lobby, characterName),
                GameActionNames.TakeResources =>  HandleTakeResources(chatId, player, lobby, characterName, data[3], data[4]),
                GameActionNames.EndTurn => HandleEndTurn(chatId, player, lobby, characterName),
                GameActionNames.Close => HandleCloseAction(chatId, data[2]),
                GameActionNames.Cancel => HandleCancelAction(chatId, player, lobby, characterName),
                GameActionNames.Done => HandleDoneAction(chatId, player, lobby, characterName, data[3]),
                GameActionNames.BuildQuarter => HandleBuildQuarter(chatId, player, lobby, characterName, data[3]),
                GameActionNames.Kill or GameActionNames.Steal => HandleCharacterEffect(chatId, player, lobby, characterName, command, data[3]),
                GameActionNames.ExchangeHands => HandleExchangeHands(chatId, player, lobby, characterName, data[3]),
                GameActionNames.DiscardQuarters => HandleDiscard(callbackQuery.Message, player, lobby, characterName, data[3]),
                GameActionNames.DestroyQuarters => HandleDestroyQuarter(player, lobby, command, characterName, data[3], data.ElementAtOrDefault(4)),
                _ => Task.CompletedTask
            };
            await action;
        }

        private async Task HandleChooseCharacter(long chatId, Player player, Lobby lobby, string characterName)
        {
            var character = lobby.CharacterDeck.Find(x => x.Name == characterName);

            player.CharacterHand.Add(characterName);
            
            character.Status = CharacterStatus.Selected;

            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);
            player.TelegramMetadata.CardMessageIds.Clear();

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby, x => x.CharacterDeck);

            await NextCharactertSelection(lobby.Id);
        }

        private async Task HandleTakeResources(long chatId, Player player, Lobby lobby, string characterName, string typeStr, string amountStr)
        {
            var resourceType = Enum.Parse(typeof(ResourceType), typeStr);
            var amount = int.Parse(amountStr);

            switch (resourceType)
            {
                case ResourceType.Card:
                    for (int i = 0; i < amount; i++)
                    {
                        player.QuarterHand.Add(lobby.DrawQuarter());
                    }

                    //TODO: Display new quarter

                    if (characterName == CharacterNames.Merchant)
                    {
                        player.Coins += _gameSettings.CoinsPerTurn / 2;
                    }
                    break;

                default:
                    player.Coins += amount;

                    if (characterName == CharacterNames.Architect)
                    {
                        for (int i = 0; i < _gameSettings.QuertersPerTurn * 2; i++)
                        {
                            player.QuarterHand.Add(lobby.DrawQuarter());
                        }
                    }
                    break;
            }

            await _playerProvider.UpdatePlayer(player, p => p.QuarterHand);
            await _playerProvider.UpdatePlayer(player, p => p.Coins);

            await _lobbyProvider.UpdateLobby(lobby, x => x.QuarterDeck);

            await _botClient.TryDeleteMessage(chatId, player.TelegramMetadata.GameActionKeyboardId);
            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleChooseGameAction(Chat chat, Player player, Lobby lobby, string characterName, string gameAction, string additionalData)
        {
            var chatId = chat.Id;

            var action = gameAction switch
            {
                GameActionNames.BuildQuarter => SendBuildQuarterKeyboard(player, characterName, gameAction),
                GameActionNames.TakeRevenue => HandleTakeRevenue(chatId, player, characterName, gameAction, additionalData),
                GameActionNames.Kill => SendCharacterKeyboard(chatId, lobby, player, characterName, gameAction),
                GameActionNames.Steal => SendCharacterKeyboard(chatId, lobby, player, characterName, gameAction),
                GameActionNames.ExchangeHands => SendPlayerKeyboard(chatId, lobby, player, characterName, gameAction),
                GameActionNames.DiscardQuarters => SendDiscardQuarterKeyboard(player, characterName, gameAction),
                GameActionNames.DestroyQuarters => SendPlayerKeyboard(chatId, lobby, player, characterName, gameAction),
                _ => Task.Run(() => Console.WriteLine($"Game action {gameAction} was not found"))
            };

            try
            {
                await action;
            }
            catch (ArgumentException ex)
            {
                var message = await _botClient.PutMessage(chatId, player.TelegramMetadata.ActionErrorId, ex.Message);
                player.TelegramMetadata.ActionErrorId = message.MessageId;
                await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.ActionErrorId);
            }
        }

        private async Task HandleBuildQuarter(long chatId, Player player, Lobby lobby, string characterName, string quarterName)
        {
            var placedQuarter = player.PlacedQuarters.FirstOrDefault(q => q.Name == quarterName);

            if (placedQuarter != null)
            {
                var tgMessage = await _botClient.PutMessage(
                    chatId,
                    player.TelegramMetadata.ActionErrorId, 
                    string.Format(GameMessages.AlreadyPlacedQuarterError, placedQuarter.QuarterBase.DisplayName));

                player.TelegramMetadata.ActionErrorId = tgMessage.MessageId;
                await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.ActionErrorId);

                return;
            }

            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);

            var quarter = _quarterFactory.GetCard(quarterName);
            var character = lobby.CharacterDeck.Find(c => c.Name == characterName);
            character.BuiltQuarters++;

            player.Coins -= quarter.Cost;
            player.Score += quarter.Cost + quarter.BonusScore;
            player.QuarterHand.Remove(quarterName);
            player.PlacedQuarters.Add(new PlacedQuarter(quarterName));

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendFormat(GameMessages.QuarterBuiltMessage, GameSymbols.PlacedQuarter, player.Name, quarter.DisplayName);
            messageBuilder.AppendLine(" " + GameSymbols.GetColorByType(quarter.Type));
            messageBuilder.AppendLine(GameSymbols.GetCostInCoins(quarter.Cost));

            if (player.PlacedQuarters.Count == _gameSettings.QuartersToWin - 1)
            {
                messageBuilder.AppendLine(GameMessages.OneQuarterLeftMessage);
            }

            await _botClient.SendTextMessageAsync(
                lobby.TelegramMetadata.ChatId,
                messageBuilder.ToString(),
                parseMode: ParseMode.Html);

            //If player completed the city
            if (player.PlacedQuarters.Count == _gameSettings.QuartersToWin)
            {
                var extraMessage = "";
                var bonusPoints = _gameSettings.FullBuildBonus;

                var finishedPlayers = (await _playerProvider.GetPlayersByLobbyId(lobby.Id, loadPlacedQuarters: true))
                    .Where(p => p.PlacedQuarters.Count >= _gameSettings.QuartersToWin && p.Id != player.Id);

                if (!finishedPlayers.Any())
                {
                    bonusPoints *= 2;
                    extraMessage = " first";
                }

                player.Score += bonusPoints;

                await _botClient.SendTextMessageAsync(
                    lobby.TelegramMetadata.ChatId,
                    string.Format(GameMessages.CityBuiltPublicMessage, player.Name, extraMessage),
                    parseMode: ParseMode.Html);

                await _botClient.SendTextMessageAsync(
                    chatId,
                    string.Format(GameMessages.CityBuiltPersonalMessage, bonusPoints, extraMessage),
                    parseMode: ParseMode.Html);
            }

            if (character.BuiltQuarters == character.CharacterBase.BuildingCapacity)
            {
                player.GameActions.Remove(GameActionNames.BuildQuarter);
            }

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby, l => l.CharacterDeck);

            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleCharacterEffect(long chatId, Player player, Lobby lobby, string characterName, string gameAction, string targetName)
        {
            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);

            CharacterEffect effect;
            string message = string.Empty;
            string symbol = string.Empty;

            switch(gameAction)
            {
                case GameActionNames.Kill:
                    effect = CharacterEffect.Killed;
                    message = GameMessages.KilledPublicMessage;
                    symbol = GameSymbols.Killed;
                    break;

                case GameActionNames.Steal:
                    effect = CharacterEffect.Robbed;
                    message = GameMessages.RobbedPublicMessage;
                    symbol = GameSymbols.Robbed;
                    break;

                default:
                    return;
            };

            var character = lobby.CharacterDeck.Find(x => x.Name == targetName);
            character.Effect = effect;

            player.GameActions.Remove(gameAction);

            await _playerProvider.UpdatePlayer(player, p => p.GameActions);
            await _lobbyProvider.UpdateLobby(lobby, x => x.CharacterDeck);

            await _botClient.SendTextMessageAsync(
                lobby.TelegramMetadata.ChatId,
                string.Format(message, symbol, player.Name, character.CharacterBase.DisplayName),
                parseMode: ParseMode.Html);

            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleEndTurn(long chatId, Player player, Lobby lobby, string characterName)
        {
            await _botClient.TryDeleteMessage(chatId, player.TelegramMetadata.GameActionKeyboardId);

            lobby.CharacterDeck.Find(x => x.Name == characterName).Status = CharacterStatus.SecretlyRemoved;

            await _lobbyProvider.UpdateLobby(lobby, x => x.CharacterDeck);

            await NextPlayerTurn(lobby.Id);
        }

        private async Task HandleCancelAction(long chatId, Player player, Lobby lobby, string characterName)
        {
            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);
            player.TelegramMetadata.CardMessageIds.Clear();
            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);

            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleDoneAction(long chatId, Player player, Lobby lobby, string characterName, string gameAction)
        {
            player.GameActions.Remove($"{GameActionNames.ExchangeHands}|{GameActionNames.DiscardQuarters}");
            await _playerProvider.UpdatePlayer(player, p => p.GameActions);

            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);
            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleCloseAction(long chatId, string messageType)
        {
            var player = await _playerProvider.GetPlayerByChatId(chatId);

            var idsToDelete = new List<int>();

            if (messageType == "Table")
            {
                idsToDelete.AddRange(player.TelegramMetadata.TableIds);
            }
            else
            {
                idsToDelete.AddRange(player.TelegramMetadata.MyHandIds);
            }

            await _botClient.TryDeleteMessages(chatId, idsToDelete);
        }

        private async Task HandleTakeRevenue(long chatId, Player player, string characterName, string gameAction, string revenue)
        {
            player.GameActions.Remove(gameAction);

            if (int.TryParse(revenue, out var revenueAmount))
            {
                player.Coins += revenueAmount;

                await _playerProvider.UpdatePlayer(player, p => p.Coins);
            }

            await _playerProvider.UpdatePlayer(player, p => p.GameActions);

            await _botClient.TryDeleteMessage(chatId, player.TelegramMetadata.GameActionKeyboardId);
            await DisplayAvailableGameActions(chatId, player.LobbyId, characterName);
        }

        private async Task HandleExchangeHands(long chatId, Player player, Lobby lobby, string characterName, string targetIdStr)
        {
            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);

            var targetChatId = long.Parse(targetIdStr);

            var target = await _playerProvider.GetPlayerByChatId(targetChatId);

            var playersHand = player.QuarterHand;
            player.QuarterHand = target.QuarterHand;
            target.QuarterHand = playersHand;
            player.GameActions.Remove($"{GameActionNames.ExchangeHands}|{GameActionNames.DiscardQuarters}");

            await SendActionPerformedMessage(target, string.Format(GameMessages.ExchangedPersonalMessage, player.Name));

            await _botClient.SendTextMessageAsync(
                lobby.TelegramMetadata.ChatId, 
                string.Format(GameMessages.ExchangedPublicMessage, GameSymbols.Exchange, player.Name, target.Name), 
                parseMode: ParseMode.Html);

            await _playerProvider.UpdatePlayer(target, p => p.QuarterHand);
            await _playerProvider.UpdatePlayer(player, p => p.QuarterHand);
            await _playerProvider.UpdatePlayer(player, p => p.GameActions);

            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleDiscard(Message message, Player player, Lobby lobby, string characterName, string quarterName)
        {
            var chatId = message.Chat.Id;
            var messageId = message.MessageId;
            var gameAction = $"{GameActionNames.ExchangeHands}|{GameActionNames.DiscardQuarters}";

            player.QuarterHand.Remove(quarterName);
            player.QuarterHand.Add(lobby.DrawQuarter());
            player.TelegramMetadata.CardMessageIds.Remove(messageId);

            await _playerProvider.UpdatePlayer(player, p => p.QuarterHand);

            //Removing discarded quarter message
            await _botClient.TryDeleteMessage(chatId, messageId);

            //If there is only done/cancel button left, we should end the action
            if (player.TelegramMetadata.CardMessageIds.Count == 1)
            {
                await HandleDoneAction(chatId, player, lobby, characterName, gameAction);
                return;
            }

            var cancelButtonId = player.TelegramMetadata.CardMessageIds.Last();
            var doneButton = InlineKeyboardButton.WithCallbackData(GameSymbols.Done, $"{GameActionNames.Done}_{player.LobbyId}_{characterName}_{gameAction}");
            
            var doneButtonId = (await _botClient.PutMessage(
                chatId, 
                cancelButtonId, 
                GameActionNames.GetActionDisplayName(GameActionNames.Done), 
                new InlineKeyboardMarkup(doneButton))).MessageId;

            player.TelegramMetadata.CardMessageIds.Remove(cancelButtonId);
            player.TelegramMetadata.CardMessageIds.Add(doneButtonId);
            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);
        }

        private async Task HandleDestroyQuarter(
            Player player,
            Lobby lobby,
            string gameAction,
            string characterName,
            string targetIdStr,
            string quarterName)
        {
            var chatId = player.TelegramMetadata.ChatId;
            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);

            var targetChatId = long.Parse(targetIdStr);
            var target = await _playerProvider.GetPlayerByChatId(targetChatId, loadPlacedQuarters: true);

            //If no quarter was selected yet, send quarters
            if (string.IsNullOrWhiteSpace(quarterName))
            {
                var quarters = target.PlacedQuarters.Where(q => q.QuarterBase.Cost <= player.Coins + 1).Select(q => q.QuarterBase);
                await SendQuartersKeyboard(player, characterName, gameAction, $"{gameAction}_{player.LobbyId}_{characterName}_{targetIdStr}", quarters);
                return;
            }

            //If quarter is already selected, destroy it
            var quarter = target.PlacedQuarters.Find(q => q.Name == quarterName);

            target.PlacedQuarters.Remove(quarter);
            target.Score -= quarter.QuarterBase.Cost + quarter.BonusScore;

            player.Coins -= quarter.QuarterBase.Cost - 1;
            player.GameActions.Remove(GameActionNames.DestroyQuarters);

            await _playerProvider.UpdatePlayer(player, p => p.Coins);
            await _playerProvider.UpdatePlayer(player, p => p.GameActions);
            await _playerProvider.UpdatePlayer(target, p => p.Score);
            await _playerProvider.UpdatePlayer(target, p => p.PlacedQuarters);

            var quarterStats = $@"{quarter.QuarterBase.DisplayName} {GameSymbols.GetColorByType(quarter.QuarterBase.Type)}
{GameSymbols.GetCostInCoins(quarter.QuarterBase.Cost)}";

            await SendActionPerformedMessage(target, string.Format(GameMessages.DestroyedPersonalMessage, player.Name, quarterStats));

            await _botClient.SendTextMessageAsync(
                lobby.TelegramMetadata.ChatId, 
                string.Format(GameMessages.DestroyedPublicMessage, GameSymbols.Destroy, player.Name, target.Name, quarterStats),
                parseMode: ParseMode.Html);

            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task SendBuildQuarterKeyboard(Player player, string characterName, string gameAction)
        {
            var quarters = player.QuarterHand.Select(x => _quarterFactory.GetCard(x)).Where(x => x.Cost <= player.Coins);

            if (!quarters.Any())
            {
                throw new ArgumentException(GameMessages.NoQuartersToAffordError);
            }

            await SendQuartersKeyboard(player, characterName, gameAction, $"{gameAction}_{player.LobbyId}_{characterName}", quarters);
        }

        private async Task SendDiscardQuarterKeyboard(Player player, string characterName, string gameAction)
        {
            var quarters = player.QuarterHand.Select(x => _quarterFactory.GetCard(x));

            await SendQuartersKeyboard(player, characterName, gameAction, $"{gameAction}_{player.LobbyId}_{characterName}", quarters);
        }

        private async Task SendQuartersKeyboard(Player player, string characterName, string gameAction, string callbackData, IEnumerable<Quarter> quarters)
        {
            var chatId = player.TelegramMetadata.ChatId;

            await _botClient.TryDeleteMessage(chatId, player.TelegramMetadata.GameActionKeyboardId);

            var sendQuartersTasks = quarters.Select(async x =>
            {
                var buildButton = InlineKeyboardButton.WithCallbackData(
                    GameActionNames.GetActionDisplayName(gameAction),
                    $"{callbackData}_{x.Name}");

                return await _botClient.SendQuarter(chatId, x, buildButton);
            });

            var messageIds = (await Task.WhenAll(sendQuartersTasks)).Select(m => m.MessageId);
            var cancelMessageId = await SendCancelButton(chatId, player.LobbyId, characterName);

            player.TelegramMetadata.CardMessageIds.Clear();
            player.TelegramMetadata.CardMessageIds.AddRange(messageIds);
            player.TelegramMetadata.CardMessageIds.Add(cancelMessageId);

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);
        }

        private async Task SendCharacterKeyboard(long chatId, Lobby lobby, Player player, string characterName, string gameAction)
        {
            await _botClient.TryDeleteMessage(chatId, player.TelegramMetadata.GameActionKeyboardId);

            var currentCharacter = lobby.CharacterDeck.Find(c => c.Name == characterName).CharacterBase;
            var characterOptions = lobby.CharacterDeck.Where(
                x => x.CharacterBase.Order > currentCharacter.Order &&
                x.Status != CharacterStatus.Removed &&
                !player.CharacterHand.Contains(x.Name)).ToList();

            if (characterName == CharacterNames.Thief)
            {
                characterOptions.RemoveAll(c => c.Name == CharacterNames.Beggar);
            }

            player.TelegramMetadata.CardMessageIds.Clear();

            foreach(var character in characterOptions)
            {
                var btnAction = new List<List<InlineKeyboardButton>>
                {
                    new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData(
                        GameActionNames.GetActionDisplayName(gameAction),
                        $"{gameAction}_{player.LobbyId}_{characterName}_{character.Name}")
                    }
                };
                var messageId = (await _botClient.SendCharacter(chatId, character.CharacterBase, "", new InlineKeyboardMarkup(btnAction))).MessageId;
                player.TelegramMetadata.CardMessageIds.Add(messageId);
            }

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);
        }

        private async Task SendPlayerKeyboard(long chatId, Lobby lobby, Player player, string characterName, string gameAction)
        {
            await _botClient.TryDeleteMessage(chatId, player.TelegramMetadata.GameActionKeyboardId);

            var players = (await _playerProvider.GetPlayersByLobbyId(player.LobbyId, loadPlacedQuarters: true)).Where(p => p.Id != player.Id).ToList();
            
            var characters = lobby.CharacterDeck;
            var character = characters.FirstOrDefault(c => c.Name == characterName).CharacterBase;

            //If it's placed quarters related ability
            if (character.Type == ColorType.Red)
            {
                Predicate<Player> characterSpecificCheck = character.Name switch
                {
                    CharacterNames.Warlord => (p => !p.PlacedQuarters.Any(q => q.QuarterBase.Cost <= player.Coins + 1)),
                    _ => (_ => false)
                };

                players.RemoveAll(p => p.PlacedQuarters.Count >= _gameSettings.QuartersToWin || p.CharacterHand.Contains(CharacterNames.Bishop) || characterSpecificCheck(p));
            }

            if (!players.Any())
            {
                var message = await _botClient.PutMessage(chatId, player.TelegramMetadata.ActionErrorId, GameMessages.NoPlayersForActionError);
                player.TelegramMetadata.ActionErrorId = message.MessageId;
                await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.ActionErrorId);

                await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
                return;
            }

            var sendPlayersTasks = players.Select(async p =>
            {
                var playerCharacters = characters.Where(c => p.CharacterHand.Contains(c.Name));

                var builder = new StringBuilder();
                builder.Append(p.Name + " ");
                builder.AppendLine(GameMessages.GetPlayerCharactersInfo(playerCharacters, p, loadNames: false));
                builder.AppendLine(GameMessages.GetPlayerInfoMessage(p));
                Console.WriteLine($"{gameAction}_{player.LobbyId}_{characterName}_{p.Id}");
                var button = InlineKeyboardButton.WithCallbackData(GameActionNames.GetActionDisplayName(gameAction), $"{gameAction}_{player.LobbyId}_{characterName}_{p.TelegramMetadata.ChatId}");

                return await _botClient.SendTextMessageAsync(chatId, builder.ToString(), parseMode: ParseMode.Html,replyMarkup: new InlineKeyboardMarkup(button));
            });

            var messageIds = (await Task.WhenAll(sendPlayersTasks)).Select(m => m.MessageId);
            var cancelMessageId = await SendCancelButton(chatId, player.LobbyId, characterName);

            player.TelegramMetadata.CardMessageIds = messageIds.ToList();
            player.TelegramMetadata.CardMessageIds.Add(cancelMessageId);

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);
        }

        private async Task<int> SendCancelButton(long chatId, string lobbyId, string characterName)
        {
            var cancelButton = InlineKeyboardButton.WithCallbackData(GameSymbols.Cancel, $"{GameActionNames.Cancel}_{lobbyId}_{characterName}");

            return (await _botClient.SendTextMessageAsync(
                chatId,
                GameActionNames.GetActionDisplayName(GameActionNames.Cancel),
                replyMarkup: new InlineKeyboardMarkup(cancelButton))).MessageId;
        }

        private async Task DisplayAvailableGameActions(long chatId, string lobbyId, string characterName)
        {
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{GameMessages.LobbyOrPlayerNotFoundError}: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;

            var character = lobby.CharacterDeck.Find(x => x.Name == characterName);

            var tgMessage = $"\n{GameMessages.GetPlayerInfoMessage(player)}\n\n{GameMessages.ChooseActionMessage}";

            var buttons = GetGameActionButtons(character, player, lobby);

            var message = await _botClient.SendCharacter(chatId, character.CharacterBase, tgMessage, new InlineKeyboardMarkup(buttons));
            player.TelegramMetadata.GameActionKeyboardId = message.MessageId;
            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.GameActionKeyboardId);
        }

        private List<List<InlineKeyboardButton>> GetGameActionButtons(Character character, Player player, Lobby lobby)
        {
            var buttons = new List<List<InlineKeyboardButton>>();

            foreach (var gameAction in player.GameActions)
            {
                if (string.IsNullOrWhiteSpace(gameAction))
                {
                    continue;
                }

                var actions = gameAction.Split('|');
                var buttonRow = actions.Select(a => GetActionButton(a, player, character)).Where(b => b != null).ToList();

                buttons.Add(buttonRow);
            }
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    GameActionNames.GetActionDisplayName(GameActionNames.EndTurn),
                    $"{GameActionNames.EndTurn}_{lobby.Id}_{character.Name}")
            });

            return buttons;
        }

        private InlineKeyboardButton GetActionButton(string action, Player player, Character character)
        {
            var actionDisplayName = GameActionNames.GetActionDisplayName(action);
            var callbackData = $"ga_{player.LobbyId}_{character.Name}_{action}";

            switch (action)
            {
                case GameActionNames.TakeRevenue:

                    int revenueAmount = 0;

                    if (character.CharacterBase.Type != ColorType.None)
                    {
                        revenueAmount = player.PlacedQuarters.Where(q => q.QuarterBase.Type == character.CharacterBase.Type).Count();
                    }

                    else if (character.Name == CharacterNames.Beggar)
                    {
                        revenueAmount = player.PlacedQuarters.Where(q => q.QuarterBase.Cost == 1).Count();
                    }

                    //No need to display action with no outcome
                    if (revenueAmount == 0)
                    {
                        return null;
                    }

                    actionDisplayName += $" ({revenueAmount})";
                    callbackData += $"_{revenueAmount}";
                    break;

                case GameActionNames.BuildQuarter or GameActionNames.DiscardQuarters:

                    //No need to display action with no outcome
                    if (!player.QuarterHand.Any())
                    {
                        return null;
                    }

                    var quartersToBuild = character.CharacterBase.BuildingCapacity - character.BuiltQuarters;
                    if (quartersToBuild > 1)
                    {
                        actionDisplayName += $" ({quartersToBuild})";
                    }

                    break;

                default:
                    break;
            }

            return InlineKeyboardButton.WithCallbackData(actionDisplayName, callbackData);
        }

        private async Task<(Player, Lobby)> TryGetPlayerAndLobby(long chatId, string lobbyId)
        {
            var player = await _playerProvider.GetPlayerByChatId(chatId, loadPlacedQuarters: true);

            if (player == null)
            {
                throw new KeyNotFoundException($"Player with chat id {chatId} not found");
            }

            var lobby = await _lobbyProvider.GetLobbyById(lobbyId);

            if (lobby == null)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.IdrakError);

                throw new KeyNotFoundException($"Lobby with id {lobbyId} not found");
            }

            return (player, lobby);
        }
    }
}
