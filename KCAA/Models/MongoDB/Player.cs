using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class Player : MongoDbObject
    {
        public static string TableName => "player";

        [BsonRequired]
        public string Name { get; set; }

        public string LobbyId { get; set; } = Guid.Empty.ToString();

        public bool HasCrown { get; set; }

        /// <summary>
        /// Character selection order
        /// </summary>
        public int CSOrder { get; set; }

        public int Coins { get; set; }

        public int Score { get; set; }

        [BsonIgnore]
        public int FullScore => Score + PlacedQuarters.Sum(pq => pq.BonusScore);

        public List<string> CharacterHand { get; set; } = new List<string>();

        public List<string> QuarterHand { get; set; } = new List<string>();

        public List<PlacedQuarter> PlacedQuarters { get; set; } = new List<PlacedQuarter>();

        public List<string> GameActions = new List<string>();

        [BsonIgnoreIfNull]
        public PlayerTelegramMetadata TelegramMetadata { get; set; }
    }
}
