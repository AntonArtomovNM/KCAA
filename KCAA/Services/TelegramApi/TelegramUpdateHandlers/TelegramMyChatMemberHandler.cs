using System;
using System.Threading.Tasks;
using KCAA.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using KCAA.Services.Interfaces;

namespace KCAA.Services.TelegramApi.TelegramUpdateHandlers
{
    public class TelegramMyChatMemberHandler : ITelegramUpdateHandler
    {
        private readonly ILobbyProvider _lobbyProvider;

        public TelegramMyChatMemberHandler(ILobbyProvider lobbyProvider)
        { 
            _lobbyProvider = lobbyProvider;
        }

        public async Task Handle(ITelegramBotClient botClient, Update update)
        {
            var myChatMember = update.MyChatMember;

            Console.WriteLine($"ChatId: {myChatMember.Chat.Id}\nOld member: {myChatMember.OldChatMember.Status}\nNew member: {myChatMember.NewChatMember.Status}");

            var chatId = myChatMember.Chat.Id;

            //if it's a group chat
            if (chatId < 0)
            {
                if (myChatMember.OldChatMember.Status == ChatMemberStatus.Left)
                {
                    await botClient.SendTextMessageAsync(chatId, GameMessages.GreetingsMessage);
                }
                else if (myChatMember.NewChatMember.Status == ChatMemberStatus.Left)
                {
                    await _lobbyProvider.DeleteLobbyByChatId(chatId);
                }
            }
        }
    }
}
