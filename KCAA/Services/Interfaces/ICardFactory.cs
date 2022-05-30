using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KCAA.Models;

namespace KCAA.Services.Interfaces
{
    public interface ICardFactory<T> where T: CardObject
    {
        T GetCard(string name);

        IEnumerable<string> GetFilteredNames (Predicate<T> filter);

        Task RegisterCard(T gameObj);
    }
}
