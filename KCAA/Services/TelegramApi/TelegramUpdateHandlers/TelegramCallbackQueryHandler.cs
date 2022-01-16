using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Extensions;
using KCAA.Models;
using KCAA.Models.MongoDB;
using KCAA.Models.Quarters;
using KCAA.Services.Interfaces;
using KCAA.Settings.GameSettings;
using Telegram.Bot;
using Telegram.Bot.Types;
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

        private ITelegramBotClient _botClient;

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            _botClient = botClient;
            var callbackQuery = update.CallbackQuery;

            Console.WriteLine($"Receive callback query\nChat id: {callbackQuery.Message.Chat.Id}\nUsername: {callbackQuery.From.Username}\nUser id: {callbackQuery.From.Id}\n{callbackQuery.Data}");

            var data = callbackQuery.Data.Split('_');

            var action = data.First() switch
            {
                "chooseCharacter" => HandleChooseCharacter(callbackQuery.Message.Chat.Id, data),
                "takeRes" =>  HandleTakeResources(callbackQuery.Message.Chat.Id, data),
                "ga" => HandleChooseGameAction(callbackQuery.Message.Chat.Id, data),
                "endTurn" => HandleEndTurn(callbackQuery.Message.Chat.Id, data),
                GameAction.BuildQuarter => HandleBuildQuarter(callbackQuery.Message.Chat.Id, data),
                _ => Task.CompletedTask
            };
            await action;
        }

        private async Task HandleChooseCharacter(long chatId, string[] data)
        {
            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;
            var characterName = data[2];

            var character = lobby.CharacterDeck.Find(x => x.Name == characterName);

            player.CharacterHand.Add(characterName);
            player.GameActions.Add(character.CharacterBase.GameAction);
            character.Status = CharacterStatus.Selected;

            var deleteMessageTasks = player.TelegramMetadata.CardMessageIds.Select(x => _botClient.DeleteMessageAsync(chatId, x));
            await Task.WhenAll(deleteMessageTasks);
            player.TelegramMetadata.CardMessageIds.Clear();

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.CharacterDeck, lobby.CharacterDeck);

            await _botClient.SendTextMessageAsync(chatId, $"{character.CharacterBase.DisplayName} selected");

            await SendCharactertSelection(_botClient, lobby.Id);
        }

        private async Task HandleTakeResources(long chatId, string[] data)
        {
            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;

            var characterName = data[2];
            var resourceType = Enum.Parse(typeof(ResourceType), data[3]);
            var amount = int.Parse(data[4]);

            switch (resourceType)
            {
                case ResourceType.Card:
                    for (int i = 0; i < amount; i++)
                    {
                        player.QuarterHand.Add(lobby.DrawQuarter());
                    }
                    await _playerProvider.UpdatePlayer(player.Id, p => p.QuarterHand, player.QuarterHand);
                    break;

                default:
                    player.Coins += amount;
                    await _playerProvider.UpdatePlayer(player.Id, p => p.Coins, player.Coins);
                    break;
            }

            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.QuarterDeck, lobby.QuarterDeck);

            await DisplayAwailableGameActions(chatId, lobby.Id, characterName);
        }

        private async Task HandleChooseGameAction(long chatId, string[] data)
        {
            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;

            await _botClient.DeleteMessageAsync(chatId, player.TelegramMetadata.GameActionKeyboardId);

            var characterName = data[2];
            var gameAction = data[3];

            var action = gameAction switch
            {
                GameAction.BuildQuarter => SendBuildQuarterKeyboard(chatId, player, characterName),
                _ => Task.Run(() => Console.WriteLine($"Game action {gameAction} was not found"))
            };

            try
            {
                await action;

            }
            catch (ArgumentException ex)
            {
                await _botClient.SendTextMessageAsync(chatId, ex.Message);
                await DisplayAwailableGameActions(chatId, player.LobbyId, characterName);
            }
        }

        private async Task HandleBuildQuarter(long chatId, string[] data)
        {
            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;

            player.TelegramMetadata.CardMessageIds.AsParallel().WithDegreeOfParallelism(5).ForAll(async id => await _botClient.DeleteMessageAsync(chatId, id));

            var characterName = data[2];
            var quarterName = data[3];

            var quarter = _quarterFactory.GetCard(quarterName);

            player.Coins -= quarter.Cost;
            player.Score += quarter.Cost + quarter.BonusScore;
            player.QuarterHand.Remove(quarterName);
            player.PlacedQuarters.Add(new PlacedQuarter(quarterName));
            player.GameActions.Remove(GameAction.BuildQuarter);

            await _playerProvider.SavePlayer(player);

            await DisplayAwailableGameActions(chatId, lobbyId, characterName);
        }

        private async Task HandleEndTurn(long chatId, string[] data)
        {
            var lobbyId = data[1];
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;

            var characterName = data[2];

            await _botClient.DeleteMessageAsync(chatId, player.TelegramMetadata.GameActionKeyboardId);

            lobby.CharacterDeck.Find(x => x.Name == characterName).Status = CharacterStatus.SecretlyRemoved;

            await _lobbyProvider.UpdateLobby(lobbyId, x => x.CharacterDeck, lobby.CharacterDeck);

            await NextPlayerTurn(_botClient, lobbyId);
        }

        private async Task SendBuildQuarterKeyboard(long chatId, Player player, string characterName)
        {
            var quarters = player.QuarterHand.Select(x => _quarterFactory.GetCard(x)).Where(x => x.Cost <= player.Coins);

            if (!quarters.Any())
            {
                throw new ArgumentException(GameMessages.NoQuartersToAffordError);
            }

            var sendMessageTasks = quarters.Select(async x =>
            {
                var button = InlineKeyboardButton.WithCallbackData(
                    GameAction.GetActionDisplayName(GameAction.BuildQuarter), 
                    $"{GameAction.BuildQuarter}_{player.LobbyId}_{characterName}_{x.Name}");

                return await _botClient.SendQuarter(chatId, x, button);
            });
            var messageIds = (await Task.WhenAll(sendMessageTasks)).Select(m => m.MessageId);

            player.TelegramMetadata.CardMessageIds.AddRange(messageIds);
            await _playerProvider.UpdatePlayer(player.Id, p => p.TelegramMetadata, player.TelegramMetadata);
        }

        private async Task DisplayAwailableGameActions(long chatId, string lobbyId, string characterName)
        {
            (Player, Lobby) tuple;

            try
            {
                tuple = await TryGetPlayerAndLobby(chatId, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred during player or lobby retrival: {ex}");
                return;
            }

            var player = tuple.Item1;
            var lobby = tuple.Item2;

            var character = lobby.CharacterDeck.Find(x => x.Name == characterName);

            var tgMessage = GameMessages.GetPlayerTurnMessage(character.CharacterBase.DisplayName, player.Coins, player.QuarterHand.Count, player.Score)
                + "\n\n" + GameMessages.ChooseActionMessage;

            var buttons = new List<List<InlineKeyboardButton>>();
            foreach (var gameAction in player.GameActions)
            {
                if (!string.IsNullOrWhiteSpace(gameAction))
                {
                    buttons.Add(new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData(
                            GameAction.GetActionDisplayName(gameAction), 
                            $"ga_{lobby.Id}_{characterName}_{gameAction}")
                    });
                }
            }
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    GameAction.GetActionDisplayName(GameAction.EndTurn), 
                    $"endTurn_{lobby.Id}_{characterName}")
            });

            var message = await _botClient.SendCharacterWithMessage(chatId, character.CharacterBase, tgMessage, buttons);

            player.TelegramMetadata.GameActionKeyboardId = message.MessageId;

            await _playerProvider.UpdatePlayer(player.Id, p => p.TelegramMetadata, player.TelegramMetadata);
        }

        private async Task<(Player, Lobby)> TryGetPlayerAndLobby(long chatId, string lobbyId)
        {
            var player = await _playerProvider.GetPlayerByChatId(chatId);

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
