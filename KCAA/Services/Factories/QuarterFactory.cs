using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Models.Quarters;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Factories
{
    public class QuarterFactory : ICardFactory<Quarter>
    {
        private readonly Dictionary<string, Quarter> quarters = new();

        public Quarter GetCard(string name) => quarters[name];

        public IEnumerable<string> GetFilteredNames(Predicate<Quarter> filter)
        {
            return quarters.Where(q => filter(q.Value)).Select(q => q.Key);
        }

        public Task RegisterCard(Quarter card)
        {
            quarters.Add(card.Name, card);

            return Task.CompletedTask;
        }
    }
}
