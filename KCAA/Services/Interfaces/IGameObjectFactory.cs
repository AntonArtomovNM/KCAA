using System.Threading.Tasks;
using KCAA.Models;

namespace KCAA.Services.Interfaces
{
    public interface IGameObjectFactory<T> where T: GameObject
    {
        T GetGameObject(string name);

        Task RegisterGameObject(T gameObj);
    }
}
