using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Interfaces
{
    public interface IPlayerProvider
    {
        Task<Player> GetPlayerById(string playerId);

        Task<Player> GetPlayerByChatId(long chatId);

        Task<List<Player>> GetPlayersByLobbyId(string lobbyId);

        Task SavePlayer(Player player);

        Task SavePlayers(IEnumerable<Player> players);

        Task DeletePlayer(string playerId);
    }
}
