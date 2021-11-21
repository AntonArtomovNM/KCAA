using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        public Player GetPlayerById(Guid playerId)
        {
            return _mongoCollection.Find(GetIdFilter(playerId)).FirstOrDefault();
        }

        public Player GetPlayerByChatId(long chatId)
        {
            return _mongoCollection.Find(x => x.ChatId == chatId).FirstOrDefault();
        }

        public List<Player> GetPlayersByLobbyId(Guid roomId)
        {
            return _mongoCollection.Find(x => x.LobbyId == roomId)?.ToList() ?? new List<Player>();
        }

        public async Task SavePlayer(Player player)
        {
            if (player.Id == Guid.Empty)
            {
               await _mongoCollection.InsertOneAsync(player);
            }
            else
            {
                await _mongoCollection.ReplaceOneAsync(GetIdFilter(player.Id), player);
            }
        }

        public async Task SavePlayers(IEnumerable<Player> players)
        {
            foreach (var player in players)
            {
                await _mongoCollection.ReplaceOneAsync(GetIdFilter(player.Id), player);
            }
        }

        public async Task DeletePlayer(Guid playerId)
        {
            await _mongoCollection.DeleteOneAsync(GetIdFilter(playerId));
        }
    }
}
