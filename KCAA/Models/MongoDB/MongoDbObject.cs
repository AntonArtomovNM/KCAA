using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public abstract class MongoDbObject
    {
        [BsonId]
        public string Id { get; set; }
    }
}
