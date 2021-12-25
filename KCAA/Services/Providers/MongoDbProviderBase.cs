using System;
using KCAA.Models.MongoDB;
using MongoDB.Driver;

namespace KCAA.Services.Providers
{
    public abstract class MongoDbProviderBase<T> where T : MongoDbObject
    {
        private readonly FilterDefinitionBuilder<T> _filterBuilder = Builders<T>.Filter;

        protected FilterDefinition<T> GetIdFilter(string id)
        {
            return _filterBuilder.Eq(x => x.Id, id);
        }
    }
}
