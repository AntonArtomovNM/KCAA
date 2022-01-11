using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using KCAA.Settings.GameSettings;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramCallbackQueryHandler : TelegramGameHandlerBase, ITelegramUpdateHandler
    {
        public TelegramCallbackQueryHandler(
            ILobbyProvider lobbyProvider, 
            IPlayerProvider playerProvider,
            GameSettings gameSettings)
            : base(playerProvider, lobbyProvider,gameSettings)
        {
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
                "chooseCharacter" => HandleChooseCharacter(callbackQuery.Message.Chat.Id, data.LastOrDefault()),
                _ => Task.CompletedTask
            };
            await action;
        }

        private async Task HandleChooseCharacter(long chatId, string characterName)
        {
            var player = await _playerProvider.GetPlayerByChatId(chatId);

            if (player == null)
            {
                return;
            }

            var lobby = await _lobbyProvider.GetLobbyById(player.LobbyId);

            if (lobby == null || lobby.Status != LobbyStatus.CharacterSelection)
            {
                await _botClient.SendTextMessageAsync(chatId, GameMessages.CharacterSelectionError);
                return;
            }

            player.CharacterHand.Add(characterName);
            var character = lobby.CharacterDeck.Find(x => x.Name == characterName);
            character.Status = CharacterStatus.SecretlyRemoved;

            var deleteMessageTasks = player.TelegramMetadata.CharacterInfoMessageIds.Select(x => _botClient.DeleteMessageAsync(chatId, x));
            await Task.WhenAll(deleteMessageTasks);
            player.TelegramMetadata.CharacterInfoMessageIds = new List<int>();

            await _playerProvider.SavePlayer(player);
            await _lobbyProvider.UpdateLobby(lobby.Id, x => x.CharacterDeck, lobby.CharacterDeck);

            await _botClient.SendTextMessageAsync(chatId, $"{character.CharacterBase.DisplayName} selected");

            await SendCharactertSelection(_botClient, lobby.Id);
        }
    }
}
