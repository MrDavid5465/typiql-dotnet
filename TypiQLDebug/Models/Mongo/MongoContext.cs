using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TypiQLDebug.Models.Mongo.Types;

namespace TypiQLDebug.Models.Mongo
{
    public class MongoContext
    {
        public readonly IMongoDatabase _configDataBase = null;
        public readonly IMongoDatabase _feedmeDataBase = null;
        public MongoClient client;
        public MongoContext(string connectionString, string database)
        {
            client = new MongoClient(connectionString);
            if (client != null)
            {
                _configDataBase = client.GetDatabase(database);
            }
        }
        public IMongoCollection<User> Users
        {
            get
            {
                return _configDataBase.GetCollection<User>("users");
            }
        }
        public IMongoCollection<UserADMock> UsersADMock
        {
            get
            {
                return _configDataBase.GetCollection<UserADMock>("usersadmock");
            }
        }
        public IMongoCollection<GroupADMock> GroupsADMock
        {
            get
            {
                return _configDataBase.GetCollection<GroupADMock>("groupsadmock");
            }
        }

    }
}
