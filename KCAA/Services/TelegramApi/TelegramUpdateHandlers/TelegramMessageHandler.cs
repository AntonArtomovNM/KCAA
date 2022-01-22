using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using KCAA.Extensions;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using KCAA.Settings;
using KCAA.Settings.GameSettings;
using KCAA.Models.Quarters;
using KCAA.Models.Characters;
using System.Text;
using System.Net.Http;
using KCAA.Helpers;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramMessageHandler : TelegramGameHandlerBase, ITelegramUpdateHandler
    {
        private readonly ICardFactory<Quarter> _quarterFactory;
        private readonly ICardFactory<CharacterBase> _characterFactory;
        private readonly TelegramSettings _telegramSettings;
   

        private ITelegramBotClient _botClient;

        public TelegramMessageHandler(
            ILobbyProvider lobbyProvider, 
            IPlayerProvider playerProvider, 
            TelegramSettings telegramSettings,
            GameSettings gameSettings,
            ICardFactory<Quarter> quarterFactory,
            ICardFactory<CharacterBase> characterFactory) 
            : base (playerProvider, lobbyProvider, gameSettings)
        {
            _telegramSettings = telegramSettings;
            _quarterFactory = quarterFactory;
            _characterFactory = characterFactory;
        }

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            _botClient = botClient;
            var message = update.Message;

            Console.WriteLine($"Receive message type: {message.Type}\nChat id: {message.Chat.Id}\nUsername: {message.From.Username}\nUser id: {message.From.Id}\n{message.Text}");

            if (message.Type != MessageType.Text)
            {
                return;
            }

            var text = message.Text.Split(' ', '@');
            var action = text.First() switch
            {
                "/create_lobby" => HandleCreateLobby(message.Chat.Id),
                "/cancel_lobby" => HandleCancelLobby(message.Chat.Id, cancelMidGame: false),
                "/start_game" => HandleGameStart(message.Chat.Id),
                "/end_game" => HandleCancelLobby(message.Chat.Id, cancelMidGame: true),
                "/start" => HandleBotStart(text.Last(), message.Chat),
                "/help" => _botClient.DisplayBotCommands(message.Chat.Id),
                "My-Hand" => DisplayHand(message),
                _ => Task.CompletedTask
            };
            await action;
        }

        private async Task HandleCreateLobby(long chatId)
        {
            //if it's a user chat
            if (chatId > 0)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby != null)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.LobbyAlreadyCreatedError);
                return;
            }

            lobby = new Lobby
            {
                //id used in telegram callback query cannot include '-' 
                Id = Guid.NewGuid().ToString().Replace("-", ""),
                TelegramMetadata = new LobbyTelegramMetadata
                {
                    ChatId = chatId
                }
            };

            await _lobbyProvider.CreateLobby(lobby);

            await SendNewJoinButton(lobby);
        }

        private async Task HandleCancelLobby(long chatId, bool cancelMidGame)
        {
            //if it's a user chat
            if (chatId > 0)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby == null)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.LobbyNotFoundError);
                return;
            }
            if (lobby.Status == LobbyStatus.Configuring && cancelMidGame || lobby.Status != LobbyStatus.Configuring && !cancelMidGame)
            {
                await _botClient.SendTextMessageAsync(chatId, cancelMidGame ? GameMessages.GameNotStartedError : GameMessages.GameIsRunningError);
                return;
            }

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            var message = new HttpRequestMessage(HttpMethod.Delete, _gameSettings.GameApiUrl + $"/{lobby.Id}");
            var response = await _httpClient.SendAsync(message);
            var responseMessage = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                await _botClient.TryDeleteMessage(chatId, lobby.TelegramMetadata.LobbyInfoMessageId);

                if (cancelMidGame)
                {
                    await DeleteMessagesForPlayers(players);
                }
            }

            await _botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, responseMessage);
        }

        private async Task HandleGameStart(long chatId)
        {
            //if it's a user chat
            if (chatId > 0)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby == null)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.LobbyNotFoundError);
                return;
            }
            
            await StartGame(lobby);
        }

        private async Task HandleBotStart(string payload, Chat chat)
        {
            if (long.TryParse(payload, out long groupChatId))
            {
                var lobby = await _lobbyProvider.GetLobbyByChatId(groupChatId);

                if (lobby != null)
                {
                    try
                    {
                        await JoinLobby(chat, groupChatId, lobby);
                    }
                    catch (ArgumentException ex)
                    {
                        await _botClient.SendTextMessageAsync(chat.Id, ex.Message);
                    }
                }
            }
            else
            {
                await _botClient.SendTextMessageAsync(chat.Id, GameMessages.GreetingsMessage);
            }
        }

        private async Task JoinLobby(Chat playerChat, long groupChatId, Lobby lobby)
        {
            if (lobby.Status != LobbyStatus.Configuring)
            {
                throw new ArgumentException(GameMessages.GameIsRunningError);
            }

            var existingPlayer = await _playerProvider.GetPlayerByChatId(playerChat.Id);

            if (existingPlayer != null && existingPlayer.LobbyId != Guid.Empty.ToString())
            {
                throw new ArgumentException(GameMessages.LobbyAlreadyJoinedError);
            }

            if (lobby.PlayersCount >= _gameSettings.MaxPlayersAmount)
            {
                throw new ArgumentException(GameMessages.LobbyIsFullError);
            }

            await CreateAndSavePlayer(playerChat, lobby.Id, existingPlayer, lobby.PlayersCount);

            lobby.PlayersCount++;
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.PlayersCount, lobby.PlayersCount);

            var groupChat = await _botClient.GetChatAsync(groupChatId);
            await _botClient.SendTextMessageAsync(playerChat.Id, string.Format(GameMessages.LobbyJoinedMessage, groupChat.Title));
  
            if (lobby.PlayersCount == _gameSettings.MaxPlayersAmount)
            {
                await StartGame(lobby);
            }
            else
            {
                await SendNewJoinButton(lobby);
            }
        }

        private async Task StartGame(Lobby lobby)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _gameSettings.GameApiUrl + $"/{lobby.Id}/start");
            var response = await _httpClient.SendAsync(message);
            var responseMessage = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                await _botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, responseMessage);
                return;
            }

            var botResponse = await _botClient.PutTextMessage(lobby.TelegramMetadata.ChatId, lobby.TelegramMetadata.LobbyInfoMessageId, responseMessage);
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.TelegramMetadata.LobbyInfoMessageId, botResponse.MessageId);

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);
            await SendReplyKeyboardToPlayers(players, GameMessages.MyHandMessage);

            await SendCharactertSelection(_botClient, lobby.Id);
        }


        private async Task SendNewJoinButton(Lobby lobby)
        {
            var tgMetadata = lobby.TelegramMetadata;

            var lobbyStrBuilder = new StringBuilder();
            lobbyStrBuilder.AppendLine(GameMessages.LobbyRegistrationMessage);
            lobbyStrBuilder.AppendLine($"Players: {lobby.PlayersCount}/{_gameSettings.MaxPlayersAmount}");

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            foreach (var player in players)
            {
                lobbyStrBuilder.AppendLine("• " + player.Name);
            }

            var buttons = new[]
            {
                new []
                {
                    InlineKeyboardButton.WithUrl("Join", $"{_telegramSettings.BotLink}?start={lobby.TelegramMetadata.ChatId}")
                }
            };

            var botResponse = await _botClient.PutInlineKeyboard(tgMetadata.ChatId, tgMetadata.LobbyInfoMessageId, lobbyStrBuilder.ToString(), buttons);

            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.TelegramMetadata.LobbyInfoMessageId, botResponse.MessageId);
        }

        private async Task CreateAndSavePlayer(Chat playerChat, string lobbyId, Player existingPlayer, int characterSelectionOrder)
        {
            var player = new Player
            {
                Id = existingPlayer?.Id,
                Name = string.IsNullOrWhiteSpace(playerChat.Username) ? playerChat.FirstName : playerChat.Username,
                LobbyId = lobbyId,
                CSOrder = characterSelectionOrder,
                HasCrown = characterSelectionOrder == 0,
                TelegramMetadata = new PlayerTelegramMetadata
                {
                    ChatId = playerChat.Id
                }
            };

            await _playerProvider.SavePlayer(player);
        }

        private async Task DisplayHand(Message message)
        {
            var playerChatId = message.From.Id;
            await _botClient.TryDeleteMessage(playerChatId, message.MessageId);

            var player = await _playerProvider.GetPlayerByChatId(playerChatId, loadPlacedQuarters: true);

            if (player.LobbyId == Guid.Empty.ToString())
            {
                await _botClient.SendTextMessageAsync(playerChatId, GameMessages.NotInGameError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyById(player.LobbyId);

            await _botClient.TryDeleteMessages(playerChatId, player.TelegramMetadata.MyHandIds);

            //Send all cards
            var characters = player.CharacterHand.Select(x => _characterFactory.GetCard(x));
            var quarters = player.QuarterHand.Select(y => _quarterFactory.GetCard(y));
            var placedQuarters = player.PlacedQuarters.Select(z => z.QuarterBase);

            var result = (await _botClient.SendCardGroup(playerChatId, characters, $"Character {GameSymbols.Character}")).ToList();
            result.AddRange(await _botClient.SendCardGroup(playerChatId, quarters, $"In hand {GameSymbols.Card}"));
            result.AddRange(await _botClient.SendCardGroup(playerChatId, placedQuarters, $"Placed {GameSymbols.PlacedQuarter}"));

            //Send player stats
            var characterInfo = GameMessages.GetPlayerCharacters(lobby, player);
            var playerInfo = GameMessages.GetPlayerInfoMessage(player.Coins, player.QuarterHand.Count, player.PlacedQuarters.Count, player.Score);
            var closebtn = InlineKeyboardButton.WithCallbackData(GameMessages.MyHandClose, $"myHandClose");

            var tgMessage = string.IsNullOrWhiteSpace(characterInfo) ? playerInfo : $"{characterInfo}\n\n{playerInfo}";

            result.Add(await _botClient.SendTextMessageAsync(playerChatId, tgMessage, parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(closebtn)));

            //Save message ids
            player.TelegramMetadata.MyHandIds = result.AsParallel().WithDegreeOfParallelism(3).Select(x => x.MessageId).ToList();
            await _playerProvider.UpdatePlayer(player.Id, p => p.TelegramMetadata, player.TelegramMetadata);
        }

        private async Task SendReplyKeyboardToPlayers(IEnumerable<Player> players, string text)
        {
            var replyKeyboardMarkup = new ReplyKeyboardMarkup(
                new[]
                {
                    new KeyboardButton[] { "My-Hand" }
                })
            {
                ResizeKeyboard = true
            };

            var sendTasks = players.AsParallel().WithDegreeOfParallelism(3).Select(p => _botClient.SendTextMessageAsync(p.TelegramMetadata.ChatId, text, replyMarkup: replyKeyboardMarkup));
            await Task.WhenAll(sendTasks);
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
                messageIds.Add(p.TelegramMetadata.GameActionKeyboardId);
                messageIds.Add(p.TelegramMetadata.ActionErrorId);

                await _botClient.TryDeleteMessages(chatId, messageIds);

                //Deleting reply keyboard and sending final message
                await _botClient.SendTextMessageAsync(chatId, GameMessages.FarewellMessage, replyMarkup: new ReplyKeyboardRemove());
            });

            await Task.WhenAll(deleteTasks);
        }
    }
}
