using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using KCAA.Models;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Builders
{
    public class GameObjectBuilder<T> : IGameObjectBuilder<T> where T: GameObject
    {
        public async Task<IEnumerable<T>> GetObjectFromSettings(string filePath)
        {
            IEnumerable<T> gameObjects;

            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    gameObjects = await JsonSerializer.DeserializeAsync<IEnumerable<T>>(stream);
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[Error] An error occurred during {typeof(T).Name} deserialization: {ex}");

                gameObjects = new List<T>();
            }

            return gameObjects;
        }
    }
}
