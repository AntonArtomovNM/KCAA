using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class Room : MongoDbObject
    {
        public static string TableName => "room";

        [BsonRequired]
        public IEnumerable<string> CardDeck { get; set; }

        [BsonRequired]
        public IEnumerable<CharacterDto> CharacterDeck { get; set; }

        [BsonRequired]
        public RoomStatus Status { get; set; }

        [BsonIgnoreIfNull]
        public string ChatId { get; set; }
    }
}
