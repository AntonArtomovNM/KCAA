using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Interfaces
{
    public interface IPlayerProvider
    {
        Task<Player> GetPlayerById(string playerId, bool loadPlacedQuarters = false);

        Task<Player> GetPlayerByChatId(long chatId);

        Task<List<Player>> GetPlayersByLobbyId(string lobbyId, bool loadPlacedQuarters = false);

        Task SavePlayer(Player player);

        Task SavePlayers(IEnumerable<Player> players);

        Task DeletePlayer(string playerId);
    }
}
