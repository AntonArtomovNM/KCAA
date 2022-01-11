using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models.Quarters;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Factories
{
    public class QuarterFactory : ICardFactory<Quarter>
    {
        private readonly Dictionary<string, Quarter> quarters = new();

        public Quarter GetCard(string name) => quarters[name];

        public Task RegisterCard(Quarter card)
        {
            quarters.Add(card.Name, card);

            return Task.CompletedTask;
        }
    }
}
