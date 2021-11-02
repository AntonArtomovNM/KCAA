using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models.Characters;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Factories
{
    public class CharacterFactory : IGameObjectFactory<Character>
    {
        private readonly Dictionary<string, Character> characters = new();

        public Character GetGameObject(string name) => characters[name];

        public Task RegisterGameObject(Character character)
        {
            characters.Add(character.Name, character);

            return Task.CompletedTask;
        }
    }
}
