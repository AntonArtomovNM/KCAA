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

        public Lobby GetLobbyByChatId(long chatId)
        {
            return _mongoCollection.Find(x => x.TelegramMetadata.ChatId == chatId).FirstOrDefault();
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
