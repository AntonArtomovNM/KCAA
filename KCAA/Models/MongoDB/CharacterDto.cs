using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class CharacterDto : MongoDbObject
    {
        [BsonRequired]
        public string Name { get; set; }

        [BsonRequired]
        public CharacterStatus Status { get; set; }

        [BsonRequired]
        public CharacterEffect Effect { get; set; }
    }
}
