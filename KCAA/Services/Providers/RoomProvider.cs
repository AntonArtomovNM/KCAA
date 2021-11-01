using System;
using MongoDB.Driver;
using KCAA.Services.Interfaces;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Providers
{
    public class RoomProvider : MongoDbProviderBase<Room>, IRoomProvider
    {
        private readonly IMongoCollection<Room> _mongoCollection;

        public RoomProvider(IMongoDatabase mongoDatabase)
        {
            _mongoCollection = mongoDatabase.GetCollection<Room>(Room.TableName);
        }

        public void CreateRoom(Room room)
        {
            _mongoCollection.InsertOne(room);
        }

        public Room GetRoomById(Guid roomId)
        {
            return _mongoCollection.Find(GetIdFilter(roomId)).FirstOrDefault();
        }

        public Room GetRoomByChatId(string chatId)
        {
            return _mongoCollection.Find(x => x.ChatId == chatId).FirstOrDefault();
        }

        public void SaveRoom(Room room)
        {
            var update = Builders<Room>.Update
                .Set(x => x.Status, room.Status)
                .Set(x => x.CardDeck, room.CardDeck)
                .Set(x => x.CharacterDeck, room.CharacterDeck);

            _mongoCollection.UpdateOne(GetIdFilter(room.Id), update);
        }

        public void DeleteRoom(Guid roomId)
        {
            _mongoCollection.DeleteOne(x => x.Id == roomId);
        }
    }
}
