using System.Threading.Tasks;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Interfaces
{
    public interface ILobbyProvider
    {
        Task CreateLobby(Lobby lobby);

        Lobby GetLobbyByChatId(long chatId);

        Task SaveLobby(Lobby lobby);

        Task DeleteLobby(Lobby lobby);

        Task DeleteLobbyByChatId(long chatId);
    }
}
