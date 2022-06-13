namespace KCAA.Models
{
    public abstract class CardObject
    {
        private string _photoWithDescription;

        public string Name { get; init; }

        public string DisplayName { get; init; }

        public ColorType Type { get; init; }

        public string PhotoUri { get; init; }

        public string PhotoWithDescriptionUri 
        { 
            get 
            {
                return _photoWithDescription ?? PhotoUri;
            } 
            init
            {
                _photoWithDescription = value;
            } 
        }

        public string Description { get; init; }

        public string GameAction { get; set; }
    }
}
