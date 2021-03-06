using KCAA.Models.Characters;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class Character
    {
        [BsonId]
        public string Name { get; set; }

        [BsonRequired]
        public CharacterStatus Status { get; set; }

        [BsonRequired]
        public CharacterEffect Effect { get; set; }

        [BsonRequired]
        public int BuiltQuarters { get; set; }

        [BsonIgnore]
        public CharacterBase CharacterBase { get; set; } 

        public Character(string name)
        {
            Name = name;
            Status = CharacterStatus.Awailable;
            Effect = CharacterEffect.None;
        }
    }
}
