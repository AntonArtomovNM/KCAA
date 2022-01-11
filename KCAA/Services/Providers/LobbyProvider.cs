using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using KCAA.Services.Interfaces;
using KCAA.Models.MongoDB;
using KCAA.Models.Characters;
using System.Linq;

namespace KCAA.Services.Providers
{
    public class LobbyProvider : MongoDbProviderBase<Lobby>, ILobbyProvider
    {
        private readonly IMongoCollection<Lobby> _mongoCollection;
        private readonly ICardFactory<CharacterBase> _characterFactory;

        public LobbyProvider(IMongoDatabase mongoDatabase, ICardFactory<CharacterBase> characterFactory)
        {
            _mongoCollection = mongoDatabase.GetCollection<Lobby>(Lobby.TableName);
            _characterFactory = characterFactory;
        }

        public async Task CreateLobby(Lobby lobby)
        {
            await _mongoCollection.InsertOneAsync(lobby);
        }

        public async Task<Lobby> GetLobbyById(string lobbyId)
        {
            var lobby = (await _mongoCollection.FindAsync(x => x.Id == lobbyId)).FirstOrDefault();

            if (lobby?.CharacterDeck != null)
            {
                SetCharacterDeck(lobby);
            }

            return lobby;
        }

        public async Task<Lobby> GetLobbyByChatId(long chatId)
        {
            var lobby = (await _mongoCollection.FindAsync(x => x.TelegramMetadata.ChatId == chatId)).FirstOrDefault();

            if (lobby?.CharacterDeck != null)
            {
                SetCharacterDeck(lobby);
            }

            return lobby;
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

        private void SetCharacterDeck(Lobby lobby)
        {
            lobby.CharacterDeck.AsParallel().WithDegreeOfParallelism(5).ForAll(dto => dto.CharacterBase = _characterFactory.GetCard(dto.Name));
        }
    }
}
