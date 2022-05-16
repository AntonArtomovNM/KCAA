namespace KCAA.Models.Characters
{
    public class CharacterBase : CardObject
    {
        public string PhotoWithDescriptionUri { get; init; }

        public int Order { get; init; }

        public int BuildingCapacity { get; init; }
    }
}
