namespace KCAA.Models
{
    public abstract class CardObject
    {
        public string Name { get; init; }

        public string DisplayName { get; init; }

        public ColorType Type { get; init; }

        public string PhotoUri { get; init; }

        public string Description { get; init; }

        public GameAction Action { get; set; }
    }
}
