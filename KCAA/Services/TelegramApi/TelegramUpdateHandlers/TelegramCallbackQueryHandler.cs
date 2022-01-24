using System;
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
                "chooseCharacter" => HandleChooseCharacter(chatId, player, lobby, characterName),
                "takeRes" =>  HandleTakeResources(chatId, player, lobby, characterName, data[3], data[4]),
                "ga" => HandleChooseGameAction(callbackQuery.Message.Chat, player, lobby, characterName, data[3], data.ElementAtOrDefault(4)),
                "endTurn" => HandleEndTurn(chatId, player, lobby, characterName),
                "myHandClose" => HandleCloseAction(chatId),
                GameAction.Cancel => HandleCancelAction(chatId, player, lobby, characterName),
                GameAction.Done => HandleDoneAction(chatId, player, lobby, characterName, data[3]),
                GameAction.BuildQuarter => HandleBuildQuarter(chatId, player, lobby, characterName, data[3]),
                GameAction.Kill or GameAction.Steal => HandleCharacterEffect(chatId, player, lobby, characterName, command, data[3]),
                GameAction.ExchangeHands => HandleExchangeHands(chatId, player, lobby, characterName, data[3]),
                GameAction.DiscardQuarters => HandleDiscard(callbackQuery.Message, player, lobby, characterName, data[3]),
                GameAction.DestroyQuarters => HandleDestroyQuarter(player, lobby, command, characterName, data[3], data.ElementAtOrDefault(4)),
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
                GameAction.BuildQuarter => SendBuildQuarterKeyboard(player, characterName, gameAction),
                GameAction.TakeRevenue => HandleTakeRevenue(chatId, player, characterName, gameAction, additionalData),
                GameAction.Kill => SendCharacterKeyboard(chatId, lobby, player, characterName, gameAction),
                GameAction.Steal => SendCharacterKeyboard(chatId, lobby, player, characterName, gameAction),
                GameAction.ExchangeHands => SendPlayerKeyboard(chatId, lobby, player, characterName, gameAction),
                GameAction.DiscardQuarters => SendDiscardQuarterKeyboard(player, characterName, gameAction),
                GameAction.DestroyQuarters => SendPlayerKeyboard(chatId, lobby, player, characterName, gameAction),
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
                var message = await _botClient.PutMessage(
                    chatId,
                    player.TelegramMetadata.ActionErrorId, 
                    string.Format(GameMessages.AlreadyPlacedQuarterError, placedQuarter.QuarterBase.DisplayName));

                player.TelegramMetadata.ActionErrorId = message.MessageId;
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

            //If player completed the city
            if (player.PlacedQuarters.Count == _gameSettings.QuartersToWin)
            {
                player.Score += _gameSettings.FullBuildBonus;

                await _botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, string.Format(GameMessages.CityBuiltMessage, player.Name));
            }

            if (character.BuiltQuarters == character.CharacterBase.BuildingCapacity)
            {
                player.GameActions.Remove(GameAction.BuildQuarter);
            }

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby, l => l.CharacterDeck);

            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleCharacterEffect(long chatId, Player player, Lobby lobby, string characterName, string gameAction, string targetName)
        {
            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);

            lobby.CharacterDeck.Find(x => x.Name == targetName).Effect = gameAction switch
            {
                GameAction.Kill => CharacterEffect.Killed,
                GameAction.Steal => CharacterEffect.Robbed,
                _ => CharacterEffect.None
            };

            player.GameActions.Remove(gameAction);

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby, x => x.CharacterDeck);

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
            player.GameActions.Remove($"{GameAction.ExchangeHands}|{GameAction.DiscardQuarters}");
            await _playerProvider.UpdatePlayer(player, p => p.GameActions);

            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.CardMessageIds);
            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleCloseAction(long chatId)
        {
            var player = await _playerProvider.GetPlayerByChatId(chatId);
            await _botClient.TryDeleteMessages(chatId, player.TelegramMetadata.MyHandIds);
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
            player.GameActions.Remove($"{GameAction.ExchangeHands}|{GameAction.DiscardQuarters}");

            await SendActionPerformedMessage(target, string.Format(GameMessages.ExchangedMessage, player.Name));

            await _playerProvider.UpdatePlayer(target, p => p.QuarterHand);
            await _playerProvider.UpdatePlayer(player, p => p.QuarterHand);
            await _playerProvider.UpdatePlayer(player, p => p.GameActions);

            await DisplayAvailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleDiscard(Message message, Player player, Lobby lobby, string characterName, string quarterName)
        {
            var chatId = message.Chat.Id;
            var messageId = message.MessageId;
            var gameAction = $"{GameAction.ExchangeHands}|{GameAction.DiscardQuarters}";

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
            var doneButton = InlineKeyboardButton.WithCallbackData(GameSymbols.Done, $"{GameAction.Done}_{player.LobbyId}_{characterName}_{gameAction}");
            
            var doneButtonId = (await _botClient.PutMessage(
                chatId, 
                cancelButtonId, 
                GameAction.GetActionDisplayName(GameAction.Done), 
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

            await _playerProvider.UpdatePlayer(player, p => p.Coins);
            await _playerProvider.UpdatePlayer(target, p => p.Score);
            await _playerProvider.UpdatePlayer(target, p => p.PlacedQuarters);

            await SendActionPerformedMessage(target, string.Format(GameMessages.DestroyedMessage, quarter.QuarterBase.DisplayName, player.Name));

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
                    GameAction.GetActionDisplayName(gameAction),
                    $"{callbackData}_{x.Name}");

                return await _botClient.SendQuarter(chatId, x, buildButton);
            });

            var cancelButton = InlineKeyboardButton.WithCallbackData(GameSymbols.Cancel, $"{GameAction.Cancel}_{player.LobbyId}_{characterName}");

            var messageIds = (await Task.WhenAll(sendQuartersTasks)).Select(m => m.MessageId);
            var cancelMessageId = (await _botClient.SendTextMessageAsync(
                chatId,
                GameAction.GetActionDisplayName(GameAction.Cancel),
                replyMarkup: new InlineKeyboardMarkup(cancelButton))).MessageId;

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

            var sendMessageTasks = characterOptions.Select(async x =>
            {
                var btnAction = new List<List<InlineKeyboardButton>>
                {
                    new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData(
                        GameAction.GetActionDisplayName(gameAction),
                        $"{gameAction}_{player.LobbyId}_{characterName}_{x.Name}")
                    }
                };
                return await _botClient.SendCharacter(chatId, x.CharacterBase, "", new InlineKeyboardMarkup(btnAction));
            });

            var messageIds = (await Task.WhenAll(sendMessageTasks)).Select(m => m.MessageId);
            player.TelegramMetadata.CardMessageIds.Clear();
            player.TelegramMetadata.CardMessageIds.AddRange(messageIds);

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);
        }

        private async Task SendPlayerKeyboard(long chatId, Lobby lobby, Player player, string characterName, string gameAction)
        {
            await _botClient.TryDeleteMessage(chatId, player.TelegramMetadata.GameActionKeyboardId);

            var players = (await _playerProvider.GetPlayersByLobbyId(player.LobbyId, loadPlacedQuarters: true)).Where(p => p.Id != player.Id).ToList();
            
            var characters = lobby.CharacterDeck.Where(c => player.CharacterHand.Contains(c.Name));
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

            var sendPlayersTasks = players.Select(p =>
            {
                var builder = new StringBuilder();
                builder.Append(p.Name + " ");
                builder.AppendLine(GameMessages.GetPlayerCharactersInfo(characters, p));
                builder.AppendLine(GameMessages.GetPlayerInfoMessage(p));
                Console.WriteLine($"{gameAction}_{player.LobbyId}_{characterName}_{p.Id}");
                var button = InlineKeyboardButton.WithCallbackData(GameAction.GetActionDisplayName(gameAction), $"{gameAction}_{player.LobbyId}_{characterName}_{p.TelegramMetadata.ChatId}");

                return _botClient.SendTextMessageAsync(chatId, builder.ToString(), parseMode: ParseMode.Html,replyMarkup: new InlineKeyboardMarkup(button));
            });

            var cancelButton = InlineKeyboardButton.WithCallbackData(GameSymbols.Cancel, $"{GameAction.Cancel}_{player.LobbyId}_{characterName}");

            var messageIds = (await Task.WhenAll(sendPlayersTasks)).Select(m => m.MessageId);
            var cancelMessageId = (await _botClient.SendTextMessageAsync(
                chatId,
                GameAction.GetActionDisplayName(GameAction.Cancel),
                replyMarkup: new InlineKeyboardMarkup(cancelButton))).MessageId;

            player.TelegramMetadata.CardMessageIds = messageIds.ToList();
            player.TelegramMetadata.CardMessageIds.Add(cancelMessageId);

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.CardMessageIds);
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
                    GameAction.GetActionDisplayName(GameAction.EndTurn),
                    $"endTurn_{lobby.Id}_{character.Name}")
            });

            return buttons;
        }

        private InlineKeyboardButton GetActionButton(string action, Player player, Character character)
        {
            var actionDisplayName = GameAction.GetActionDisplayName(action);
            var callbackData = $"ga_{player.LobbyId}_{character.Name}_{action}";

            switch (action)
            {
                case GameAction.TakeRevenue:

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

                case GameAction.BuildQuarter or GameAction.DiscardQuarters:

                    //No need to display action with no outcome
                    if (!player.QuarterHand.Any())
                    {
                        return null;
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
