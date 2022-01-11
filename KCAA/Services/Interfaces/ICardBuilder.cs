using System.Collections.Generic;
using System.Threading.Tasks;

namespace KCAA.Services.Interfaces
{
    public interface ICardBuilder<T> 
    {
        Task<IEnumerable<T>> GetCardsFromSettings(string filePath);
    }
}
