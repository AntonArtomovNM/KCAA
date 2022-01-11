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

        public string LobbyId { get; set; } = Guid.Empty.ToString();

        public bool IsHost { get; set; }

        public bool HasCrown { get; set; }

        /// <summary>
        /// Character selection order
        /// </summary>
        public int CSOrder { get; set; }

        public int Coins { get; set; }

        public List<string> CharacterHand { get; set; } = new List<string>();

        public List<string> QuarterHand { get; set; } = new List<string>();

        public List<PlacedQuarter> ActiveQuarters { get; set; }

        [BsonIgnoreIfNull]
        public PlayerTelegramMetadata TelegramMetadata { get; set; }
    }
}
