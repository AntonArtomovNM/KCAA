using System;
using System.Collections.Generic;
using KCAA.Models.MongoDB;
using KCAA.Services.Interfaces;
using MongoDB.Driver;

namespace KCAA.Services.Providers
{
    public class PlayerProvider : MongoDbProviderBase<Player>, IPlayerProvider
    {
        private readonly IMongoCollection<Player> _mongoCollection;

        public PlayerProvider(IMongoDatabase mongoDatabase)
        {
            _mongoCollection = mongoDatabase.GetCollection<Player>(Player.TableName);
        }

        public void CreatePlayer(Player player)
        {
            _mongoCollection.InsertOne(player);
        }

        public Player GetPlayerById(Guid playerId)
        {
            return _mongoCollection.Find(GetIdFilter(playerId)).FirstOrDefault();
        }

        public Player GetPlayerByChatId(string chatId)
        {
            return _mongoCollection.Find(x => x.ChatId == chatId).FirstOrDefault();
        }

        public List<Player> GetPlayersByRoomId(Guid roomId)
        {
            return _mongoCollection.Find(x => x.RoomId == roomId)?.ToList() ?? new List<Player>();
        }

        public void SavePlayer(Player player)
        {
            var update = Builders<Player>.Update
                .Set(x => x.Name, player.Name)
                .Set(x => x.RoomId, player.RoomId)
                .Set(x => x.ChatId, player.ChatId)
                .Set(x => x.IsHost, player.IsHost)
                .Set(x => x.HasCrown, player.HasCrown)
                .Set(x => x.Score, player.Score)
                .Set(x => x.Coins, player.Coins)
                .Set(x => x.Characters, player.Characters)
                .Set(x => x.Cards, player.Cards)
                .Set(x => x.ActiveCards, player.ActiveCards);

            _mongoCollection.UpdateOne(GetIdFilter(player.Id), update);
        }

        public void SavePlayerV2(Player player)
        {
            _mongoCollection.ReplaceOne(GetIdFilter(player.Id), player);
        }

        public void DeletePlayer(Guid playerId)
        {
            _mongoCollection.DeleteOne(x => x.Id == playerId);
        }
    }
}
