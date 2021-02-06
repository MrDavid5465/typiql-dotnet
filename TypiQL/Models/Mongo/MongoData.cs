using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.GridFS;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DataCrush.TypiQL.Models.Mongo
{
    public partial class MongoData
    {
        private readonly IHttpContextAccessor _context;
        private readonly Dictionary<string, IMongoDatabase> _connections;
        private readonly Dictionary<string, Types> _types;
        private ConfigData _data;
        private readonly Dictionary<string, GridFSBucket> _buckets;
        public MongoData(TypiQLSettings settings, IHttpContextAccessor context, ConfigData data)
        {
            _context = context;
            _data = data;
            List<Connection> connections = data.GetConnections("mongo").Result;
            Connection adConnection = data.GetConnections("ad").Result.FirstOrDefault();
            _connections = new Dictionary<string, IMongoDatabase>();
            _buckets = new Dictionary<string, GridFSBucket>();
            foreach (Connection c in connections)
            {
                var client = new MongoClient(c.ConnectionString);
                IMongoDatabase database = client.GetDatabase(c.DatabaseName);
                _buckets.Add(c.Id.ToString(), new GridFSBucket(database));
                _connections.Add(
                    c.Id.ToString(),
                    new MongoClient(//c.ConnectionString
                        new MongoClientSettings
                        {
                            Server = new MongoServerAddress("localhost"),
                            ClusterConfigurator = cb =>
                            {
                                cb.Subscribe<CommandStartedEvent>(
                                    e =>
                                    {
                                        Console.WriteLine($"{e.CommandName} = {e.Command}");
                                    }
                                );
                            }
                        }
                    ).GetDatabase(c.DatabaseName));
            }
            if (adConnection != null)
                _connections.Add(adConnection.Id.ToString(), new MongoClient(settings.TypiQLConnectionString).GetDatabase(settings.TypiQLDatabase));
            List<Types> types = data.GetTypes("mongo").Result;
            _types = new Dictionary<string, Types>();
            foreach (Types t in types)
            {
                _types.Add(t.Name, t);
            }
        }
        public FilterDefinition<BsonDocument> BuildFilter(Types t, Dictionary<string, dynamic> keys, ref int skip, ref int limit, List<SortDefinition<BsonDocument>> sort, ref bool upsert)
        {
            upsert = false;
            List<FilterDefinition<BsonDocument>> filters = new List<FilterDefinition<BsonDocument>>();
            if (keys.Count == 0)
            {
                filters.Add(Builders<BsonDocument>.Filter.Empty);
            }
            else
            {
                foreach (KeyValuePair<string, dynamic> key in keys)
                {
                    var value = key.Value;
                    if (key.Value == null || key.Value is string && key.Value == "")
                    {

                    }
                    else if (t.Model.Fields.ContainsKey(key.Key.Split("_")[0]) && t.Model.Fields[key.Key.Split("_")[0]].DataName == "_id")
                    {
                        if (key.Value is string && ((string)key.Value).Length == 24)
                        {
                            value = new ObjectId(key.Value);
                        }
                        else if (key.Value is ObjectId)
                        {
                            value = key.Value;
                        }
                        else
                        {
                            value = new List<ObjectId>();
                            foreach (string v in key.Value)
                            {
                                if (v.Length == 24)
                                {
                                    value.Add(new ObjectId(v));
                                }
                            }
                        }
                    }
                    else if (key.Value is ObjectId)
                    {
                        value = ((ObjectId)key.Value).ToString();
                    }
                    if (value == null)
                    {

                    }
                    else if (key.Key == "_orderBy" && value != null)
                    {
                        foreach (string field in ((string)value).Split(","))
                        {
                            sort.Add(Builders<BsonDocument>.Sort.Ascending(t.Model.Fields[field.Trim()].DataName));
                        }
                    }
                    else if (key.Key == "_orderBy_desc" && value != null)
                    {
                        foreach (string field in ((string)value).Split(","))
                        {
                            sort.Add(Builders<BsonDocument>.Sort.Descending(t.Model.Fields[field.Trim()].DataName));
                        }
                    }
                    else if (key.Key == "_upsert" && value != null)
                    {
                        upsert = (bool)value;
                    }
                    else if (key.Key == "_start" && value != null)
                    {
                        skip = (int)value;
                    }
                    else if (key.Key == "_limit" && value != null)
                    {
                        limit = (int)value;
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "startsWith")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Regex(t.Model.Fields[key.Key.Split("_")[0]].DataName, new BsonRegularExpression($"/^{value}/i")));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "endsWith")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Regex(t.Model.Fields[key.Key.Split("_")[0]].DataName, new BsonRegularExpression($"/{value}$/i")));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notStartsWith")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Regex(t.Model.Fields[key.Key.Split("_")[0]].DataName, new BsonRegularExpression($"/^(?!{value}).*/i")));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notEndsWith")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Regex(t.Model.Fields[key.Key.Split("_")[0]].DataName, new BsonRegularExpression($"/.*(?<!{value})$/i")));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "contains")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Regex(t.Model.Fields[key.Key.Split("_")[0]].DataName, new BsonRegularExpression($"/{value}/i")));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notContains")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Regex(t.Model.Fields[key.Key.Split("_")[0]].DataName, new BsonRegularExpression($"/(?!{value})/i")));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lte")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Lte(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lt")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Lt(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gte")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Gte(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gt")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Gt(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "in")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.In(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notIn")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Nin(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "anyEq")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.AnyEq(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "anyNe")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.AnyNe(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "not")
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Ne(t.Model.Fields[key.Key.Split("_")[0]].DataName, value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_").Length == 3)
                    {
                        Types st = _types[_data.ResolveType(t.Model.Fields[key.Key.Split("_")[0]].ColumnGraphType).Name];
                        if (key.Value == null || key.Value is string && key.Value == "")
                        {

                        }
                        else if (t.Model.Fields.ContainsKey(key.Key.Split("_")[1]) && t.Model.Fields[key.Key.Split("_")[1]].DataName == "_id")
                        {
                            if (key.Value is string && ((string)key.Value).Length == 24)
                            {
                                value = new ObjectId(key.Value);
                            }
                            else if (key.Value is ObjectId)
                            {
                                value = key.Value;
                            }
                            else
                            {
                                value = new List<ObjectId>();
                                foreach (string v in key.Value)
                                {
                                    if (v.Length == 24)
                                    {
                                        value.Add(new ObjectId(v));
                                    }
                                }
                            }
                        }
                        else if (key.Value is ObjectId)
                        {
                            value = ((ObjectId)key.Value).ToString();
                        }
                        if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "startsWith")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Regex(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        new BsonRegularExpression($"/^{value}/i")
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "endsWith")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Regex(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        new BsonRegularExpression($"/{value}$/i")
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notStartsWith")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Regex(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        new BsonRegularExpression($"/^(?!{value}).*/i")
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notEndsWith")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Regex(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        new BsonRegularExpression($"/.*(?<!{value})$/i")
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "contains")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Regex(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        new BsonRegularExpression($"/{value}/i")
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notContains")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Regex(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        new BsonRegularExpression($"/(?!{value})/i")
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "lte")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Lte(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "lt")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Lt(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "gte")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Gte(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "gt")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Gt(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "in")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.In(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notIn")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Nin(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "not")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Ne(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "anyEq")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.AnyEq(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "anyNe")
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.AnyNe(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "last")
                        {
                            BsonDocument filter = new BsonDocument();
                            filter.Add("$expr", new BsonDocument()
                                .Add("$eq", new BsonArray()
                                    .Add(new BsonDocument()
                                        .Add("$arrayElemAt", new BsonArray()
                                            .Add($"${t.Model.Fields[key.Key.Split("_")[0]].DataName}.{st.Model.Fields[key.Key.Split("_")[1]].DataName}")
                                            .Add(-1.0)
                                        )
                                    )
                                    .Add(value)
                                )
                            );

                            filters.Add(
                                filter
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "lastNot")
                        {                            
                            BsonDocument filter = new BsonDocument();
                            filter.Add("$expr", new BsonDocument()
                                .Add("$ne", new BsonArray()
                                    .Add(new BsonDocument()
                                        .Add("$arrayElemAt", new BsonArray()
                                            .Add($"${t.Model.Fields[key.Key.Split("_")[0]].DataName}.{st.Model.Fields[key.Key.Split("_")[1]].DataName}")
                                            .Add(-1.0)
                                        )
                                    )
                                    .Add(value)
                                )
                            );

                            filters.Add(
                                filter
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "first")
                        {
                            BsonDocument filter = new BsonDocument();
                            filter.Add("$expr", new BsonDocument()
                                .Add("$eq", new BsonArray()
                                    .Add(new BsonDocument()
                                        .Add("$arrayElemAt", new BsonArray()
                                            .Add($"${t.Model.Fields[key.Key.Split("_")[0]].DataName}.{st.Model.Fields[key.Key.Split("_")[1]].DataName}")
                                            .Add(0.0)
                                        )
                                    )
                                    .Add(value)
                                )
                            );

                            filters.Add(
                                filter
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "firstNot")
                        {
                            BsonDocument filter = new BsonDocument();
                            filter.Add("$expr", new BsonDocument()
                                .Add("$ne", new BsonArray()
                                    .Add(new BsonDocument()
                                        .Add("$arrayElemAt", new BsonArray()
                                            .Add($"${t.Model.Fields[key.Key.Split("_")[0]].DataName}.{st.Model.Fields[key.Key.Split("_")[1]].DataName}")
                                            .Add(0.0)
                                        )
                                    )
                                    .Add(value)
                                )
                            );

                            filters.Add(
                                filter
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "atIndex")
                        {
                            BsonDocument filter = new BsonDocument();
                            filter.Add("$expr", new BsonDocument()
                                .Add("$eq", new BsonArray()
                                    .Add(new BsonDocument()
                                        .Add("$arrayElemAt", new BsonArray()
                                            .Add($"${t.Model.Fields[key.Key.Split("_")[0]].DataName}.{st.Model.Fields[key.Key.Split("_")[1]].DataName}")
                                            .Add(value["index"])
                                        )
                                    )
                                    .Add(value["value"])
                                )
                            );

                            filters.Add(
                                filter
                            );
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "atIndexNot")
                        {
                            BsonDocument filter = new BsonDocument();
                            filter.Add("$expr", new BsonDocument()
                                .Add("$ne", new BsonArray()
                                    .Add(new BsonDocument()
                                        .Add("$arrayElemAt", new BsonArray()
                                            .Add($"${t.Model.Fields[key.Key.Split("_")[0]].DataName}.{st.Model.Fields[key.Key.Split("_")[1]].DataName}")
                                            .Add(value["index"])
                                        )
                                    )
                                    .Add(value["value"])
                                )
                            );

                            filters.Add(
                                filter
                            );
                        }
                        else
                        {
                            filters.Add(
                                Builders<BsonDocument>.Filter.ElemMatch(
                                    t.Model.Fields[key.Key.Split("_")[0]].DataName,
                                    Builders<BsonDocument>.Filter.Eq(
                                        st.Model.Fields[key.Key.Split("_")[1]].DataName,
                                        value
                                    )
                                )
                            );
                        }
                    }
                    else if (value != null)
                    {
                        filters.Add(Builders<BsonDocument>.Filter.Eq(t.Model.Fields[key.Key].DataName, value));
                    }
                }
            }
            if (filters.Count == 0)
            {
                filters.Add(Builders<BsonDocument>.Filter.Empty);
            }
            return Builders<BsonDocument>.Filter.And(filters);
        }
        public dynamic Result(BsonDocument document)
        {
            if (document == null)
            {
                return null;
            }
            //Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            //foreach (var bsonElement in document)
            //{
            //    result.Add(bsonElement.Name, BsonTypeMapper.MapToDotNetValue(bsonElement.Value));
            //}
            //return result;
            return BsonTypeMapper.MapToDotNetValue(document);
        }
        public List<dynamic> Results(List<BsonDocument> documents)
        {
            if (documents == null)
            {
                return null;
            }
            List<dynamic> results = new List<dynamic>();
            foreach (BsonDocument document in documents)
            {
                //Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
                //foreach (var bsonElement in document)
                //{
                //    result.Add(bsonElement.Name, BsonTypeMapper.MapToDotNetValue(bsonElement.Value));
                //}
                //results.Add(result);
                results.Add(Result(document));
            }

            return results;
        }

        public async Task<Dictionary<string, dynamic>> GetDocument(string collection, Dictionary<string, dynamic> keys)
        {
            Types t = _types[collection];

            List<SortDefinition<BsonDocument>> sort = new List<SortDefinition<BsonDocument>>();
            int skip = 0;
            int limit = 0;
            bool upsert = false;
            FilterDefinition<BsonDocument> filter = BuildFilter(t, keys, ref skip, ref limit, sort, ref upsert);
            return Result(await _connections[t.Connection]
                .GetCollection<BsonDocument>(t.Model.Name)
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Combine(sort))
                .Skip(skip)
                .Limit(limit)
                .FirstOrDefaultAsync());
        }
        public async Task<List<dynamic>> GetDocuments(string collection, Dictionary<string, dynamic> keys)
        {
            Types t = _types[collection];

            List<SortDefinition<BsonDocument>> sort = new List<SortDefinition<BsonDocument>>();
            int skip = 0;
            int limit = 0;
            bool upsert = false;
            FilterDefinition<BsonDocument> filter = BuildFilter(t, keys, ref skip, ref limit, sort, ref upsert);
            var results = Results(await _connections[t.Connection]
                .GetCollection<BsonDocument>(t.Model.Name)
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Combine(sort))
                .Skip(skip)
                .Limit(limit)
                .ToListAsync());
            if (results == null)
            {
                return new List<dynamic>();
            }
            else
            {
                return results;
            }
        }
        public async Task<Dictionary<string, dynamic>> UpdateDocument(string collection, Dictionary<string, dynamic> keys, Dictionary<string, dynamic> update)
        {
            Types t = _types[collection];

            List<SortDefinition<BsonDocument>> sort = new List<SortDefinition<BsonDocument>>();
            int skip = 0;
            int limit = 0;
            bool upsert = false;
            FilterDefinition<BsonDocument> filter = BuildFilter(t, keys, ref skip, ref limit, sort, ref upsert);
            List<UpdateDefinition<BsonDocument>> updateSet = new List<UpdateDefinition<BsonDocument>>();
            var valuesCorrected = new Dictionary<string, dynamic>();
            var obj = await GetDocument(collection, keys);
            foreach (KeyValuePair<string, dynamic> kv in update)
            {
                if (kv.Key.Split("_").Length > 1)
                {
                    switch (kv.Key.Split("_")[1])
                    {
                        case "Add":
                            {
                                var value = new List<dynamic>();
                                if (!obj.ContainsKey(t.Model.Fields[kv.Key.Split("_")[0]].DataName))
                                {
                                    obj.Add(t.Model.Fields[kv.Key.Split("_")[0]].DataName, new List<dynamic>());
                                }
                                value = obj[t.Model.Fields[kv.Key.Split("_")[0]].DataName];
                                value.Add(kv.Value);
                                valuesCorrected.Add(t.Model.Fields[kv.Key.Split("_")[0]].DataName, value);
                                break;
                            }
                        case "Remove":
                            {
                                var value = new List<dynamic>();
                                value = obj[t.Model.Fields[kv.Key.Split("_")[0]].DataName];
                                value.Remove(kv.Value);
                                valuesCorrected.Add(t.Model.Fields[kv.Key.Split("_")[0]].DataName, value);
                                break;
                            }
                        default:
                            {
                                valuesCorrected.Add(t.Model.Fields[kv.Key.Split("_")[0]].DataName, kv.Value);
                                break;
                            }
                    }
                }
                else
                {
                    valuesCorrected.Add(t.Model.Fields[kv.Key].DataName, kv.Value);
                }                
            }
            if (valuesCorrected.ContainsKey("type") && valuesCorrected["type"] == "image" && valuesCorrected.ContainsKey("file"))
            {
                valuesCorrected.Add("thumbnail", CreateThumbnail(valuesCorrected["file"]));
            }
            if (t.Model.ModelType == "gridfs")
            {
                if (await GetDocument(collection, keys) is Dictionary<string, dynamic> original && original.ContainsKey("fileId"))
                {
                    await DeleteFile(t, original["fileId"]);
                }
                foreach (var kv in await UploadFile(t, valuesCorrected))
                {
                    if (kv.Key == "_id")
                    {
                        valuesCorrected.Add("fileId", ((ObjectId)kv.Value).ToString());
                    }
                    else
                    {
                        valuesCorrected.Add(kv.Key, kv.Value);
                    }
                }
                valuesCorrected.Remove("file");
            }
            foreach (KeyValuePair<string, dynamic> o in valuesCorrected)
            {
                updateSet.Add(Builders<BsonDocument>.Update.Set(o.Key, BsonTypeMapper.MapToBsonValue(o.Value)));
            }
            UpdateDefinition<BsonDocument> updates = Builders<BsonDocument>.Update.Combine(updateSet.ToArray());
            await _connections[t.Connection].GetCollection<BsonDocument>(t.Model.Name).UpdateOneAsync(filter, updates, new UpdateOptions { IsUpsert = upsert });
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Update", Result(await _connections[t.Connection]
                .GetCollection<BsonDocument>(t.Model.Name)
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Combine(sort))
                .Skip(skip)
                .Limit(limit)
                .FirstOrDefaultAsync()));
        }
        public async Task<List<dynamic>> UpdateDocuments(string collection, Dictionary<string,dynamic> keys, Dictionary<string,dynamic> update)
        {
            Types t = _types[collection];

            List<SortDefinition<BsonDocument>> sort = new List<SortDefinition<BsonDocument>>();
            int skip = 0;
            int limit = 0;
            bool upsert = false;
            FilterDefinition<BsonDocument> filter = BuildFilter(t, keys, ref skip, ref limit, sort, ref upsert);
            List<UpdateDefinition<BsonDocument>> updateSet = new List<UpdateDefinition<BsonDocument>>();
            var valuesCorrected = new Dictionary<string, dynamic>();
            foreach (KeyValuePair<string, dynamic> kv in update)
            {
                valuesCorrected.Add(t.Model.Fields[kv.Key].DataName, kv.Value);
            }
            if (valuesCorrected.ContainsKey("type") && valuesCorrected["type"] == "image" && valuesCorrected.ContainsKey("file"))
            {
                valuesCorrected.Add("thumbnail", CreateThumbnail(valuesCorrected["file"]));
            }
            if (t.Model.ModelType == "gridfs")
            {
                if (await GetDocument(collection, keys) is Dictionary<string, dynamic> original && original.ContainsKey("fileId"))
                {
                    await DeleteFile(t, original["fileId"]);
                }
                foreach (var kv in await UploadFile(t, valuesCorrected))
                {
                    if (kv.Key == "_id")
                    {
                        valuesCorrected.Add("fileId", ((ObjectId)kv.Value).ToString());
                    }
                    else
                    {
                        valuesCorrected.Add(kv.Key, kv.Value);
                    }
                }
                valuesCorrected.Remove("file");
            }
            foreach (KeyValuePair<string, dynamic> o in valuesCorrected)
            {
                updateSet.Add(Builders<BsonDocument>.Update.Set(o.Key, BsonTypeMapper.MapToBsonValue(o.Value)));
            }
            UpdateDefinition<BsonDocument> updates = Builders<BsonDocument>.Update.Combine(updateSet.ToArray());
            await _connections[t.Connection].GetCollection<BsonDocument>(t.Model.Name).UpdateManyAsync(filter, updates, new UpdateOptions { IsUpsert = upsert });
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "RemoveMany", await GetDocuments(collection, keys)); 
        }
        public async Task<Dictionary<string, dynamic>> AddDocument(string collection, Dictionary<string, dynamic> values)
        {
            Types t = _types[collection];
            ObjectId id = ObjectId.GenerateNewId();
            var valuesCorrected = new Dictionary<string, dynamic>();
            foreach(KeyValuePair<string, dynamic> kv in values)
            {
                if (!(t.Model.Fields[kv.Key].DataName == null || t.Model.Fields[kv.Key].DataName == ""))
                {
                    valuesCorrected.Add(t.Model.Fields[kv.Key].DataName, kv.Value);
                }                
            }
            if (valuesCorrected.ContainsKey("type") && valuesCorrected["type"] == "image" && valuesCorrected.ContainsKey("fileId"))
            {
                valuesCorrected.Add("thumbnail", CreateThumbnail(valuesCorrected["fileId"]));
            }
            if (t.Model.ModelType == "gridfs")
            {
                foreach (var kv in await UploadFile(t, valuesCorrected))
                {
                    if (kv.Key == "_id")
                    {
                        valuesCorrected["fileId"] = ((ObjectId)kv.Value).ToString();
                    }
                    else
                    {
                        valuesCorrected.Add(kv.Key, kv.Value);
                    }
                }
            }
            BsonDocument bsonValues = BsonTypeMapper.MapToBsonValue(valuesCorrected) as BsonDocument;// BsonDocument.Parse(JsonConvert.SerializeObject(valuesCorrected));
            bsonValues.Add("_id", id);            
            await _connections[t.Connection].GetCollection<BsonDocument>(t.Model.Name).InsertOneAsync(bsonValues);
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Add", Result(await _connections[t.Connection]
                .GetCollection<BsonDocument>(t.Model.Name)
                .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
                .FirstOrDefaultAsync()));
        }
        public async Task<List<dynamic>> AddDocuments(string collection, List<Dictionary<string, dynamic>> manyValues)
        {
            Types t = _types[collection];
            List<BsonDocument> manyBsonValues = new List<BsonDocument>();
            var ids = new List<ObjectId>();
            foreach (Dictionary<string, dynamic> values in manyValues)
            {
                ObjectId id = ObjectId.GenerateNewId();
                var valuesCorrected = new Dictionary<string, dynamic>();
                foreach (KeyValuePair<string, dynamic> kv in values)
                {
                    if (!(t.Model.Fields[kv.Key].DataName == null || t.Model.Fields[kv.Key].DataName == ""))
                    {
                        valuesCorrected.Add(t.Model.Fields[kv.Key].DataName, kv.Value);
                    }
                }
                if (valuesCorrected.ContainsKey("type") && valuesCorrected["type"] == "image" && valuesCorrected.ContainsKey("fileId"))
                {
                    valuesCorrected.Add("thumbnail", CreateThumbnail(valuesCorrected["fileId"]));
                }
                if (t.Model.ModelType == "gridfs")
                {
                    foreach (var kv in await UploadFile(t, valuesCorrected))
                    {
                        if (kv.Key == "_id")
                        {
                            valuesCorrected["fileId"] = ((ObjectId)kv.Value).ToString();
                        }
                        else
                        {
                            valuesCorrected.Add(kv.Key, kv.Value);
                        }
                    }
                }
                BsonDocument bsonValues = BsonTypeMapper.MapToBsonValue(valuesCorrected) as BsonDocument;// BsonDocument.Parse(JsonConvert.SerializeObject(valuesCorrected));
                bsonValues.Add("_id", id);
                ids.Add(id);
                manyBsonValues.Add(bsonValues);
            }
            await _connections[t.Connection].GetCollection<BsonDocument>(t.Model.Name).InsertManyAsync(manyBsonValues);
            return Results(await _connections[t.Connection]
                .GetCollection<BsonDocument>(t.Model.Name)
                .Find(Builders<BsonDocument>.Filter.In("_id", ids))
                .ToListAsync());
        }
        public async Task<Dictionary<string, dynamic>> RemoveDocument(string collection, Dictionary<string, dynamic> keys)
        {
            Types t = _types[collection];
            List<SortDefinition<BsonDocument>> sort = new List<SortDefinition<BsonDocument>>();
            int skip = 0;
            int limit = 0;
            bool upsert = false;
            FilterDefinition<BsonDocument> filter = BuildFilter(t, keys, ref skip, ref limit, sort, ref upsert);
            
            Dictionary<string, dynamic> document = Result(await _connections[t.Connection]
                .GetCollection<BsonDocument>(t.Model.Name)
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Combine(sort))
                .Skip(skip)
                .Limit(limit)
                .FirstOrDefaultAsync());
            if (t.Model.ModelType == "gridfs")
            {
                if (document.ContainsKey("fileId"))
                {
                    await DeleteFile(t, document["fileId"]);
                }
            }
            await _connections[t.Connection].GetCollection<BsonDocument>(t.Model.Name).DeleteOneAsync(filter);
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Remove", document);
        }
        public async Task<List<dynamic>> RemoveDocuments(string collection, Dictionary<string, dynamic> keys)
        {
            Types t = _types[collection];
            List<SortDefinition<BsonDocument>> sort = new List<SortDefinition<BsonDocument>>();
            int skip = 0;
            int limit = 0;
            bool upsert = false;
            FilterDefinition<BsonDocument> filter = BuildFilter(t, keys, ref skip, ref limit, sort, ref upsert);

            List<dynamic> documents = await GetDocuments(collection, keys);
            foreach (var document in documents)
            {
                if (t.Model.ModelType == "gridfs")
                {
                    if (document.ContainsKey("fileId"))
                    {
                        await DeleteFile(t, document["fileId"]);
                    }
                }
            }
            await _connections[t.Connection].GetCollection<BsonDocument>(t.Model.Name).DeleteManyAsync(filter);
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "RemoveMany", documents);
        }
        public async Task<Dictionary<string,dynamic>> UploadFile(Types type, Dictionary<string, dynamic> values)
        {
            ObjectId Id = await _buckets[type.Connection].UploadFromBytesAsync(values["filename"], Convert.FromBase64String(values["fileId"]));
            byte[] bytes = Convert.FromBase64String(values["fileId"]);
            GridFSFileInfo fileInfo = await _buckets[type.Connection].Find(Builders<GridFSFileInfo>.Filter.Eq("_id", Id)).SingleAsync();
            var result = Result(fileInfo.ToBsonDocument());
            result.Remove("filename");
            return result;
        }
        public async Task<string> ReadFileAsBase64(string type, string id)
        {
            Types t = _types[type];
            if (id == "")
            {
                return "";
            }
            byte[] bytes = await _buckets[t.Connection].DownloadAsBytesAsync(new ObjectId(id));            
            return Convert.ToBase64String(bytes);
        }
        public async Task<bool> DeleteFile(Types type, string id)
        {
            if (id == "")
            {
                return false;
            }
            await _buckets[type.Connection].DeleteAsync(new ObjectId(id));
            return true;
        }
        public string CreateThumbnail(string photo)
        {
            byte[] bytes = Convert.FromBase64String(photo);
            Image image;
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                image = Image.FromStream(ms);
                image = new Bitmap(image, new Size((int)(128 * ((float)image.Width / image.Height)), 128));
            }
            MemoryStream stream = new MemoryStream();
            image.Save(stream, ImageFormat.Jpeg);
            byte[] bytesFromStream = stream.ToArray();
            return Convert.ToBase64String(bytesFromStream);
        }
    }
}
