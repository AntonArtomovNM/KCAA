using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class Lobby : MongoDbObject
    {
        public static string TableName => "lobby";

        [BsonRequired]
        public List<string> QuarterDeck { get; set; }

        [BsonRequired]
        public List<Character> CharacterDeck { get; set; }

        [BsonRequired]
        public LobbyStatus Status { get; set; }

        public int PlayersCount { get; set; }

        [BsonIgnoreIfNull]
        public LobbyTelegramMetadata TelegramMetadata { get; set; }

        public Lobby()
        {
            Status = LobbyStatus.Configuring;
        }
    }
}
