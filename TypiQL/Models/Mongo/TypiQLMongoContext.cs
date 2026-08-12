using DataCrush.TypiQL.Models;
using DnsClient;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TypiQL.Models;

namespace DataCrush.TypiQL.Models.Mongo
{
    public class TypiQLMongoContext
    {
        public readonly IMongoDatabase _configDataBase = null;
        public MongoClient client;
        public string adminRole;
        public TypiQLMongoContext(TypiQLSettings settings)
        {
            client = new MongoClient(settings.TypiQLConnectionString);
            if (client != null)
                _configDataBase = client.GetDatabase(settings.TypiQLDatabase);
            adminRole = settings.TypiQLAdminRole;
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
        public IMongoCollection<LoggingContext> Logs
        {
            get
            {
                return _configDataBase.GetCollection<LoggingContext>("logs");
            }
        }
        public IMongoCollection<ConfigBackup> ConfigBackups
        {
            get
            {
                return _configDataBase.GetCollection<ConfigBackup>("configBackups");
            }

        }
    }
}
