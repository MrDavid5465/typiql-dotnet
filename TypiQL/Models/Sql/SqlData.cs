using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using GraphQL;
using GraphQL.Types;
using LinqKit;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace DataCrush.TypiQL.Models.Sql
{
    public class SqlData
    {
        private readonly ConfigData _data;
        private readonly Dictionary<string, Connection> _connections;
        private readonly SchemaHelpers _helpers;
        public SqlData(ConfigData data, SchemaHelpers helpers)
        {
            _data = data;
            List<Connection> connections = data.GetConnections("sql").Result;
            _connections = new Dictionary<string, Connection>();
            foreach (Connection c in connections)
            {
                _connections.Add(c.Id.ToString(), c);
            }
            _helpers = helpers;
        }
        public string BuildQuery(IResolveFieldContext context, Types t, Dictionary<string, dynamic> keys, ref Dictionary<string, dynamic> values, string query = "")
        {
            Model model = t.Model;
            var parameters = new List<string>();
            var options = new List<string>();
            if (model.ModelType == "table")
            {
                List<string> dataNames = new List<string>();
                if (context == null)
                {
                    foreach(var column in model.Columns)
                    {                        
                        if (column.DataName != null && column.DataName != "" && column.ColumnType != "Aggregation")
                            dataNames.Add($"{column.DataName} AS '{column.DataName}'");
                    }
                }
                else
                {
                    foreach (var field in context.SubFields)
                    {
                        Column column = model.Fields[field.Key];
                        if (column.DataName != null && column.DataName != "")
                        {
                            var aggregations = new List<string> { "Count", "Sum", "Average", "Max", "Min" };
                            if (column.ColumnType == "Aggregation" && column.Arguments.Where(c => aggregations.Contains(c.Type)).Any())
                            {
                                Argument aggregate = column.Arguments.Where(c => aggregations.Contains(c.Type)).FirstOrDefault();
                                dataNames.Add($"{aggregate.Type}({aggregate.Value}) AS '{column.DataName}'");
                            }
                            else
                            {
                                dataNames.Add($"{column.DataName} AS '{column.DataName}'");
                            }
                        }
                    }
                }                
                foreach (KeyValuePair<string, dynamic> key in keys)
                {
                    if (key.Value == null || key.Value is string && key.Value == "")
                    {

                    }
                    else if (key.Key.StartsWith("_orderBy"))
                    {
                        if (!keys.ContainsKey("_start"))
                        {
                            var sort = new List<string>();
                            foreach (string field in ((string)key.Value).Split(","))
                            {
                                sort.Add(t.Model.Fields[field.Trim()].DataName);
                            }
                            options.Add($"ORDER BY {string.Join(",", sort)} {(key.Key.Length > 2 ? key.Key.Split("_")[2] : "")}");
                        }
                    }
                    else if (key.Key == "_start")
                    {
                        var order = "";
                        if (keys.ContainsKey("_orderBy") && keys["_orderBy"] != null)
                        {
                            order = keys["_orderBy"];
                        }
                        else if (keys.ContainsKey("_orderBy_desc") && keys["_orderBy_desc"] != null)
                        {
                            order = keys["_orderBy_desc"];
                        }
                        else
                        {
                            var sort = new List<string>();
                            foreach (string field in model.Key)
                            {
                                sort.Add(t.Model.Fields[field.Trim()].DataName);
                            }
                            order = $"ORDER BY {string.Join(",", sort)} {(key.Key.Length > 2 ? key.Key.Split("_")[2] : "")}";
                        }
                        options.Add($"{order} OFFSET @_start ROWS");
                        values.Add("_start", (int)key.Value);
                        if (keys.ContainsKey("_limit") && keys["_limit"] != null)
                        {
                            options.Add($"FETCH NEXT @_limit ROWS");
                            values.Add("_limit", (int)keys["_limit"]);
                        }
                    }
                    else if (key.Key == "_limit")
                    {
                        if (!keys.ContainsKey("_start"))
                        {
                            query = $"SELECT TOP (@_limit) {string.Join(",", dataNames)} FROM [dbo].[{model.Name}] ";
                            values.Add(key.Key, (int)key.Value);
                        }
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "startsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} LIKE @{key.Key}");
                        values.Add(key.Key, $"{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "endsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} LIKE @{key.Key}");
                        values.Add(key.Key, $"%{key.Value}");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notStartsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT LIKE @{key.Key}");
                        values.Add(key.Key, $"{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notEndsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT LIKE @{key.Key}");
                        values.Add(key.Key, $"%{key.Value}");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "contains")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} LIKE @{key.Key}");
                        values.Add(key.Key, $"%{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notContains")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT LIKE @{key.Key}");
                        values.Add(key.Key, $"%{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lte")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} <= @{key.Key}");
                        values.Add(key.Key, key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lt")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} < @{key.Key}");
                        values.Add(key.Key, key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gte")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} >= @{key.Key}");
                        values.Add(key.Key, key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gt")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} > @{key.Key}");
                        values.Add(key.Key, key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "in")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} IN @{key.Key}");
                        values.Add(key.Key, key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notIn")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT IN @{key.Key}");
                        values.Add(key.Key, key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "not")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} != @{key.Key}");
                        values.Add(key.Key, $"{key.Value}");
                    }
                    else if (key.Value != null)
                    {
                        parameters.Add($"{model.Fields[key.Key].DataName} = @{key.Key}");
                        values.Add(key.Key, key.Value);
                    }
                }
                query = query == "" ? $"SELECT {string.Join(",", dataNames)} FROM {model.Name}" : query;
                if (parameters.Count > 0)
                {
                    query += $" WHERE {string.Join(" AND ", parameters)}";
                }                
                query += $" {string.Join(" ", options)}";
            }
            else if (model.ModelType == "storedProcedure")
            {
                query = $"EXEC [dbo].[{model.Name}] ";
                foreach (string key in model.Key)
                {
                    parameters.Add($"@p{model.Key.FindIndex(k => k == key)}");
                    values.Add($"p{model.Key.FindIndex(k => k == key)}", keys[key]);
                }
                query += string.Join(", ", parameters);
            }
            return query;
        }
        public string BuildFilter(Types t, Dictionary<string, dynamic> keys, string part, ref Dictionary<string, dynamic> values, string query = "")
        {
            Model model = t.Model;
            var parameters = new List<string>();
            var options = new List<string>();
            if (model.ModelType == "table")
            {
                foreach (KeyValuePair<string, dynamic> key in keys)
                {
                    if (key.Value == null || key.Value is string && key.Value == "")
                    {

                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "startsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} LIKE @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", $"{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "endsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} LIKE @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", $"%{key.Value}");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notStartsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT LIKE @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", $"{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notEndsWith")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT LIKE @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", $"%{key.Value}");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "contains")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} LIKE @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", $"%{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notContains")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT LIKE @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", $"%{key.Value}%");
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lte")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} <= @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lt")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} < @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gte")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} >= @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gt")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} > @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "in")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} IN @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notIn")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} NOT IN @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", key.Value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "not")
                    {
                        parameters.Add($"{model.Fields[key.Key.Split("_")[0]].DataName} != @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", $"{key.Value}");
                    }
                    else if (key.Value != null)
                    {
                        parameters.Add($"{model.Fields[key.Key].DataName} = @{key.Key}{part}");
                        values.Add($"{key.Key}{part}", key.Value);
                    }
                }
                if (parameters.Count > 0)
                {
                    query += $"({string.Join(" AND ", parameters)})";
                }
            }
            return query;
        }
        public async Task<Dictionary<string, dynamic>> BatchRecord(string type, IEnumerable<string> keySets, Types parentType, string parentField)
        {
            Types t = _data.typeDict[type];
            List<string> filters = new List<string>();
            Dictionary<string, Dictionary<string, dynamic>> queries = new Dictionary<string, Dictionary<string, dynamic>>();
            Dictionary<string, ExpressionStarter<Dictionary<string, object>>> linqFilters = new Dictionary<string, ExpressionStarter<Dictionary<string, dynamic>>>();

            Dictionary<string, dynamic> result;
            Dictionary<string, dynamic> filtersFromResults = new Dictionary<string, dynamic>();
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();

            var results = new List<Dictionary<string, dynamic>>();
            List<string> dataNames = new List<string>();

            foreach (var column in t.Model.Columns)
            {
                if (column.DataName != null && column.DataName != "")
                    dataNames.Add($"{column.DataName} AS '{column.DataName}'");
            }

            int part = 0;
            int pos = 0;
            foreach (var json in keySets)
            {
                part++;
                pos++;
                Dictionary<string, dynamic> keys = (Dictionary<string, dynamic>)BsonTypeMapper.MapToDotNetValue(BsonDocument.Parse(json));
                var filter = BuildFilter(t, keys, part.ToString(), ref values);
                filters.Add(filter);
                if (values.Count > 2000 || keySets.Count() == pos)
                {

                    string query = $"SELECT {string.Join(",", dataNames)} FROM {t.Model.Name}";
                    if (filters.Count > 0)
                    {
                        query += $" WHERE {string.Join(" OR ", filters)}";
                    }
                    queries.Add(query, values);
                    filters = new List<string>();
                    values = new Dictionary<string, dynamic>();
                }
                linqFilters.Add(json, _helpers.BuildLinqFilter(t, keys));
                filtersFromResults.Add(json, null);
            }

            bool fail = false;
            foreach (var query in queries)
            {
                using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
                {
                    foreach (IDictionary<string, dynamic> row in await connection.QueryAsync(query.Key, query.Value))
                    {
                        results.Add(new Dictionary<string, dynamic>(row));
                        Dictionary<string, dynamic> obj = new Dictionary<string, dynamic>(row);

                        if (!fail)
                        {
                            try
                            {
                                string filter = _helpers.BuildFilterFromResult(t, parentType, parentField, obj);
                                if (filter == "invalid")
                                {
                                    fail = true;
                                }
                                else
                                {
                                    filtersFromResults[filter] = obj;
                                }                                
                            }
                            catch
                            {
                                fail = true;
                            }
                        }
                    }
                }
            }
            if (fail)
            {
                result = new Dictionary<string, dynamic>();
                foreach (var linq in linqFilters)
                {
                    var r = results.AsQueryable().Where(linq.Value).FirstOrDefault();
                    result.Add(linq.Key, r);
                }
            }
            else
            {
                result = filtersFromResults;
            }
            return result;
        }
        public async Task<Dictionary<string, dynamic>> BatchRecords(string type, IEnumerable<string> keySets, Types parentType, string parentField)
        {
            Types t = _data.typeDict[type];
            List<string> filters = new List<string>();
            Dictionary<string, Dictionary<string,dynamic>> queries = new Dictionary<string, Dictionary<string, dynamic>>();
            Dictionary<string, ExpressionStarter<Dictionary<string, object>>> linqFilters = new Dictionary<string, ExpressionStarter<Dictionary<string, dynamic>>>();

            Dictionary<string, dynamic> result;
            Dictionary<string, dynamic> filtersFromResults = new Dictionary<string, dynamic>();
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            
            var results = new List<Dictionary<string, dynamic>>();
            List<string> dataNames = new List<string>();
            
            foreach (var column in t.Model.Columns)
            {
                if (column.DataName != null && column.DataName != "")
                    dataNames.Add($"{column.DataName} AS '{column.DataName}'");
            }
            
            int part = 0;
            int pos = 0;
            foreach (var json in keySets)
            {
                part++;
                pos++;
                Dictionary<string, dynamic> keys = (Dictionary<string, dynamic>)BsonTypeMapper.MapToDotNetValue(BsonDocument.Parse(json));
                var filter = BuildFilter(t, keys, part.ToString(), ref values);
                filters.Add(filter);
                if (values.Count > 2000 || keySets.Count() == pos)
                {
                    
                    string query = $"SELECT {string.Join(",", dataNames)} FROM {t.Model.Name}";
                    if (filters.Count > 0)
                    {
                        query += $" WHERE {string.Join(" OR ", filters)}";
                    }
                    queries.Add(query, values);
                    filters = new List<string>();
                    values = new Dictionary<string, dynamic>();
                }
                linqFilters.Add(json, _helpers.BuildLinqFilter(t, keys));
                filtersFromResults.Add(json, new List<dynamic>());
            }

            bool fail = false;
            foreach(var query in queries)
            {
                using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
                {
                    foreach (IDictionary<string, dynamic> row in await connection.QueryAsync(query.Key, query.Value))
                    {
                        results.Add(new Dictionary<string, dynamic>(row));
                        Dictionary<string, dynamic> obj = new Dictionary<string, dynamic>(row);

                        if (!fail)
                        {
                            try
                            {
                                string filter = _helpers.BuildFilterFromResult(t, parentType, parentField, obj);
                                if (filter == "invalid")
                                {
                                    fail = true;
                                }
                                else
                                {
                                    ((List<dynamic>)filtersFromResults[filter]).Add(obj);
                                }
                            }
                            catch
                            {
                                fail = true;
                            }                            
                        }                                                
                    }
                }
            }
            if (fail)
            {
                result = new Dictionary<string, dynamic>();
                foreach (var linq in linqFilters)
                {
                    var r = results.AsQueryable().Where(linq.Value).ToList();
                    result.Add(linq.Key, r);
                }
            }
            else
            {
                result = filtersFromResults;
            }
            return result;
        }
             
        public async Task<dynamic> GetRecord(IResolveFieldContext context, string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(context, t, keys,  ref values);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = await connection.QueryFirstOrDefaultAsync(query, values);
                return result == null ? null : new Dictionary<string, dynamic>(result);
            }
        }
        public async Task<List<dynamic>> GetRecords(IResolveFieldContext context, string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(context, t, keys, ref values);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = new List<dynamic>();
                foreach (IDictionary<string,dynamic> row in await connection.QueryAsync(query, values))
                {
                    result.Add(new Dictionary<string, dynamic>(row));
                }
                return result;
            }
        }
        public async Task<dynamic> CountRecords(IResolveFieldContext context, string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = new Dictionary<string,dynamic>();
            string query = BuildQuery(null, t, keys, ref values, $"SELECT COUNT({t.Model.Fields[t.Model.Key[0]].DataName}) AS {t.Name}Count FROM {t.Model.Name}");
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = 0;
                await connection.QueryAsync(query, values);
                return result;
            }
        }
        public async Task<dynamic> SumRecords(IResolveFieldContext context, string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(null, t, keys, ref values, $"SELECT SUM({t.Model.Fields[t.Model.Key[0]].DataName}) FROM {t.Model.Name}");
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = 0;
                await connection.QueryAsync(query, values);
                return result;
            }
        }
        public async Task<dynamic> AverageRecords(IResolveFieldContext context, string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(null, t, keys, ref values, $"SELECT AVERAGE({t.Model.Fields[t.Model.Key[0]].DataName}) FROM {t.Model.Name}");
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = 0;
                await connection.QueryAsync(query, values);
                return result;
            }
        }
        public async Task<dynamic> MinValue(IResolveFieldContext context, string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(null, t, keys, ref values, $"SELECT AVERAGE({t.Model.Fields[t.Model.Key[0]].DataName}) FROM {t.Model.Name}");
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = 0;
                await connection.QueryAsync(query, values);
                return result;
            }
        }
        public async Task<dynamic> MaxValue(IResolveFieldContext context, string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(null, t, keys, ref values, $"SELECT AVERAGE({t.Model.Fields[t.Model.Key[0]].DataName}) FROM {t.Model.Name}");
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = 0;
                await connection.QueryAsync(query, values);
                return result;
            }
        }
        //Max
        //Min
        public async Task<dynamic> AddRecord(IResolveFieldContext context, string type, Dictionary<string, dynamic> values)
        {
            Types t = _data.typeDict[type];
            List<string> columns = new List<string>();
            List<string> parameters = new List<string>();
            foreach (KeyValuePair<string, dynamic> kv in values)
            {
                columns.Add($"[{t.Model.Fields[kv.Key].DataName}]");
                parameters.Add($"@{kv.Key}");
            }
            string query = $"INSERT INTO [dbo].[{t.Model.Name}] ({string.Join(",", columns)}) VALUES ({string.Join(",", parameters)})";
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                await connection.QueryAsync(query, values);
            }
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Add", values);
        }
        public async Task<dynamic> UpdateRecord(IResolveFieldContext context, string type, Dictionary<string, dynamic> filter, Dictionary<string, dynamic> values)
        {
            Types t = _data.typeDict[type];
            List<string> columns = new List<string>();
            Dictionary<string, dynamic> queryValues = new Dictionary<string, dynamic>();
            foreach (KeyValuePair<string, dynamic> kv in values)
            {
                columns.Add($"[{t.Model.Fields[kv.Key].DataName}] = @value{kv.Key}");
                queryValues.Add($"value{kv.Key}", kv.Value);
            }
            string query = $"UPDATE [dbo].[{t.Model.Name}] SET {string.Join(",", columns)} FROM [dbo].[{t.Model.Name}]";
            query = BuildQuery(null, t, filter, ref queryValues, query);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                await connection.QueryAsync(query, queryValues);
            }
            queryValues = new Dictionary<string, dynamic>();
            query = BuildQuery(context, t, filter, ref queryValues);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Update", new Dictionary<string, dynamic>(await connection.QueryFirstOrDefaultAsync(query, queryValues)));
            }
        }
        public async Task<dynamic> RemoveRecord(IResolveFieldContext context, string type, Dictionary<string, dynamic> filter)
        {
            Types t = _data.typeDict[type];
            Dictionary<string, dynamic> values = await GetRecord(context, type, filter);
            Dictionary<string, dynamic> queryValues = new Dictionary<string, dynamic>();
            string query = $"DELETE TOP (1) FROM [dbo].[{t.Model.Name}]";
            query = BuildQuery(context, t, filter, ref queryValues, query);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                await connection.QueryAsync(query, queryValues);
            }
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Remove", values);
        }
    }
}
