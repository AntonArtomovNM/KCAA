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

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramMessageHandler : ITelegramUpdateHandler
    {
        private readonly ILobbyProvider _lobbyProvider;
        private readonly IPlayerProvider _playerProvider;
        private readonly TelegramSettings _telegramSettings;
        private readonly GameSettings _gameSettings;
        private readonly IGameObjectFactory<Card> _cardFactory;
        private readonly IGameObjectFactory<Character> _characterFactory;

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
                "/cancel_lobby" => HandleCancelLobby(botClient, message.Chat.Id),
                "/start_game" => HandleGameStart(botClient, message.Chat.Id),
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
                await botClient.SendTextMessageAsync(chatId, BotMessages.CommandOnlyForGroupsError);
                return;
            }

            var lobby = _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby != null)
            {
                await botClient.SendTextMessageAsync(chatId, BotMessages.LobbyAlreadyCreatedError);
                return;
            }

            lobby = new Lobby
            {
                //TODO: Add character and card deck
                TelegramMetadata = new TelegramMetadata
                {
                    ChatId = chatId
                }
            };

            await _lobbyProvider.CreateLobby(lobby);

            await DisplayJoinLobbyMessage(botClient, lobby);
        }

        private async Task HandleCancelLobby(ITelegramBotClient botClient, long chatId)
        {
            Lobby lobby;
            try
            {
                lobby = GetConfiguringLobby(chatId);
            }
            catch (ArgumentException ex)
            {
                await botClient.SendTextMessageAsync(chatId, ex.Message);
                return;
            }

            await _lobbyProvider.DeleteLobby(lobby);

            var players = _playerProvider.GetPlayersByLobbyId(lobby.Id);

            foreach (var player in players)
            {
                player.LobbyId = Guid.Empty;
            }

            await _playerProvider.SavePlayers(players);

            await botClient.PutTextMessage(lobby.TelegramMetadata.ChatId, lobby.TelegramMetadata.LobbyInfoMessageId, BotMessages.LobbyCanceledMessage);
        }

        private async Task HandleGameStart(ITelegramBotClient botClient, long chatId)
        {
            Lobby lobby;
            try
            {
                lobby = GetConfiguringLobby(chatId);
            }
            catch (ArgumentException ex)
            {
                await botClient.SendTextMessageAsync(chatId, ex.Message);
                return;
            }

            if (lobby.PlayersCount < _gameSettings.MinPlayersAmount)
            {
                await botClient.SendTextMessageAsync(chatId, string.Format(BotMessages.NotEnoughPlayers, _gameSettings.MinPlayersAmount));
                return;
            }

            await StartGame(botClient, lobby);

            await _lobbyProvider.SaveLobby(lobby);
        }

        private async Task HandleBotStart(ITelegramBotClient botClient, string payload, Chat chat)
        {
            var responce = BotMessages.GreetingsMessage;

            if (long.TryParse(payload, out long groupChatId))
            {
                var lobby = _lobbyProvider.GetLobbyByChatId(groupChatId);

                if (lobby != null)
                {
                    responce = await JoinLobby(botClient, chat, groupChatId, lobby);
                }
            }

            await botClient.SendTextMessageAsync(chat.Id, responce);
        }

        private async Task<string> JoinLobby(ITelegramBotClient botClient, Chat playerChat, long groupChatId, Lobby lobby)
        {
            if (lobby.Status != LobbyStatus.Configuring)
            {
                return BotMessages.GameIsRunningError;
            }

            var existingPlayer = _playerProvider.GetPlayerByChatId(playerChat.Id);

            if (existingPlayer != null && existingPlayer.LobbyId != Guid.Empty)
            {
                return BotMessages.LobbyAlreadyJoinedError;
            }

            if (lobby.PlayersCount >= _gameSettings.MaxPlayersAmount)
            {
                return BotMessages.LobbyIsFullError;
            }

            await CreateAndSavePlayer(playerChat, lobby, existingPlayer);

            if (lobby.PlayersCount == _gameSettings.MaxPlayersAmount)
            {
                await StartGame(botClient, lobby);
            }
            else
            {
                await DisplayJoinLobbyMessage(botClient, lobby);
            }

            var groupChat = await botClient.GetChatAsync(groupChatId);

            return string.Format(BotMessages.LobbyJoinedMessage, groupChat.Title);
        }

        private async Task StartGame(ITelegramBotClient botClient, Lobby lobby)
        {
            lobby.Status = LobbyStatus.CharacterSelection;

            var message = await botClient.PutTextMessage(lobby.TelegramMetadata.ChatId, lobby.TelegramMetadata.LobbyInfoMessageId, BotMessages.GameStartMessage);

            lobby.TelegramMetadata.LobbyInfoMessageId = message.MessageId;
            await _lobbyProvider.SaveLobby(lobby);
        }

        private async Task DisplayJoinLobbyMessage(ITelegramBotClient botClient, Lobby lobby)
        {
            var tgMetadata = lobby.TelegramMetadata;

            var lobbyStrBuilder = new StringBuilder();
            lobbyStrBuilder.AppendLine(BotMessages.LobbyRegistrationMessage);
            lobbyStrBuilder.AppendLine($"Players: {lobby.PlayersCount}/{_gameSettings.MaxPlayersAmount}");

            var players = _playerProvider.GetPlayersByLobbyId(lobby.Id);

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

            var sentMessage = await botClient.PutInlineKeyboard(tgMetadata.ChatId, tgMetadata.LobbyInfoMessageId, lobbyStrBuilder.ToString(), buttons);

            lobby.TelegramMetadata.LobbyInfoMessageId = sentMessage.MessageId;
            await _lobbyProvider.SaveLobby(lobby);
        }

        private Lobby GetConfiguringLobby(long chatId)
        {
            //if it's a user chat
            if (chatId > 0)
            {
                throw new ArgumentException(BotMessages.CommandOnlyForGroupsError);
            }

            var lobby = _lobbyProvider.GetLobbyByChatId(chatId);

            if (lobby == null)
            {
                throw new ArgumentException(BotMessages.LobbyNotFoundError);
            }

            if (lobby.Status != LobbyStatus.Configuring)
            {
                throw new ArgumentException(BotMessages.GameIsRunningError);
            }

            return lobby;
        }

        private async Task CreateAndSavePlayer(Chat playerChat, Lobby lobby, Player existingPlayer)
        {
            var player = new Player
            {
                Id = existingPlayer?.Id ?? Guid.Empty,
                Name = string.IsNullOrWhiteSpace(playerChat.Username) ? playerChat.FirstName : playerChat.Username,
                LobbyId = lobby.Id,
                ChatId = playerChat.Id
            };

            await _playerProvider.SavePlayer(player);

            lobby.PlayersCount++;
        }

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
