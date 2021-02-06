using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace DataCrush.TypiQL.Models.Sql
{
    public class SqlData
    {
        private readonly ConfigData _data;
        private readonly Dictionary<string, Connection> _connections;
        private readonly Dictionary<string, Types> _types;
        public SqlData(ConfigData data)
        {
            _data = data;
            List<Connection> connections = data.GetConnections("sql").Result;
            _connections = new Dictionary<string, Connection>();
            foreach (Connection c in connections)
            {
                _connections.Add(c.Id.ToString(), c);
            }
            List<Types> types = data.GetTypes("sql").Result;
            _types = new Dictionary<string, Types>();
            foreach (Types t in types)
            {
                _types.Add(t.Name, t);
            }
        }
        public string BuildQuery(Types t, Dictionary<string, dynamic> keys, ref Dictionary<string, dynamic> values, string query = "")
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
                            query = $"SELECT TOP (@_limit) * FROM [dbo].[{model.Name}] ";
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
                List<string> dataNames = new List<string>();
                foreach(var column in model.Columns)
                {
                    if (column.DataName != null && column.DataName != "")
                        dataNames.Add($"{column.DataName} AS '{column.DataName}'");
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
        public async Task<dynamic> GetRecord(string type, Dictionary<string, dynamic> keys)
        {
            Types t = _types[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(t, keys,  ref values);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                var result = await connection.QueryFirstOrDefaultAsync(query, values);
                return result == null ? null : new Dictionary<string, dynamic>(result);
            }
        }
        public async Task<List<dynamic>> GetRecords(string type, Dictionary<string, dynamic> keys)
        {
            Types t = _types[type];
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query = BuildQuery(t, keys, ref values);
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
        public async Task<dynamic> AddRecord(string type, Dictionary<string, dynamic> values)
        {
            Types t = _types[type];
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
        public async Task<dynamic> UpdateRecord(string type, Dictionary<string, dynamic> filter, Dictionary<string, dynamic> values)
        {
            Types t = _types[type];
            List<string> columns = new List<string>();
            Dictionary<string, dynamic> queryValues = new Dictionary<string, dynamic>();
            foreach (KeyValuePair<string, dynamic> kv in values)
            {
                columns.Add($"[{t.Model.Fields[kv.Key].DataName}] = @value{kv.Key}");
                queryValues.Add($"value{kv.Key}", kv.Value);
            }
            string query = $"UPDATE [dbo].[{t.Model.Name}] SET {string.Join(",", columns)} FROM [dbo].[{t.Model.Name}]";
            query = BuildQuery(t, filter, ref queryValues, query);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                await connection.QueryAsync(query, queryValues);
            }
            queryValues = new Dictionary<string, dynamic>();
            query = BuildQuery(t, filter, ref queryValues);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Update", new Dictionary<string, dynamic>(await connection.QueryFirstOrDefaultAsync(query, queryValues)));
            }
        }
        public async Task<dynamic> RemoveRecord(string type, Dictionary<string, dynamic> filter)
        {
            Types t = _types[type];
            Dictionary<string, dynamic> values = await GetRecord(type, filter);
            Dictionary<string, dynamic> queryValues = new Dictionary<string, dynamic>();
            string query = $"DELETE TOP (1) FROM [dbo].[{t.Model.Name}]";
            query = BuildQuery(t, filter, ref queryValues, query);
            using (SqlConnection connection = new SqlConnection(_connections[t.Connection].ConnectionString))
            {
                await connection.QueryAsync(query, queryValues);
            }
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Remove", values);
        }
    }
}
