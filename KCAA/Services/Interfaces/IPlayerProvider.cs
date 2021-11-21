using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Interfaces
{
    public interface IPlayerProvider
    {
        Player GetPlayerById(Guid playerId);

        Player GetPlayerByChatId(long chatId);

        List<Player> GetPlayersByLobbyId(Guid lobbyId);

        Task SavePlayer(Player player);

        Task SavePlayers(IEnumerable<Player> players);

        Task DeletePlayer(Guid playerId);
    }
}
