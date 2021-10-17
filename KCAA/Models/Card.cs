namespace KCAA.Models
{
    public class Card
    {
        public string Name { get; init; }

        public string DisplayName { get; init; }

        public ColorType Type { get; init; }

        public string PhotoUri { get; init; }

        public int Cost { get; init; }

        public int BonusScore { get; set; }

        public string Description { get; init; }

        public GameAction Action { get; set; }
    }
}
