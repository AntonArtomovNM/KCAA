using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models.Cards;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Factories
{
    public class CardFactory : IGameObjectFactory<Card>
    {
        private readonly Dictionary<string, Card> cards = new();

        public Card GetGameObject(string name) => cards[name];

        public Task RegisterGameObject(Card card)
        {
            cards.Add(card.Name, card);

            return Task.CompletedTask;
        }
    }
}
