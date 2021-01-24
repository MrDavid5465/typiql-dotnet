using DataCrush.TypiQL.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DataCrush.TypiQL.Models.Mongo
{
    public class TypiQLMongoContext
    {
        public readonly IMongoDatabase _configDataBase = null;
        public MongoClient client;
        public string adminRole;
        public TypiQLMongoContext(IOptions<TypiQLSettings> settings)
        {
            client = new MongoClient(settings.Value.ConnectionString);
            if (client != null)
                _configDataBase = client.GetDatabase(settings.Value.Database);
            adminRole = settings.Value.AdminRole;
        }
        public IMongoCollection<Types> Types
        {
            get
            {
                return _configDataBase.GetCollection<Types>("types");
            }
        }
        public IMongoCollection<Connection> Connections
        {
            get
            {
                return _configDataBase.GetCollection<Connection>("connections");
            }
        }
        public IMongoCollection<Server> Server
        {
            get
            {
                return _configDataBase.GetCollection<Server>("server");
            }
        }
        public IMongoCollection<Bucket> Buckets
        {
            get
            {
                return _configDataBase.GetCollection<Bucket>("buckets");
            }
        }
    }
}
