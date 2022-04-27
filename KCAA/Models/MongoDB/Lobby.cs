using System;
using System.Collections.Generic;
using System.Linq;
using KCAA.Models.Characters;
using MongoDB.Bson.Serialization.Attributes;

namespace KCAA.Models.MongoDB
{
    public class Lobby : MongoDbObject
    {
        public static string TableName => "lobby";

        [BsonRequired]
        public List<string> QuarterDeck { get; set; }

        [BsonRequired]
        public List<Character> CharacterDeck { get; set; }

        [BsonRequired]
        public LobbyStatus Status { get; set; }

        public int PlayersCount { get; set; }

        [BsonIgnoreIfNull]
        public LobbyTelegramMetadata TelegramMetadata { get; set; }

        public Lobby()
        {
            Status = LobbyStatus.Configuring;
        }

        public string DrawQuarter()
        {
            if (!QuarterDeck.Any())
            {
                GenerateBasicQuarterDeck();
            }

            var rand = new Random();
            var index = rand.Next(0, QuarterDeck.Count());

            var quarter = QuarterDeck.ElementAt(index);
            QuarterDeck.RemoveAt(index);

            return quarter;
        }

        public void GenerateBasicQuarterDeck()
        {
            QuarterDeck = new List<string>();

            // 4 (i) color types with 5 (j) cost types repeated 3 (k) times 
            for (int k = 0; k < 3; k++)
            {
                for (int i = 1; i < 5; i++)
                {
                    for (int j = 1; j <= 5; j++)
                    {
                        QuarterDeck.Add($"{Enum.GetName(typeof(ColorType), i).First()}{j}");
                    }
                }
            }
        }

        public void GenerateCharacterDeck()
        {
            CharacterDeck =  new List<Character>
            {
                new (CharacterNames.Assassin),
                new (CharacterNames.Thief),
                new (CharacterNames.Magician),
                new (CharacterNames.King),
                new (CharacterNames.Bishop),
                new (CharacterNames.Merchant),
                new (CharacterNames.Architect),
                new (CharacterNames.Warlord),
                new (CharacterNames.Beggar)
            };
        }
    }
}
