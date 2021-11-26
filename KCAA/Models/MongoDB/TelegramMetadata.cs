using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class TelegramMetadata : MongoDbObject
    {
        [BsonIgnoreIfDefault]
        public long ChatId { get; set; }

        [BsonIgnoreIfDefault]
        public int LobbyInfoMessageId { get; set; }
    }
}
