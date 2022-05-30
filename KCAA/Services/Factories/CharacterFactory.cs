using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KCAA.Models.Characters;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Factories
{
    public class CharacterFactory : ICardFactory<CharacterBase>
    {
        private readonly Dictionary<string, CharacterBase> characters = new();

        public CharacterBase GetCard(string name) => characters[name];

        public IEnumerable<string> GetFilteredNames(Predicate<CharacterBase> filter)
        {
            return characters.Where(c => filter(c.Value)).Select(c => c.Key);
        }

        public Task RegisterCard(CharacterBase character)
        {
            characters.Add(character.Name, character);

            return Task.CompletedTask;
        }
    }
}
