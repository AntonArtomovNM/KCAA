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
using System.Text;
using System.Net.Http;
using KCAA.Helpers;
using KCAA.Models;
using Serilog;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramMessageHandler : TelegramGameHandlerBase, ITelegramUpdateHandler
    {
        private readonly ICardFactory<Quarter> _quarterFactory;
        private readonly TelegramSettings _telegramSettings;

        public object GameActions { get; private set; }

        public TelegramMessageHandler(
            ILobbyProvider lobbyProvider,
            IPlayerProvider playerProvider,
            TelegramSettings telegramSettings,
            GameSettings gameSettings,
            ICardFactory<Quarter> quarterFactory)
            : base(playerProvider, lobbyProvider, gameSettings)
        {
            _telegramSettings = telegramSettings;
            _quarterFactory = quarterFactory;
        }

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            _botClient = botClient;
            var message = update.Message;

            Log.Information($"[Debug] Receive message type: {message.Type} | Chat id: {message.Chat.Id} | Username: {message.From.Username} | User id: {message.From.Id} | Data: {message.Text}");

            if (message.Type != MessageType.Text)
            {
                return;
            }

            var text = message.Text.Split(' ', '@');
            var action = text.First() switch
            {
                "My-Hand" => DisplayHand(message),
                "Table" => DisplayTable(message),
                "/create_lobby" => HandleCreateLobby(message.Chat.Id),
                "/start" => HandleStart(text.Last(), message.Chat),
                "/end" => HandleEndGame(message.Chat.Id),
                "/rules" => _botClient.SendTextMessageAsync(message.Chat.Id, GameMessages.BasicRules, parseMode: ParseMode.Html),
                "/help" => _botClient.DisplayBotCommands(message.Chat.Id),
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

        private async Task HandleEndGame(long chatId)
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

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);

            if (lobby.Status == LobbyStatus.Configuring)
            {
                await CancelGame(chatId, lobby, players);
            }
            else
            {
                await EndGame(lobby.Id);
            }
        }

        private async Task HandleStart(string payload, Chat chat)
        {
            //if it's a user chat
            if (chat.Id > 0)
            {
                await HandlUserChatStart(payload, chat);
                return;
            }

            await HandleGroupChatStart(chat);
        }

        private async Task HandleGroupChatStart(Chat chat)
        {
            var lobby = await _lobbyProvider.GetLobbyByChatId(chat.Id);

            if (lobby == null)
            {
                await _botClient.SendTextMessageAsync(chat.Id, GameMessages.LobbyNotFoundError);
                return;
            }

            await StartGame(lobby);
        }

        private async Task HandlUserChatStart(string payload, Chat chat)
        {
            if (!long.TryParse(payload, out long groupChatId))
            {
                await _botClient.SendTextMessageAsync(chat.Id, GameMessages.GreetingsMessage);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyByChatId(groupChatId);

            if (lobby == null)
            {
                return;
            }

            try
            {
                await JoinLobby(chat, groupChatId, lobby);
            }
            catch (ArgumentException ex)
            {
                await _botClient.SendTextMessageAsync(chat.Id, ex.Message);
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
            await _lobbyProvider.UpdateLobby(lobby, x => x.PlayersCount);

            var groupChat = await _botClient.GetChatAsync(groupChatId);
            await _botClient.SendTextMessageAsync(playerChat.Id, string.Format(GameMessages.LobbyJoinedMessage, groupChat.Title), parseMode: ParseMode.Html);

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

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);
            await SendReplyKeyboardToPlayers(players);

            await DisplayRemovedCharacters(lobby.Id);

            await NextCharactertSelection(lobby.Id);
        }


        private async Task SendNewJoinButton(Lobby lobby)
        {
            var tgMetadata = lobby.TelegramMetadata;

            var lobbyStrBuilder = new StringBuilder();
            lobbyStrBuilder.AppendLine(GameMessages.LobbyRegistrationMessage);
            lobbyStrBuilder.AppendLine($"Players: {lobby.PlayersCount}/{_gameSettings.MaxPlayersAmount}");

            var players = (await _playerProvider.GetPlayersByLobbyId(lobby.Id)).OrderBy(p => p.CSOrder);

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

            var botResponse = await _botClient.PutMessage(tgMetadata.ChatId, tgMetadata.LobbyInfoMessageId, lobbyStrBuilder.ToString(), new InlineKeyboardMarkup(buttons));
            lobby.TelegramMetadata.LobbyInfoMessageId = botResponse.MessageId;
            await _lobbyProvider.UpdateLobby(lobby, x => x.TelegramMetadata.LobbyInfoMessageId);
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
            await _botClient.TryDeleteMessages(playerChatId, player.TelegramMetadata.MyHandIds);

            if (player.LobbyId == Guid.Empty.ToString())
            {
                await _botClient.SendTextMessageAsync(playerChatId, GameMessages.NotInGameError);
                return;
            }

            var characterDeck = (await _lobbyProvider.GetLobbyById(player.LobbyId)).CharacterDeck;

            player.TelegramMetadata.MyHandIds = await SendPlayerData(playerChatId, player, characterDeck, loadSecretData: true);
            player.TelegramMetadata.MyHandIds.Add(await SendCloseButton(playerChatId, player.LobbyId, "My-Hand"));

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.MyHandIds);
        }

        private async Task DisplayTable(Message message)
        {
            var playerChatId = message.From.Id;
            await _botClient.TryDeleteMessage(playerChatId, message.MessageId);

            var player = await _playerProvider.GetPlayerByChatId(playerChatId);
            await _botClient.TryDeleteMessages(playerChatId, player.TelegramMetadata.TableIds);

            if (player.LobbyId == Guid.Empty.ToString())
            {
                await _botClient.SendTextMessageAsync(playerChatId, GameMessages.NotInGameError);
                return;
            }

            var characterDeck = (await _lobbyProvider.GetLobbyById(player.LobbyId)).CharacterDeck;
            var otherPlayers = (await _playerProvider.GetPlayersByLobbyId(player.LobbyId, loadPlacedQuarters: true))
                .Where(p => p.Id != player.Id)
                .OrderBy(p => p.CSOrder);

            var messageIds = new List<int>();
            var lastPlayerId = otherPlayers.Last().Id;

            foreach (var p in otherPlayers)
            {
                messageIds.AddRange(await SendPlayerData(playerChatId, p, characterDeck));

                if (p.Id != lastPlayerId)
                {
                    messageIds.Add((await _botClient.SendTextMessageAsync(playerChatId, GameMessages.PlayerBorders)).MessageId);
                }
            }
            messageIds.Add(await SendCloseButton(playerChatId, player.LobbyId, "Table"));

            player.TelegramMetadata.TableIds = messageIds;

            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.TableIds);
        }

        private async Task<List<int>> SendPlayerData(long chatId, Player player, IEnumerable<Character> characterDeck, bool loadSecretData = false)
        {
            var playerCharacters = characterDeck.Where(c => player.CharacterHand.Contains(c.Name));
            var placedQuarters = player.PlacedQuarters.Select(z => z.QuarterBase);

            var MessageIds = new List<int>();

            if (loadSecretData)
            {
                var quarters = player.QuarterHand.Select(y => _quarterFactory.GetCard(y));

                if (playerCharacters.Any())
                {
                    MessageIds.Add((await _botClient.SendTextMessageAsync(chatId, $"Characters {GameSymbols.Character}:")).MessageId);
                    MessageIds.AddRange(await _botClient.SendCardGroup(chatId, playerCharacters.Select(c => c.CharacterBase), c => $"{c.DisplayName} (Character {GameSymbols.Character})"));
                }

                if (quarters.Any())
                {
                    int groupsCount = (quarters.Count() % 10 == 0) ? quarters.Count() / 10 : quarters.Count() / 10 + 1;
                    MessageIds.Add((await _botClient.SendTextMessageAsync(chatId, $"Quarters in hand {GameSymbols.Card}:")).MessageId);
                    for (int i = 0; i < groupsCount; i++) 
                    {
                        MessageIds.AddRange(await _botClient.SendCardGroup(chatId, quarters.Skip(i * 10).Take(10), q => $"{GameSymbols.GetColorByType(q.Type)} {q.DisplayName}{(q.BonusScore > 0 ? $"[+{q.BonusScore}{GameSymbols.Score}] " : "")} (In hand {GameSymbols.Card})"));
                    }

                    
                }
            }

            if (placedQuarters.Any())
            {
                MessageIds.Add((await _botClient.SendTextMessageAsync(chatId, $"Placed quarters {GameSymbols.PlacedQuarter}:")).MessageId);
                MessageIds.AddRange(await _botClient.SendCardGroup(chatId, placedQuarters, pq => $"{GameSymbols.GetColorByType(pq.Type)} {pq.DisplayName}{(pq.BonusScore > 0 ? $"[+{pq.BonusScore}{GameSymbols.Score}] " : "")} (Placed {GameSymbols.PlacedQuarter})"));
            }

            //Send player stats
            var characterInfo = GameMessages.GetPlayerCharactersInfo(playerCharacters, player, loadSecretData);
            var playerInfo = GameMessages.GetPlayerInfoMessage(player);

            var builder = new StringBuilder();

            if (!loadSecretData)
            {
                builder.Append(player.Name + " ");
            }
            if (!string.IsNullOrWhiteSpace(characterInfo))
            {
                builder.AppendLine(characterInfo);
            }
            builder.AppendLine(playerInfo);

            MessageIds.Add((await _botClient.SendTextMessageAsync(
                chatId,
                builder.ToString(),
                parseMode: ParseMode.Html)).MessageId);

            return MessageIds;
        }

        private async Task<int> SendCloseButton(long chatId, string lobbyId, string messageType)
        {
            var closebtn = InlineKeyboardButton.WithCallbackData(
                GameSymbols.Close,
                $"{GameActionNames.Close}_{lobbyId}_{messageType}");

            return (await _botClient.SendTextMessageAsync(chatId, GameActionNames.GetActionDisplayName(GameActionNames.Close), replyMarkup: new InlineKeyboardMarkup(closebtn))).MessageId;
        }

        private async Task SendReplyKeyboardToPlayers(IEnumerable<Player> players)
        {
            var replyKeyboardMarkup = new ReplyKeyboardMarkup(
                new[]
                {
                    new KeyboardButton[] { "My-Hand", "Table" }
                })
            {
                ResizeKeyboard = true
            };

            var sendTasks = players
                .AsParallel()
                .WithDegreeOfParallelism(3)
                .Select(p => _botClient.SendTextMessageAsync(
                    p.TelegramMetadata.ChatId,
                    GameMessages.ReplyButtonsMessage,
                    parseMode: ParseMode.Html,
                    replyMarkup: replyKeyboardMarkup));

            await Task.WhenAll(sendTasks);
        }
    }
}
