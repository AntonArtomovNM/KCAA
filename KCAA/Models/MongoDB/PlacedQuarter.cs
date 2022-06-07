using KCAA.Models.Quarters;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class PlacedQuarter
    {
        [BsonRequired]
        public string Name { get; set; }

        public int BonusScore { get; set; }

        [BsonIgnore]
        public Quarter QuarterBase { get; set; }

        public PlacedQuarter(string name)
        {
            Name = name;
        }
    }
}
