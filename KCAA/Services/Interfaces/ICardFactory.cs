using System.Threading.Tasks;
using KCAA.Models;

namespace KCAA.Services.Interfaces
{
    public interface ICardFactory<T> where T: CardObject
    {
        T GetCard(string name);

        Task RegisterCard(T gameObj);
    }
}
