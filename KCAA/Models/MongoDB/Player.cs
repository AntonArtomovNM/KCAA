using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class Player : MongoDbObject
    {
        public static string TableName => "player";

        [BsonRequired]
        public string Name { get; set; }

        public Guid LobbyId { get; set; }

        [BsonIgnoreIfNull]
        public long ChatId { get; set; }

        public bool IsHost { get; set; }

        public bool HasCrown { get; set; }

        public int Coins { get; set; }

        public IEnumerable<string> Characters { get; set; }

        public IEnumerable<string> CardHand { get; set; }

        public IEnumerable<ActiveCardDto> ActiveCards { get; set; }
    }
}
