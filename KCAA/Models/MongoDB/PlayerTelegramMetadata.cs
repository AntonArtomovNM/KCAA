using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace KCAA.Models.MongoDB
{
    public class PlayerTelegramMetadata
    {
        [BsonId]
        public long ChatId { get; set; }

        public List<int> CharacterInfoMessageIds { get; set; } = new List<int>();
    }
}
