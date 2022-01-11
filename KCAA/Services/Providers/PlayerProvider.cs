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

        public async Task<Player> GetPlayerById(string playerId)
        {
            return (await _mongoCollection.FindAsync(GetIdFilter(playerId))).FirstOrDefault();
        }

        public async Task<Player> GetPlayerByChatId(long chatId)
        {
            return (await _mongoCollection.FindAsync(x => x.TelegramMetadata.ChatId == chatId)).FirstOrDefault();
        }

        public async Task<List<Player>> GetPlayersByLobbyId(string lobbyId)
        {
            return (await _mongoCollection.FindAsync(x => x.LobbyId == lobbyId))?.ToList() ?? new List<Player>();
        }

        public async Task SavePlayer(Player player)
        {
            if (string.IsNullOrWhiteSpace(player.Id))
            {
                player.Id = Guid.NewGuid().ToString();
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

        public async Task DeletePlayer(string playerId)
        {
            await _mongoCollection.DeleteOneAsync(GetIdFilter(playerId));
        }
    }
}
