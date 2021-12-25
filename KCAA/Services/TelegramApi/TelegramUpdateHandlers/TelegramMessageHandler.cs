using System;
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
using KCAA.Models.Cards;
using System.Collections.Generic;
using KCAA.Models.Characters;
using System.Text;
using System.Net.Http;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramMessageHandler : ITelegramUpdateHandler
    {
        private readonly ILobbyProvider _lobbyProvider;
        private readonly IPlayerProvider _playerProvider;
        private readonly IGameObjectFactory<Card> _cardFactory;
        private readonly IGameObjectFactory<Character> _characterFactory;
        private readonly TelegramSettings _telegramSettings;
        private readonly GameSettings _gameSettings;
        private readonly HttpClient _httpClient;

        public TelegramMessageHandler(
            ILobbyProvider lobbyProvider, 
            IPlayerProvider playerProvider, 
            TelegramSettings telegramSettings,
            GameSettings gameSettings,
            IGameObjectFactory<Card> cardFactory,
            IGameObjectFactory<Character> characterFactory) 
        {
            _lobbyProvider = lobbyProvider;
            _playerProvider = playerProvider;
            _telegramSettings = telegramSettings;
            _gameSettings = gameSettings;
            _cardFactory = cardFactory;
            _characterFactory = characterFactory;
            _httpClient = new HttpClient();
        }

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            var message = update.Message;

            Console.WriteLine($"Receive message type: {message.Type}\nChat id: {message.Chat.Id}\nUsername: {message.From.Username}\nUser id: {message.From.Id}\n{message.Text}");

            if (message.Type != MessageType.Text)
            {
                return;
            }

            var text = message.Text.Split(' ', '@');
            var action = text.First() switch
            {
                "/create_lobby" => HandleCreateLobby(botClient, message.Chat.Id),
                "/cancel_lobby" => HandleCancelLobby(botClient, message.Chat.Id, cancelMidGame: false),
                "/start_game" => HandleGameStart(botClient, message.Chat.Id),
                "/end_game" => HandleCancelLobby(botClient, message.Chat.Id, cancelMidGame: true),
                "/start" => HandleBotStart(botClient, text.Last(), message.Chat),
                "/help" => botClient.DisplayBotCommands(message.Chat.Id),
                "/test_display_card" => DisplayCardsTest(botClient, message.Chat.Id),
                "/test_display_character" => DisplayCharacterTest(botClient, message.Chat.Id),
                _ => Task.CompletedTask
            };
            await action;
        }

        private async Task HandleCreateLobby(ITelegramBotClient botClient, long chatId)
        {
            //if it's a user chat
            if (chatId > 0)
            {
                await botClient.SendTextMessageAsync(chatId, GameMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby != null)
            {
                await botClient.SendTextMessageAsync(chatId, GameMessages.LobbyAlreadyCreatedError);
                return;
            }

            lobby = new Lobby
            {
                Id = Guid.NewGuid().ToString(),
                TelegramMetadata = new TelegramMetadata
                {
                    ChatId = chatId
                }
            };

            await _lobbyProvider.CreateLobby(lobby);

            await SendNewJoinButton(botClient, lobby);
        }

        private async Task HandleCancelLobby(ITelegramBotClient botClient, long chatId, bool cancelMidGame)
        {
            //if it's a user chat
            if (chatId > 0)
            {
                await botClient.SendTextMessageAsync(chatId, GameMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby == null)
            {
                await botClient.SendTextMessageAsync(chatId, GameMessages.LobbyNotFoundError);
                return;
            }
            if (lobby.Status == LobbyStatus.Configuring && cancelMidGame || lobby.Status != LobbyStatus.Configuring && !cancelMidGame)
            {
                await botClient.SendTextMessageAsync(chatId, cancelMidGame ? GameMessages.GameNotStartedError : GameMessages.GameIsRunningError);
                return;
            }

            var message = new HttpRequestMessage(HttpMethod.Delete, _gameSettings.GameApiUrl + $"/{lobby.Id}");
            var response = await _httpClient.SendAsync(message);
            var responseMessage = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                await botClient.DeleteMessageAsync(chatId, lobby.TelegramMetadata.LobbyInfoMessageId);
            }
            
            await botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, responseMessage);
        }

        private async Task HandleGameStart(ITelegramBotClient botClient, long chatId)
        {
            //if it's a user chat
            if (chatId > 0)
            {
                await botClient.SendTextMessageAsync(chatId, GameMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby == null)
            {
                await botClient.SendTextMessageAsync(chatId, GameMessages.LobbyNotFoundError);
                return;
            }
            
            await StartGame(botClient, lobby);
        }

        private async Task HandleBotStart(ITelegramBotClient botClient, string payload, Chat chat)
        {
            if (long.TryParse(payload, out long groupChatId))
            {
                var lobby = await _lobbyProvider.GetLobbyByChatId(groupChatId);

                if (lobby != null)
                {
                    try
                    {
                        await JoinLobby(botClient, chat, groupChatId, lobby);
                    }
                    catch (ArgumentException ex)
                    {
                        await botClient.SendTextMessageAsync(chat.Id, ex.Message);
                    }
                }
            }
            else
            {
                await botClient.SendTextMessageAsync(chat.Id, GameMessages.GreetingsMessage);
            }
        }

        private async Task JoinLobby(ITelegramBotClient botClient, Chat playerChat, long groupChatId, Lobby lobby)
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

            await CreateAndSavePlayer(playerChat, lobby.Id, existingPlayer);

            lobby.PlayersCount++;
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.PlayersCount, lobby.PlayersCount);

            var groupChat = await botClient.GetChatAsync(groupChatId);
            await botClient.SendTextMessageAsync(playerChat.Id, string.Format(GameMessages.LobbyJoinedMessage, groupChat.Title));
  
            if (lobby.PlayersCount == _gameSettings.MaxPlayersAmount)
            {
                await StartGame(botClient, lobby);
            }
            else
            {
                await SendNewJoinButton(botClient, lobby);
            }
        }

        private async Task StartGame(ITelegramBotClient botClient, Lobby lobby)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, _gameSettings.GameApiUrl + $"/{lobby.Id}/start");
            var response = await _httpClient.SendAsync(message);
            var responseMessage = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                await botClient.SendTextMessageAsync(lobby.TelegramMetadata.ChatId, responseMessage);
                return;
            }

            var botResponse = await botClient.PutTextMessage(lobby.TelegramMetadata.ChatId, lobby.TelegramMetadata.LobbyInfoMessageId, responseMessage);
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.TelegramMetadata.LobbyInfoMessageId, botResponse.MessageId);


        }

        private async Task SendNewJoinButton(ITelegramBotClient botClient, Lobby lobby)
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

            var botResponse = await botClient.PutInlineKeyboard(tgMetadata.ChatId, tgMetadata.LobbyInfoMessageId, lobbyStrBuilder.ToString(), buttons);

            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.TelegramMetadata.LobbyInfoMessageId, botResponse.MessageId);
        }

        private async Task CreateAndSavePlayer(Chat playerChat, string lobbyId, Player existingPlayer)
        {
            var player = new Player
            {
                Id = existingPlayer?.Id,
                Name = string.IsNullOrWhiteSpace(playerChat.Username) ? playerChat.FirstName : playerChat.Username,
                LobbyId = lobbyId,
                ChatId = playerChat.Id
            };

            await _playerProvider.SavePlayer(player);
        }

        [Obsolete]
        private async Task DisplayCardsTest(ITelegramBotClient botClient, long chatId)
        {
            var cards = new List<Card>
                    {
                        _cardFactory.GetGameObject(CardNames.Yellow1),
                        _cardFactory.GetGameObject(CardNames.Blue2),
                        _cardFactory.GetGameObject(CardNames.Green3),
                        _cardFactory.GetGameObject(CardNames.Red4),
                        _cardFactory.GetGameObject(CardNames.DragonGates)
                    };

            var sendCardsTasks = cards.Select(async x => await botClient.SendCard(chatId, x));
            await Task.WhenAll(sendCardsTasks);
        }

        [Obsolete]
        private async Task DisplayCharacterTest(ITelegramBotClient botClient, long chatId)
        {
            var characters = new List<Character>
                    {
                        _characterFactory.GetGameObject(CharacterNames.Architect),
                        _characterFactory.GetGameObject(CharacterNames.Assassin),
                        _characterFactory.GetGameObject(CharacterNames.Beggar),
                        _characterFactory.GetGameObject(CharacterNames.Bishop),
                        _characterFactory.GetGameObject(CharacterNames.King),
                        _characterFactory.GetGameObject(CharacterNames.Magician),
                        _characterFactory.GetGameObject(CharacterNames.Merchant),
                        _characterFactory.GetGameObject(CharacterNames.Thief),
                        _characterFactory.GetGameObject(CharacterNames.Warlord)
                    };

            var sendCharactersTasks = characters.Select(async x => await botClient.SendCharacter(chatId, x));
            await Task.WhenAll(sendCharactersTasks);
        }
    }
}
