using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class LobbyTelegramMetadata
    {
        [BsonId]
        public long ChatId { get; set; }

        [BsonIgnoreIfDefault]
        public int LobbyInfoMessageId { get; set; }
    }
}
