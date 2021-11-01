using System;
using System.Collections.Generic;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Interfaces
{
    public interface IPlayerProvider
    {
        void CreatePlayer(Player player);

        Player GetPlayerById(Guid playerId);

        Player GetPlayerByChatId(string chatId);

        List<Player> GetPlayersByRoomId(Guid roomId);

        void SavePlayer(Player player);

        void DeletePlayer(Guid playerId);
    }
}
