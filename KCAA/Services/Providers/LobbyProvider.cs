using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using KCAA.Services.Interfaces;
using KCAA.Models.MongoDB;

namespace KCAA.Services.Providers
{
    public class LobbyProvider : MongoDbProviderBase<Lobby>, ILobbyProvider
    {
        private readonly IMongoCollection<Lobby> _mongoCollection;

        public LobbyProvider(IMongoDatabase mongoDatabase)
        {
            _mongoCollection = mongoDatabase.GetCollection<Lobby>(Lobby.TableName);
        }

        public async Task CreateLobby(Lobby lobby)
        {
            await _mongoCollection.InsertOneAsync(lobby);
        }

        public async Task<Lobby> GetLobbyById(string lobbyId)
        {
            return (await _mongoCollection.FindAsync(x => x.Id == lobbyId)).FirstOrDefault();
        }

        public async Task<Lobby> GetLobbyByChatId(long chatId)
        {
            return (await _mongoCollection.FindAsync(x => x.TelegramMetadata.ChatId == chatId)).FirstOrDefault();
        }

        public async Task UpdateLobby<T>(string lobbyId, Expression<Func<Lobby, T>> updateFunc, T value)
        {
            var update = Builders<Lobby>.Update.Set(updateFunc, value);

            await _mongoCollection.UpdateOneAsync(GetIdFilter(lobbyId), update);
        }

        public async Task SaveLobby(Lobby lobby)
        {
            await _mongoCollection.ReplaceOneAsync(GetIdFilter(lobby.Id), lobby);
        }

        public async Task DeleteLobby(Lobby lobby)
        {
            await _mongoCollection.DeleteOneAsync(GetIdFilter(lobby.Id));
        }

        public async Task DeleteLobbyByChatId(long chatId)
        {
            await _mongoCollection.DeleteOneAsync(x => x.TelegramMetadata.ChatId == chatId);
        }
    }
}
