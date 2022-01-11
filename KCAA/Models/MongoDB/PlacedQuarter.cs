using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class PlacedQuarter
    {
        [BsonRequired]
        public string Name { get; set; }

        public int BonusScore { get; set; }
    }
}
