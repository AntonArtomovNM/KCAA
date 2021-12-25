using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Interfaces
{
    public interface ILobbyProvider
    {
        Task CreateLobby(Lobby lobby);

        Task<Lobby> GetLobbyById(string lobbyId);

        Task<Lobby> GetLobbyByChatId(long chatId);

        /// <summary>
        /// For updating a specific field
        /// </summary>
        Task UpdateLobby<T>(string lobbyId, Expression<Func<Lobby, T>> updateFunc, T value);

        /// <summary>
        /// For updating all fields
        /// </summary>
        Task SaveLobby(Lobby lobby);

        Task DeleteLobby(Lobby lobby);

        Task DeleteLobbyByChatId(long chatId);
    }
}
