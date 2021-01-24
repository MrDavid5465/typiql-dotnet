using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using DataCrush.TypiQL.Models.Mongo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using GraphQL.Utilities;
using GraphQL.Types;
using Newtonsoft.Json.Linq;
using DataCrush.TypiQL.Models.AD;
using System.DirectoryServices;
using Newtonsoft.Json;
using System.Drawing;
using System.IO;
using MongoDB.Driver.GridFS;
using System.Text.RegularExpressions;
using TypiQL.Models;

namespace DataCrush.TypiQL.Models
{
    public class ConfigData
    {
        private readonly TypiQLMongoContext _mongoContext;
        public readonly IMongoDatabase _database;
        private readonly IHttpContextAccessor _httpContext;
        public readonly ITypesRepo _typesRepo;
        public readonly IConnectionsRepo _connectionsRepo;
        public readonly Dictionary<string, ISubscriberRepo<dynamic>> _subscriptionRepos;
        public bool _live;
        public IOptions<TypiQLSettings> _settings;

        public ConfigData(IOptions<TypiQLSettings> settings, IHttpContextAccessor httpContext, TypesRepo typesRepo, ConnectionsRepo connectionsRepo, TypiQLMongoContext context)
        {
            _httpContext = httpContext;
            _mongoContext = context; //new MongoContext(settings);
            _database = context._configDataBase; //new MongoClient(settings.Value.ConnectionString).GetDatabase(settings.Value.Database);
            _typesRepo = typesRepo;
            _connectionsRepo = connectionsRepo;
            _subscriptionRepos = new Dictionary<string, ISubscriberRepo<dynamic>>();
            _settings = settings;
            foreach(Types t in GetTypes().Result)
            {
                _subscriptionRepos.Add(t.Name, new SubscriberRepo<dynamic>());
            }
        }
        public async Task<IObservable<Subscriber<Dictionary<string,dynamic>>>> Subscription(Types type, Dictionary<string, dynamic> filter = null)
        {
            if (filter == null) 
            {
                filter = new Dictionary<string, dynamic>();
            }
            return await _subscriptionRepos[type.Name].Subscription(type, filter);
        }
        public async Task<IObservable<Subscriber<dynamic>>> SubscribeToType(Types type)
        {
            return await _subscriptionRepos[type.Name].SubscribeToType(type);
        }
        public async Task<List<SqlColumn>> GetSqlColumns(string connectionId, string table)
        {
            var connections = new Dictionary<string, Connection>();
            foreach (Connection c in await GetConnections("sql"))
            {
                connections.Add(c.Id.ToString(), c);
            }
            string query = "SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table";

            using (SqlConnection connection = new SqlConnection(connections[connectionId].ConnectionString))
            {
                List<SqlColumn> result = await connection.QueryAsync<SqlColumn>(query, new Dictionary<string, dynamic> { { "table", table } }) as List<SqlColumn>;
                return result;
            }
        }
        public string GetUserName()
        {
            return _httpContext.HttpContext.User.Identity.Name;
        }
        public async Task<Connection> GetConnection(string name)
        {
            return await _mongoContext.Connections.Find(Builders<Connection>.Filter.Eq(c => c.Name, name)).FirstOrDefaultAsync();
        }
        public async Task<List<string>> GetDatabaseNames(string type, string connectionString)
        {
            switch (type)
            {
                case "mongo":
                    MongoClient client = new MongoClient(connectionString);
                    return client.ListDatabaseNames().ToList();
                case "sql":
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        Dictionary<string, dynamic> result = await connection.QueryAsync("SELECT name FROM master.sys.databases") as Dictionary<string, dynamic>;
                        List<string> names = new List<string>();
                        foreach (KeyValuePair<string, dynamic> kv in result)
                        {
                            names.Add(kv.Value);
                        }
                        return names;
                    }
                default:
                    return new List<string>();
            }
        }
        public async Task<List<Connection>> GetConnections()
        {
            return await _mongoContext.Connections.Find(Builders<Connection>.Filter.Empty).ToListAsync();
        }
        public async Task<List<Connection>> GetConnections(string type)
        {
            return await _mongoContext.Connections.Find(Builders<Connection>.Filter.Eq(c => c.Type, type)).ToListAsync();
        }

        public async Task<Connection> GetConnection(ObjectId id)
        {
            return await _mongoContext.Connections.Find(Builders<Connection>.Filter.Eq(c => c.Id, id)).FirstOrDefaultAsync();
        }
        public async Task<Connection> AddConnection(Connection values)
        {
            values.Id = ObjectId.GenerateNewId();
            if (values.Type == "ad")
            {
                List<Connection> connections = await GetConnections("ad");
                if (connections.Count == 0)
                {
                    await _mongoContext.Connections.InsertOneAsync(values);
                    await AddType(new Dictionary<string, dynamic>
                    {
                        { "name", "UserData" },
                        { "type", "mongo" },
                        { "schema", "type UserData { id: ID sid: String } input UserDataInput { sid: String }" },
                        {
                            "queries",
                            new List<Dictionary<string, dynamic>>
                            {
                                new Dictionary<string, dynamic>
                                {
                                    { "name", "myUserData" },
                                    { "schema", "myUserData: UserData" },
                                    { "type", "Get" },
                                    {
                                        "arguments",
                                        new List<Dictionary<string, dynamic>>
                                        {
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "sid" },
                                                { "type", "String" },
                                                { "key", "sid" },
                                                { "value", "@currentUser.sid" }
                                            }
                                        }
                                    },
                                    {
                                        "allowedGroups",
                                        new List<string>
                                        {
                                            { "Domain Users" }
                                        }
                                    }
                                },
                                new Dictionary<string, dynamic>
                                {
                                    { "name", "getUserData" },
                                    { "schema", "getUserData(userName: String!): UserData" },
                                    { "type", "Get" },
                                    {
                                        "arguments",
                                        new List<Dictionary<string, dynamic>>
                                        {
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "userName" },
                                                { "type", "String" },
                                                { "key", "sid" },
                                                { "value", "@user(userName).sid" }
                                            }
                                        }
                                    },
                                    {
                                        "allowedGroups",
                                        new List<string>
                                        {
                                            { "Domain Users" }
                                        }
                                    }
                                }
                            }
                        },
                        {
                            "mutations",
                            new List<Dictionary<string, dynamic>>
                            {
                                new Dictionary<string, dynamic>
                                {
                                    { "name", "updateMyUserData" },
                                    { "schema", "updateMyUserData(update: UserDataInput!): UserData" },
                                    { "type", "Update" },
                                    {
                                        "arguments",
                                        new List<Dictionary<string, dynamic>>
                                        {
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "sid" },
                                                { "type", "String" },
                                                { "key", "sid" },
                                                { "value", "@currentUser.sid" }
                                            },
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "update" },
                                                { "type", "Object" },
                                                { "key", "update" },
                                                { "value", "update" }
                                            },
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "sidUpdate" },
                                                { "type", "Object" },
                                                { "key", "update" },
                                                { "value", "{ \"sid\": \"@currentUser.sid\" }" }
                                            },
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "upsert" },
                                                { "type", "Boolean" },
                                                { "key", "_upsert" },
                                                { "value", "true" }
                                            }
                                        }
                                    },
                                    {
                                        "allowedGroups",
                                        new List<string>
                                        {
                                            { "Domain Users" }
                                        }
                                    }
                                },
                                new Dictionary<string, dynamic>
                                {
                                    { "name", "updateUserData" },
                                    { "schema", "updateUserData(userName: String!, update: UserDataInput!): UserData" },
                                    { "type", "Update" },
                                    {
                                        "arguments",
                                        new List<Dictionary<string, dynamic>>
                                        {
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "userName" },
                                                { "type", "String" },
                                                { "key", "sid" },
                                                { "value", "@user(userName).sid" }
                                            },
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "update" },
                                                { "type", "Object" },
                                                { "key", "update" },
                                                { "value", "update" }
                                            },
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "sidUpdate" },
                                                { "type", "Object" },
                                                { "key", "update" },
                                                { "value", "{ \"sid\": \"@user(userName).sid\" }" }
                                            },
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "upsert" },
                                                { "type", "Boolean" },
                                                { "key", "_upsert" },
                                                { "value", "true" }
                                            }
                                        }
                                    },
                                    {
                                        "allowedGroups",
                                        new List<string>
                                        {
                                            { "IT" }
                                        }
                                    }
                                }
                            }
                        },
                        { "connection", values.Id.ToString() },
                        {
                            "model",
                            new Dictionary<string, dynamic>
                            {
                                { "name", "userDatas" },
                                { "modelType", "collection" },
                                {
                                    "key",
                                    new List<string>
                                    {
                                        "sid"
                                    }
                                },
                                {
                                    "columns",
                                    new List<Dictionary<string, dynamic>>
                                    {
                                        new Dictionary<string,dynamic>
                                        {
                                            { "name", "id" },
                                            { "dataName", "_id" },
                                            { "columnType", "String" },
                                            { "columnGraphType", "ID" },
                                            { "arguments", new List<Dictionary<string,dynamic>>() },
                                            { "allowedGroups", new List<string>() }
                                        },
                                        new Dictionary<string,dynamic>
                                        {
                                            { "name", "sid" },
                                            { "dataName", "sid" },
                                            { "columnType", "String" },
                                            { "columnGraphType", "String" },
                                            { "arguments", new List<Dictionary<string,dynamic>>() },
                                            { "allowedGroups", new List<string>() }
                                        }
                                    }
                                }
                            }
                        }
                    });
                    await AddType(new Dictionary<string, dynamic>
                    {
                        { "name", "User" },
                        { "type", "ad" },
                        { "schema", "type User { sid: ID sAMAccountName: String distinguishedName: String data: UserData } input UserInput { data: UserDataInput }" },
                        {
                            "queries",
                            new List<Dictionary<string, dynamic>>
                            {
                                new Dictionary<string, dynamic>
                                {
                                    { "name", "my" },
                                    { "schema", "my: User" },
                                    { "type", "Get" },
                                    {
                                        "arguments",
                                        new List<Dictionary<string, dynamic>>
                                        {
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "userName" },
                                                { "type", "String" },
                                                { "key", "sAMAccountName" },
                                                { "value", "@currentUser" }
                                            }
                                        }
                                    },
                                    {
                                        "allowedGroups",
                                        new List<string>
                                        {
                                            { "Domain Users" }
                                        }
                                    }
                                },
                                new Dictionary<string, dynamic>
                                {
                                    { "name", "impersonate" },
                                    { "schema", "impersonate(userName: String!): User" },
                                    {
                                        "arguments",
                                        new List<Dictionary<string, dynamic>>
                                        {
                                            new Dictionary<string, dynamic>
                                            {
                                                { "name", "userName" },
                                                { "type", "String" },
                                                { "key", "sAMAccountName" },
                                                { "value", "userName" }
                                            }
                                        }
                                    },
                                    { "type", "Get" },
                                    {
                                        "allowedGroups",
                                        new List<string>
                                        {
                                            "IT"
                                        }
                                    }
                                }
                            }
                        },
                        { "mutations", new List<Dictionary<string, dynamic>>() },
                        { "connection", values.Id.ToString() },
                        {
                            "model",
                            new Dictionary<string, dynamic>
                            {
                                { "name", "" },
                                { "modelType", "ADObject" },
                                {
                                    "key",
                                    new List<string>
                                    {
                                        "sAMAccountName"
                                    }
                                },
                                {
                                    "columns",
                                    new List<Dictionary<string, dynamic>>
                                    {
                                        new Dictionary<string,dynamic>
                                        {
                                            { "name", "sid" },
                                            { "dataName", "objectSid" },
                                            { "columnType", "String" },
                                            { "columnGraphType", "ID" },
                                            { "arguments", new List<Dictionary<string,dynamic>>() },
                                            { "allowedGroups", new List<string>() }
                                        },
                                        new Dictionary<string,dynamic>
                                        {
                                            { "name", "sAMAccountName" },
                                            { "dataName", "sAMAccountName" },
                                            { "columnType", "String" },
                                            { "columnGraphType", "String" },
                                            { "arguments", new List<Dictionary<string,dynamic>>() },
                                            { "allowedGroups", new List<string>() }
                                        },
                                        new Dictionary<string,dynamic>
                                        {
                                            { "name", "distinguishedName" },
                                            { "dataName", "distinguishedName" },
                                            { "columnType", "String" },
                                            { "columnGraphType", "String" },
                                            { "arguments", new List<Dictionary<string,dynamic>>() },
                                            { "allowedGroups", new List<string>() }
                                        },
                                        new Dictionary<string,dynamic>
                                        {
                                            { "name", "data" },
                                            { "dataName", "" },
                                            { "columnType", "Object" },
                                            { "columnGraphType", "UserData" },
                                            {
                                                "arguments", new List<Dictionary<string,dynamic>>
                                                {
                                                    new Dictionary<string, dynamic>
                                                    {
                                                        { "name", "sid" },
                                                        { "type", "String" },
                                                        { "key", "sid" },
                                                        { "value", "sid" }
                                                    }
                                                }
                                            },
                                            { "allowedGroups", new List<string>() }
                                        }
                                    }
                                }
                            }
                        }
                    });
                }
            }
            else
            {
                await _mongoContext.Connections.InsertOneAsync(values);
            }
            _connectionsRepo.AddConnection(values);
            return await GetConnection(values.Id);
        }
        public async Task<Connection> UpdateConnection(ObjectId id, Dictionary<string, dynamic> connectionUpdate)
        {
            var filter = Builders<Connection>.Filter.Eq(c => c.Id, id);
            List<UpdateDefinition<Connection>> updates = new List<UpdateDefinition<Connection>>();
            foreach (KeyValuePair<string, dynamic> kv in connectionUpdate)
            {
                updates.Add(Builders<Connection>.Update.Set(kv.Key, kv.Value));
            }
            var update = Builders<Connection>.Update.Combine(updates);
            await _mongoContext.Connections.UpdateOneAsync(filter, update);
            var result = await GetConnection(id);
            _connectionsRepo.UpdateConnection(result);
            return result;
        }
        public async Task<Connection> RemoveConnection(ObjectId id)
        {
            Connection connection = await GetConnection(id);
            await _mongoContext.Connections.DeleteOneAsync(Builders<Connection>.Filter.Eq(c => c.Id, id));
            _connectionsRepo.RemoveConnection(connection);
            return connection;
        }



        public async Task<Types> GetTypesType(ObjectId id)
        {
            return await _mongoContext.Types.Find(Builders<Types>.Filter.Eq(c => c.Id, id)).FirstOrDefaultAsync();
        }
        public async Task<List<Types>> GetTypes(string type)
        {
            return await _mongoContext.Types.Find(Builders<Types>.Filter.Eq(t => t.Type, type)).ToListAsync();
        }
        public async Task<List<Types>> GetTypes(ObjectId connection)
        {
            return await _mongoContext.Types.Find(Builders<Types>.Filter.Eq(t => t.Connection, connection.ToString())).ToListAsync();
        }
        public async Task<Types> GetTypesType(string type)
        {
            var result = await _mongoContext.Types.Find(Builders<Types>.Filter.Eq(t => t.Name, type)).FirstOrDefaultAsync();
            if (result == null)
                return new Types { Model = new Model { Columns = new List<Column>() } };
            else return result;
        }
        public async Task<List<Types>> GetTypes()
        {
            return await _mongoContext.Types.Find(Builders<Types>.Filter.Empty).SortBy(t => t.Name).ToListAsync();
        }
        public dynamic InterpretValue(Dictionary<string, dynamic> argument)
        {
            if (argument["name"] == argument["value"])
            {
                return argument["value"];
            }
            else
            {
                switch (argument["type"])
                {
                    case "Int32":
                        {
                            return int.Parse(argument["value"]);
                        }
                    case "Float":
                        {
                            return float.Parse(argument["value"]);
                        }
                    case "Boolean":
                        {
                            return bool.Parse(argument["value"]);
                        }
                    case "List":
                    case "Object":
                        {
                            if (argument["value"][0] == '{' || argument["value"][0] == '[')
                            {
                                return BsonTypeMapper.MapToDotNetValue(BsonDocument.Parse(argument["value"]));
                            }
                            else
                            {
                                return argument["value"];
                            }
                        }
                    default: { return argument["value"]; }
                }
            }
        }
        public async Task<Types> AddType(Dictionary<string, dynamic> typeUpdate)
        {
            Types t = await GetTypesType(typeUpdate["name"]);
            if (t.Name == typeUpdate["name"])
            {
                throw new Exception($"Type with name {typeUpdate["name"]} already exists");
            }
            ObjectId Id = ObjectId.GenerateNewId();
            foreach (KeyValuePair<string, dynamic> types in typeUpdate)
            {
                if (types.Key == "queries")
                {
                    foreach (Dictionary<string, dynamic> query in types.Value)
                    {
                        foreach (Dictionary<string, dynamic> argument in query["arguments"])
                        {
                            argument["value"] = InterpretValue(argument);
                        }
                    }
                }
                else if (types.Key == "mutations")
                {
                    foreach (Dictionary<string, dynamic> mutation in types.Value)
                    {
                        foreach (Dictionary<string, dynamic> argument in mutation["arguments"])
                        {
                            argument["value"] = InterpretValue(argument);
                        }
                    }
                }
                else if (types.Key == "subscriptions")
                {
                    foreach (Dictionary<string, dynamic> subscription in types.Value)
                    {
                        foreach (Dictionary<string, dynamic> argument in subscription["arguments"])
                        {
                            argument["value"] = InterpretValue(argument);
                        }
                    }
                }
                else if (types.Key == "model")
                {
                    foreach (Dictionary<string, dynamic> column in types.Value["columns"])
                    {
                        foreach (Dictionary<string, dynamic> argument in column["arguments"])
                        {
                            argument["value"] = ValueIsColumn(argument, types);
                        }
                    }
                }
            }
            BsonDocument bsonValues = BsonTypeMapper.MapToBsonValue(typeUpdate) as BsonDocument;
            bsonValues.Add("_id", Id);
            await _database.GetCollection<BsonDocument>("types").InsertOneAsync(bsonValues);
            Types result = await GetTypesType(Id);
            _typesRepo.AddTypes(result);
            return result;
        }
        public dynamic ValueIsColumn(Dictionary<string, dynamic> argument, KeyValuePair<string, dynamic> types)
        {
            List<dynamic> columns = types.Value["columns"];
            bool isColumn = columns.Where(c => c["name"] == argument["value"] || ((string)argument["value"]).Contains(c["name"])).ToList().Count > 0;
            return isColumn ? argument["value"] : InterpretValue(argument);
        }
        public async Task<Types> UpdateType(ObjectId id, Dictionary<string, dynamic> typeUpdate)
        {
            FilterDefinition<BsonDocument> filter = Builders<BsonDocument>.Filter.Eq("_id", id);
            List<UpdateDefinition<BsonDocument>> updates = new List<UpdateDefinition<BsonDocument>>();
            foreach (KeyValuePair<string, dynamic> types in typeUpdate)
            {
                if (types.Key == "queries")
                {
                    foreach (Dictionary<string, dynamic> query in types.Value)
                    {
                        foreach (Dictionary<string, dynamic> argument in query["arguments"])
                        {
                            argument["value"] = InterpretValue(argument);
                        }
                    }
                    updates.Add(Builders<BsonDocument>.Update.Set(types.Key, BsonArray.Create(types.Value)));
                }
                else if (types.Key == "mutations")
                {
                    foreach (Dictionary<string, dynamic> mutation in types.Value)
                    {
                        foreach (Dictionary<string, dynamic> argument in mutation["arguments"])
                        {
                            argument["value"] = InterpretValue(argument);
                        }
                    }
                    updates.Add(Builders<BsonDocument>.Update.Set(types.Key, BsonTypeMapper.MapToBsonValue(types.Value)));
                }
                else if (types.Key == "subscriptions")
                {
                    foreach (Dictionary<string, dynamic> subscription in types.Value)
                    {
                        foreach (Dictionary<string, dynamic> argument in subscription["arguments"])
                        {
                            argument["value"] = InterpretValue(argument);
                        }
                    }
                    updates.Add(Builders<BsonDocument>.Update.Set(types.Key, BsonTypeMapper.MapToBsonValue(types.Value)));
                }
                else if (types.Key == "model")
                {
                    foreach (Dictionary<string, dynamic> column in types.Value["columns"])
                    {
                        foreach (Dictionary<string, dynamic> argument in column["arguments"])
                        {
                            argument["value"] = ValueIsColumn(argument, types);
                        }
                    }
                    updates.Add(Builders<BsonDocument>.Update.Set(types.Key, BsonTypeMapper.MapToBsonValue(types.Value) as BsonDocument));
                }
                else if (types.Key == "inputTypes")
                {
                    updates.Add(Builders<BsonDocument>.Update.Set(types.Key, BsonArray.Create(types.Value)));
                }
                else
                {
                    updates.Add(Builders<BsonDocument>.Update.Set(types.Key, types.Value));
                }
            }
            UpdateDefinition<BsonDocument> update = Builders<BsonDocument>.Update.Combine(updates);
            await _database.GetCollection<BsonDocument>("types").UpdateOneAsync(filter, update);

            return await GetTypesType(id);
        }
        public async Task<Types> RemoveType(ObjectId id)
        {
            Types type = await GetTypesType(id);

            try
            {
                ISchema s = await BuildSchema(remove: type.Name);
                var schemaTypes = s.AllTypes;
            }
            catch
            {
                return new Types();
            }
            await _mongoContext.Types.DeleteOneAsync(Builders<Types>.Filter.Eq(c => c.Id, id));
            return type;
        }
        public async Task<Server> UpdateServer(Dictionary<string, dynamic> serverUpdate)
        {
            List<UpdateDefinition<Server>> updateSet = new List<UpdateDefinition<Server>>();
            foreach (KeyValuePair<string, dynamic> kv in serverUpdate)
            {
                updateSet.Add(Builders<Server>.Update.Set(kv.Key, kv.Value));
            }
            await _mongoContext.Server.UpdateOneAsync(_ => true, new UpdateDefinitionBuilder<Server>().Combine(updateSet), new UpdateOptions { IsUpsert = true });
            return await _mongoContext.Server.Find(_ => true).FirstOrDefaultAsync();
        }
        public async Task<Server> GetServer()
        {
            Server server = await _mongoContext.Server.Find(Builders<Server>.Filter.Empty).FirstOrDefaultAsync();
            //if (server == null || server.ExpiresOn <= DateTime.Now)
            //{
            //    string url = "https://localhost:55539/security/api-keys";
            //    HttpClient client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true });
            //    HttpResponseMessage xsrf = await client.GetAsync(url);
            //    client.DefaultRequestHeaders.Add("XSRF-TOKEN", xsrf.Headers.Where(h => h.Key == "XSRF-TOKEN").First().Value);
            //    HttpResponseMessage accessToken = await client.PostAsync(url, new StringContent(
            //        $"{{ \"purpose\": \"Portal Application\", \"expires_on\": \"{DateTime.Today.AddMonths(1)}\"}}",
            //        Encoding.UTF8,
            //        "application/json"
            //    ));
            //    var json = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(await accessToken.Content.ReadAsStringAsync());
            //    return await UpdateServer(new Dictionary<string, dynamic> {
            //        { "accessToken", json["access_token"] },
            //        { "expiresOn", json["expires_on"] }
            //    });
            //}
            return server;
        }
        public async Task<List<Column>> ValidateTypeSchema(string name, string schema, List<string> queries, List<string> mutations)
        {
            try
            {
                ISchema s = await BuildSchema(name, schema, queries, mutations);
                var schemaTypes = s.AllTypes;
                ObjectGraphType type = s.FindType(name) as ObjectGraphType;
                InputObjectGraphType inputType = s.FindType($"{name}Input") as InputObjectGraphType;
                List<Column> columns = new List<Column>();
                foreach (FieldType f in type.Fields)
                {
                    var resolvedType = ResolveType(f);
                    columns.Add(new Column
                    {
                        Name = f.Name,
                        ColumnGraphType = resolvedType.ResolvedName,
                        Arguments = new List<Argument> { new Argument() },
                        AllowedGroups = new List<string>()
                    });
                }
                if (inputType != null)
                {
                    foreach (FieldType f in inputType.Fields)
                    {
                        var resolvedType = ResolveType(f);
                        if (columns.FindIndex(c => c.Name == f.Name) == -1)
                        {
                            columns.Add(new Column
                            {
                                Name = f.Name,
                                ColumnGraphType = resolvedType.ResolvedName,
                                Arguments = new List<Argument> { new Argument() },
                                AllowedGroups = new List<string>()
                            });
                        }

                    }
                }

                return columns;
            }
            catch(Exception err)
            {
                return new List<Column> { new Column { Name = err.Message } };
            }
        }
        public ResolvedType ResolveType(QueryArgument a)
        {
            return ResolveType(a.ResolvedType);;
        }
        public ResolvedType ResolveType(string type)
        {
            return ResolveType(BuildSchema().Result.FindType(type));
        }
        public ResolvedType ResolveType(IFieldType a)
        {
            return ResolveType(a.ResolvedType);
        }
        public ResolvedType ResolveType(IGraphType a)
        {
            ResolvedType rt = new ResolvedType { Name = "", ResolvedName = "", TypeStack = new List<string>() };
            if (a.Name == null)
            {
                IGraphType resolvedGraphType = a;
                do
                {
                    if (resolvedGraphType.GetType() == typeof(ListGraphType))
                    {
                        rt.TypeStack.Add("array");
                        resolvedGraphType = (resolvedGraphType as ListGraphType).ResolvedType;
                    }
                    else if (resolvedGraphType.GetType() == typeof(NonNullGraphType))
                    {
                        rt.TypeStack.Add("required");
                        resolvedGraphType = (resolvedGraphType as NonNullGraphType).ResolvedType;
                    }
                    else if (resolvedGraphType.GetType() == typeof(ObjectGraphType))
                    {
                        rt.TypeStack.Add("object");
                    }
                    else
                    {
                        rt.TypeStack.Add("scalar");
                    }
                } while (resolvedGraphType.Name == null);
                rt.Name = resolvedGraphType.Name;
                rt.ResolvedName = resolvedGraphType.Name;
                rt.TypeStack.Reverse();
                foreach (string typeName in rt.TypeStack)
                {
                    if (typeName == "array")
                    {
                        rt.ResolvedName = $"[{rt.ResolvedName}]";
                    }
                    else if (typeName == "required")
                    {
                        rt.ResolvedName = $"{rt.ResolvedName}!";
                    }
                }
            }
            else
            {
                rt.Name = a.Name;
                rt.ResolvedName = a.Name;
            }
            return rt;
        }
        public async Task<ISchema> BuildSchema(string name = null, string schema = null, List<string> queries = null, List<string> mutations = null, string remove = null)
        {
            ISchema s = new SchemaBuilder().Build(await GetSchemaString(name, schema, queries, mutations, remove));
            return s;
        }
        public async Task<List<Column>> GetFilterColumns(string name, string schema)
        {
            if (name == "" || name == null)
            {
                return new List<Column>();
            }
            try
            {
                ISchema s = await BuildSchema(name, schema);
                var schemaTypes = s.AllTypes;
                IGraphType graphType = s.FindType(name);
                Types type = new Types { Model = new Model { Columns = new List<Column>() } };
                if (schema == null)
                {
                    type = await GetTypesType(name.Trim('[', ']', '!'));
                } else if (graphType is ObjectGraphType)
                {
                    foreach (var f in ((ObjectGraphType)graphType).Fields)
                    {
                        ResolvedType resolvedType = ResolveType(f);
                        type.Model.Columns.Add(new Column
                        {
                            Name = f.Name,
                            ColumnGraphType = resolvedType.ResolvedName
                        });
                    }
                }
                List<Column> columns = type.Model.Columns;
                var columnsToAdd = new List<Column>();
                columnsToAdd.Add(new Column { Name = "operationName", ColumnGraphType = "String", ColumnType = "String", DataName = "operationName", AllowedGroups = new List<string>() });
                columnsToAdd.Add(new Column { Name = "operationName_not", ColumnGraphType = "String", ColumnType = "String", DataName = "operationName", AllowedGroups = new List<string>() });
                columnsToAdd.Add(new Column { Name = "operationName_in", ColumnGraphType = "[String]", ColumnType = "List", DataName = "operationName", AllowedGroups = new List<string>() });
                columnsToAdd.Add(new Column { Name = "operationName_notIn", ColumnGraphType = "[String]", ColumnType = "List", DataName = "operationName", AllowedGroups = new List<string>() });
                if (graphType is ObjectGraphType) foreach (var f in ((ObjectGraphType)graphType).Fields)
                {
                    ResolvedType resolvedType = ResolveType(f);                    
                    if (resolvedType.Name == "ID")
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_in", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notIn", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    else if (resolvedType.Name == "String")
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_startsWith", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_endsWith", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notStartsWith", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notEndsWith", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_contains", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notContains", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_in", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notIn", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_not", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    else if (resolvedType.Name == "Int32")
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_in", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notIn", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_not", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    else if (resolvedType.Name == "Float")
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_in", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notIn", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_not", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    else if (resolvedType.Name == "Boolean")
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_not", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    else if (resolvedType.Name == "DateTime")
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_in", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notIn", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_not", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    else if (resolvedType.Name == "DateTimeOffset")
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_lt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gte", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_gt", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_in", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_notIn", ColumnGraphType = $"[{resolvedType.ResolvedName}]", ColumnType = "List", DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_not", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    else if (resolvedType.TypeStack.Contains("array"))
                    {
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_anyEq", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                        columnsToAdd.Add(new Column { Name = $"{f.Name}_anyNe", ColumnGraphType = resolvedType.ResolvedName, ColumnType = type.Model.Fields[f.Name].ColumnType, DataName = type.Model.Fields[f.Name].DataName, AllowedGroups = new List<string>() });
                    }
                    if (s.FindType(resolvedType.Name) is ObjectGraphType subType && type.Type == "mongo" && resolvedType.TypeStack.Contains("array"))
                    {
                        Types st = await GetTypesType(resolvedType.Name);
                        foreach (FieldType sf in subType.Fields)
                        {
                            var rt = ResolveType(sf);
                            if (rt.Name == "ID")
                            {
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_in", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notIn", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            }
                            else if (rt.Name == "String")
                            {
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_startsWith", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_endsWith", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notStartsWith", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notEndsWith", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_contains", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notContains", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_in", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notIn", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_not", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            }
                            else if (rt.Name == "Int32")
                            {
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_in", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notIn", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_not", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            }
                            else if (rt.Name == "Float")
                            {
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_in", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notIn", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_not", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            }
                            else if (rt.Name == "Boolean")
                            {
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_not", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            }
                            else if (rt.Name == "DateTime")
                            {
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_in", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notIn", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_not", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            }
                            else if (rt.Name == "DateTimeOffset")
                            {
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gte", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_gt", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_in", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_notIn", ColumnGraphType = $"[{rt.ResolvedName}]", ColumnType = "List", DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                                columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_not", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            }
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_anyEq", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_anyNe", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_last", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_lastNot", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_first", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_firstNot", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_atIndex", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                            columnsToAdd.Add(new Column { Name = $"{f.Name}_{sf.Name}_atIndexNot", ColumnGraphType = rt.ResolvedName, ColumnType = st.Model.Fields[sf.Name].ColumnType, DataName = st.Model.Fields[sf.Name].DataName, AllowedGroups = new List<string>() });
                        }
                    }
                }
                foreach (Column c in columnsToAdd)
                {
                    columns.Add(c);
                }
                return columns.OrderBy(c => c.DataName).ToList();
            }
            catch
            {
                return new List<Column>();
            }
        }
        public async Task<Bucket> SaveBucket(string name, string type, string userName, Dictionary<string, dynamic> bucketUpdate)
        {
            FilterDefinition<Bucket> filter = Builders<Bucket>.Filter.And(
                Builders<Bucket>.Filter.Eq(b => b.Name, name),
                Builders<Bucket>.Filter.Eq(b => b.BucketType, type),
                Builders<Bucket>.Filter.Eq(b => b.Owner, userName)
            );
            List<UpdateDefinition<Bucket>> updateSet = new List<UpdateDefinition<Bucket>>();
            foreach (KeyValuePair<string, dynamic> kv in bucketUpdate)
            {
                updateSet.Add(Builders<Bucket>.Update.Set(kv.Key, kv.Value));
            }
            var result = await _mongoContext.Buckets.UpdateOneAsync(filter, new UpdateDefinitionBuilder<Bucket>().Combine(updateSet), new UpdateOptions { IsUpsert = true });
            return await GetBucket(name, type, userName);
        }
        public async Task<Bucket> GetBucket(ObjectId id)
        {
            FilterDefinition<Bucket> filter = Builders<Bucket>.Filter.Eq(b => b.Id, id);
            var bucket = await _mongoContext.Buckets.Find(filter).FirstOrDefaultAsync();
            return bucket;
        }
        public async Task<Bucket> GetBucket(string name, string type, string userName)
        {
            FilterDefinition<Bucket> filter = Builders<Bucket>.Filter.And(
                Builders<Bucket>.Filter.Eq(b => b.Name, name),
                Builders<Bucket>.Filter.Eq(b => b.BucketType, type),
                Builders<Bucket>.Filter.Eq(b => b.Owner, userName)
            );
            return await _mongoContext.Buckets.Find(filter).FirstOrDefaultAsync();
        }
        public async Task<Bucket> EmptyBucket(ObjectId id)
        {
            FilterDefinition<Bucket> filter = Builders<Bucket>.Filter.Eq(b => b.Id, id);
            var bucket = await _mongoContext.Buckets.Find(filter).FirstOrDefaultAsync();
            await _mongoContext.Buckets.DeleteOneAsync(filter);
            return bucket;
        }
        public async Task<List<Bucket>> ListBucket(string name, string type, string userName)
        {
            FilterDefinition<Bucket> filter = Builders<Bucket>.Filter.And(
                name != null ? Builders<Bucket>.Filter.Eq(b => b.Name, name) : Builders<Bucket>.Filter.Ne(b => b.Name, name),
                type != null ? Builders<Bucket>.Filter.Eq(b => b.BucketType, type) : Builders<Bucket>.Filter.Ne(b => b.BucketType, type),
                Builders<Bucket>.Filter.Eq(b => b.Owner, userName)
            );
            return await _mongoContext.Buckets.Find(filter).ToListAsync();
        }
        public async Task<string> GetTypeAsJson(string name)
        {
            var result = await _database.GetCollection<Dictionary<string, dynamic>>("types").Find(Builders<Dictionary<string, dynamic>>.Filter.Eq("name", name)).FirstOrDefaultAsync();
            var json = JsonConvert.SerializeObject(result);
            return json;
        }
        public DirectorySearcher createDirectorySearcher(Connection connection)
        {
            if (connection == null)
            {
                connection = new Connection();
            }
            DirectorySearcher searcher = new DirectorySearcher(connection.ConnectionString);
            searcher.PropertiesToLoad.AddRange(new string[] {
                "SAMAccountName",
                "DisplayName",
                "DistinguishedName",
                "Name",
                "Member",
            });
            return searcher;
        }
        public List<string> GetRoles()
        {
            return _settings.Value.RoleNames;
        }
        public List<string> GetMyRoles()
        {
            return _settings.Value.RoleNames.FindAll(r => _httpContext.HttpContext.User.IsInRole(r));
        }
        public async Task<List<ADGroup>> GetGroups()
        {
            Server server = await GetServer();
            Connection connection = GetConnections("ad").Result.FirstOrDefault();
            DirectorySearcher searcher = createDirectorySearcher(connection);
            if (connection == null) return null;
            List<ADGroup> aDGroups = new List<ADGroup>();
            if (connection.Name.ToLower() == "computer")
            {
                DirectoryEntry computer = new DirectoryEntry(string.Format("WinNT://{0},computer", connection.ConnectionString));
                foreach (DirectoryEntry c in computer.Children)
                {
                    if (c.SchemaClassName == "Group")
                    {
                        List<string> children = new List<string>();
                        foreach(DirectoryEntry child in c.Children)
                        {
                            children.Add(child.Name);
                        }
                        aDGroups.Add(new ADGroup(c) { SAMAccountName = c.Name, Members = children });
                    }
                }
            } else {
                searcher.Filter = server.GroupFilter == null || server.GroupFilter == "" ? "(ObjectClass=group)" : server.GroupFilter;
                foreach (SearchResult g in searcher.FindAll())
                {
                    aDGroups.Add(new ADGroup(g));
                }
            }
            return aDGroups.OrderBy(g => g.Name.ToString()).ToList();
        }
        public async Task<string> GetSchemaString(string name = null, string schema = null, List<string> queries = null, List<string> mutations = null, string remove = null)
        {
            //Log.Information("ConfigData.GetSchemaString({name}, {schema}, {queries}, {mutations}, {remove})", name, schema, queries, mutations, remove);
            List<Types> types = await GetTypes();
            Dictionary<string, Types> typeDict = new Dictionary<string, Types>();

            List<string> typeSchema = new List<string>();
            List<string> queriesSchema = new List<string>();
            List<string> mutationsSchema = new List<string>();
            foreach (Types t in types)
            {
                if (name != null && name == t.Name && schema != null)
                {
                    typeSchema.Add(schema);
                    if (queries != null)
                    {
                        queriesSchema.Add(string.Join("\n ", queries));
                    }
                    if (mutations != null)
                    {
                        mutationsSchema.Add(string.Join("\n ", mutations));
                    }
                }
                else
                {
                    if (remove == null || remove != t.Name)
                    {
                        typeSchema.Add(t.Schema);
                        queriesSchema.Add(string.Join("\n ", t.QueriesSchema));
                        mutationsSchema.Add(string.Join("\n ", t.MutationsSchema));
                    }
                }
                typeDict.Add(t.Name, t);
            }
            if (name != null && !typeDict.ContainsKey(name))
            {
                typeSchema.Add(schema);
                if (queries != null)
                {
                    queriesSchema.Add($"\n {string.Join("\n ", queries)}");
                }
                if (mutations != null)
                {
                    mutationsSchema.Add($"\n {string.Join("\n ", mutations)}");
                }
            }
            string typesString = string.Join("\n ", typeSchema);
            string queryString = "type Query {\n" + string.Join("\n ", queriesSchema) + "\n}";
            string mutationString = "type Mutation {\n" + string.Join("\n ", mutationsSchema) + "\n}";
            return $"{typesString}\n{queryString}\n{mutationString}";
        }
    }
}
