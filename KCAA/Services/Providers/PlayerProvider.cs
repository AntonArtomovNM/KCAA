using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using KCAA.Models.MongoDB;
using KCAA.Models.Quarters;
using KCAA.Services.Interfaces;
using MongoDB.Driver;

namespace KCAA.Services.Providers
{
    public class PlayerProvider : MongoDbProviderBase<Player>, IPlayerProvider
    {
        private readonly IMongoCollection<Player> _mongoCollection;
        private readonly ICardFactory<Quarter> _quarterFactory;

        public PlayerProvider(IMongoDatabase mongoDatabase, ICardFactory<Quarter> quarterFactory)
        {
            _mongoCollection = mongoDatabase.GetCollection<Player>(Player.TableName);
            _quarterFactory = quarterFactory;
        }

        public async Task<Player> GetPlayerById(string playerId, bool loadPlacedQuarters = false)
        {
            var player = (await _mongoCollection.FindAsync(GetIdFilter(playerId))).FirstOrDefault();
            
            if (loadPlacedQuarters && player?.PlacedQuarters != null)
            {
                SetPlacedQuarters(player);
            }

            return player;
        }

        public async Task<Player> GetPlayerByChatId(long chatId, bool loadPlacedQuarters = false)
        {
            var player = (await _mongoCollection.FindAsync(x => x.TelegramMetadata.ChatId == chatId)).FirstOrDefault();

            if (loadPlacedQuarters && player?.PlacedQuarters != null)
            {
                SetPlacedQuarters(player);
            }

            return player;
        }

        public async Task<List<Player>> GetPlayersByLobbyId(string lobbyId, bool loadPlacedQuarters = false)
        {
            var players = (await _mongoCollection.FindAsync(x => x.LobbyId == lobbyId))?.ToList() ?? new List<Player>();

            if (loadPlacedQuarters)
            {
                players.AsParallel().WithDegreeOfParallelism(2).ForAll(p => SetPlacedQuarters(p));
            }

            return players;
        }

        public async Task UpdatePlayer<T>(string playerId, Expression<Func<Player, T>> updateFunc, T value)
        {
            var update = Builders<Player>.Update.Set(updateFunc, value);

            await _mongoCollection.UpdateOneAsync(GetIdFilter(playerId), update);
        }

        public async Task SavePlayer(Player player)
        {
            if (string.IsNullOrWhiteSpace(player.Id))
            {
                player.Id = Guid.NewGuid().ToString().Replace("-", "");
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

        private void SetPlacedQuarters(Player player)
        {
            player.PlacedQuarters.AsParallel().WithDegreeOfParallelism(3).ForAll(q => q.QuarterBase = _quarterFactory.GetCard(q.Name));
        }
    }
}
