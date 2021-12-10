using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class Lobby : MongoDbObject
    {
        public static string TableName => "lobby";

        [BsonRequired]
        public IEnumerable<string> CardDeck { get; set; }

        [BsonRequired]
        public IEnumerable<CharacterDto> CharacterDeck { get; set; }

        [BsonRequired]
        public LobbyStatus Status { get; set; }

        public int PlayersCount { get; set; }

        [BsonIgnoreIfNull]
        public TelegramMetadata TelegramMetadata { get; set; }

        public Lobby()
        {
            Status = LobbyStatus.Configuring;
        }
    }
}
