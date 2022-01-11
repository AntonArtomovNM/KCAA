using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using KCAA.Models;
using KCAA.Services.Interfaces;

namespace KCAA.Services.Builders
{
    public class CardBuilder<T> : ICardBuilder<T> where T: CardObject
    {
        public async Task<IEnumerable<T>> GetCardsFromSettings(string filePath)
        {
            IEnumerable<T> cards;

            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    cards = await JsonSerializer.DeserializeAsync<IEnumerable<T>>(stream);
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[Error] An error occurred during {typeof(T).Name} card deserialization: {ex}");

                cards = new List<T>();
            }

            return cards;
        }
    }
}
