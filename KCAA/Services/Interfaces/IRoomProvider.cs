using System;
using System.Threading.Tasks;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Interfaces
{
    interface IRoomProvider
    {
        void CreateRoom(Room room);

        Room GetRoomById(Guid roomId);

        Room GetRoomByChatId(string chatId);

        void SaveRoom(Room room);

        void DeleteRoom(Guid roomId);
    }
}
