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
        private readonly TelegramSettings _telegramSettings;

        public TelegramMessageHandler(
            ILobbyProvider lobbyProvider, 
            IPlayerProvider playerProvider, 
            TelegramSettings telegramSettings,
            GameSettings gameSettings,
            ICardFactory<Quarter> quarterFactory) 
            : base (playerProvider, lobbyProvider, gameSettings)
        {
            _telegramSettings = telegramSettings;
            _quarterFactory = quarterFactory;
        }

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            base._botClient = botClient;
            var message = update.Message;

            Console.WriteLine($"Receive message type: {message.Type}\nChat id: {message.Chat.Id}\nUsername: {message.From.Username}\nUser id: {message.From.Id}\n{message.Text}");

            if (message.Type != MessageType.Text)
            {
                return;
            }

            var text = message.Text.Split(' ', '@');
            var action = text.First() switch
            {
                "My-Hand" => DisplayHand(message),
                "/create_lobby" => HandleCreateLobby(message.Chat.Id),
                "/start" => HandleStart(text.Last(), message.Chat),
                "/end" => HandleEndGame(message.Chat.Id),
                "/help" => base._botClient.DisplayBotCommands(message.Chat.Id),
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

            await CancelGame(chatId, lobby, players);
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

            var botResponse = await _botClient.PutMessage(lobby.TelegramMetadata.ChatId, lobby.TelegramMetadata.LobbyInfoMessageId, responseMessage);
            lobby.TelegramMetadata.LobbyInfoMessageId = botResponse.MessageId;
            await _lobbyProvider.UpdateLobby(lobby, x => x.TelegramMetadata.LobbyInfoMessageId);

            var players = await _playerProvider.GetPlayersByLobbyId(lobby.Id);
            await SendReplyKeyboardToPlayers(players, GameMessages.MyHandMessage);

            await NextCharactertSelection(lobby.Id);
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

            if (player.LobbyId == Guid.Empty.ToString())
            {
                await _botClient.SendTextMessageAsync(playerChatId, GameMessages.NotInGameError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyById(player.LobbyId);

            await _botClient.TryDeleteMessages(playerChatId, player.TelegramMetadata.MyHandIds);

            //Send all cards
            var characters = lobby.CharacterDeck.Where(c => player.CharacterHand.Contains(c.Name)).ToList();
            var quarters = player.QuarterHand.Select(y => _quarterFactory.GetCard(y)).ToList();
            var placedQuarters = player.PlacedQuarters.Select(z => z.QuarterBase).ToList();

            var result = new List<Message>();

            if (characters.Any())
            {
                result.AddRange(await _botClient.SendCardGroup(playerChatId, characters.Select(c => c.CharacterBase), $"Character {GameSymbols.Character}"));

                if (characters.Count > 1)
                {
                    result.Add(await _botClient.SendTextMessageAsync(playerChatId, $"Characters {GameSymbols.Character}"));
                }
            }

            if (quarters.Any())
            {
                result.AddRange(await _botClient.SendCardGroup(playerChatId, quarters, $"In hand {GameSymbols.Card}"));

                if (quarters.Count > 1)
                {
                    result.Add(await _botClient.SendTextMessageAsync(playerChatId, $"Quarters in hand {GameSymbols.Card}"));
                }
            }

            if (placedQuarters.Any())
            {
                result.AddRange(await _botClient.SendCardGroup(playerChatId, placedQuarters, $"Placed {GameSymbols.PlacedQuarter}"));

                if (placedQuarters.Count > 1)
                {
                    result.Add(await _botClient.SendTextMessageAsync(playerChatId, $"Placed quarters {GameSymbols.PlacedQuarter}"));
                }
            }

            //Send player stats
            var characterInfo = GameMessages.GetPlayerCharactersInfo(characters, player);
            var playerInfo = GameMessages.GetPlayerInfoMessage(player);
            var closebtn = InlineKeyboardButton.WithCallbackData(GameMessages.MyHandClose, $"myHandClose_{lobby.Id}");

            var tgMessage = string.IsNullOrWhiteSpace(characterInfo) ? playerInfo : $"{characterInfo}\n\n{playerInfo}";

            result.Add(await _botClient.SendTextMessageAsync(playerChatId, tgMessage, parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(closebtn)));

            //Save message ids
            player.TelegramMetadata.MyHandIds = result.AsParallel().WithDegreeOfParallelism(3).Select(x => x.MessageId).ToList();
            await _playerProvider.UpdatePlayer(player, p => p.TelegramMetadata.MyHandIds);
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
    }
}
