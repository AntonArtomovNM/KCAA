using System.Collections.Generic;
using System.Threading.Tasks;

namespace KCAA.Services.Interfaces
{
    public interface IGameObjectBuilder<T> 
    {
        Task<IEnumerable<T>> GetObjectsFromSettings(string filePath);
    }
}
