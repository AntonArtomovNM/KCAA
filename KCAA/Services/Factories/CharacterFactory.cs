using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models.Characters;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Factories
{
    public class CharacterFactory : ICardFactory<CharacterBase>
    {
        private readonly Dictionary<string, CharacterBase> characters = new();

        public CharacterBase GetCard(string name) => characters[name];

        public Task RegisterCard(CharacterBase character)
        {
            characters.Add(character.Name, character);

            return Task.CompletedTask;
        }
    }
}
