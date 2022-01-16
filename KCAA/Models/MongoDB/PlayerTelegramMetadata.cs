using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace KCAA.Models.MongoDB
{
    public class PlayerTelegramMetadata
    {
        [BsonId]
        public long ChatId { get; set; }

        public List<int> CardMessageIds { get; set; } = new List<int>();

        public int GameActionKeyboardId { get; set; }
    }
}
