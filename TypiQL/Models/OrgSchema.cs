using DataCrush.TypiQL.Models.AD;
using DataCrush.TypiQL.Models.Mongo;
using DataCrush.TypiQL.Models.Sql;
using GraphQL;
using GraphQL.Resolvers;
using GraphQL.Server.Authorization.AspNetCore;
using GraphQL.Types;
using GraphQL.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TypiQL.Models;

namespace DataCrush.TypiQL.Models
{
    public class BaseSchema : Schema
    {
        private readonly ConfigData _data;
        private readonly IHttpContextAccessor _accessor;
        private readonly ADData _adData;
        private readonly MongoData _mongoData;
        private readonly SqlData _sqlData;
        private readonly TypiQLSettings _settings;
        public BaseSchema(TypiQLSettings settings, IServiceProvider provider) : base(provider)
        {
            _accessor = provider.GetRequiredService<IHttpContextAccessor>();
            _data = provider.GetRequiredService<ConfigData>();
            _adData = provider.GetRequiredService<ADData>();
            _adData.GetTypes();
            _mongoData = provider.GetRequiredService<MongoData>();
            _sqlData = provider.GetRequiredService<SqlData>();
            _settings = settings;

            Query = new Queries(_settings, _data, _accessor, _mongoData, _adData, _sqlData, provider);
            Mutation = new Mutations(_settings, _data, _accessor);
            Subscription = new Subscriptions(_settings, _data, provider.GetRequiredService<TypiQLMongoContext>());
        }
    }
    public class OrgSchema : Schema
    {
        private readonly ConfigData _data;
        private readonly IHttpContextAccessor _accessor;
        private readonly ADData _adData;
        private readonly MongoData _mongoData;
        private readonly SqlData _sqlData;
        private List<Types> _types;
        private readonly TypiQLSettings _settings;

        private Dictionary<string, Types> _typeDict
        {
            get
            {
                Dictionary<string, Types> types = new Dictionary<string, Types>();
                foreach (Types c in _types)
                {
                    types.Add(c.Name, c);
                }
                return types;
            }
        }

        public OrgSchema(TypiQLSettings settings, IServiceProvider provider) : base(provider)
        {
            _accessor = provider.GetRequiredService<IHttpContextAccessor>();
            _data = provider.GetRequiredService<ConfigData>();
            _adData = provider.GetRequiredService<ADData>();
            _adData.GetTypes();
            _mongoData = provider.GetRequiredService<MongoData>();
            _sqlData = provider.GetRequiredService<SqlData>();
            _settings = settings;
            
            foreach (CustomResolver cr in _settings.Resolvers)
            {
                cr.GetFieldResolver(provider);
            }

            Query = new Queries(_settings, _data, _accessor, _mongoData, _adData, _sqlData, provider);
            Mutation = new Mutations(_settings, _data, _accessor);
            Subscription = new Subscriptions(_settings, _data, provider.GetRequiredService<TypiQLMongoContext>());

            GenerateSchema();
        }
        public void ReloadTypeDict()
        {
            _types = _data.GetTypes().Result;
        }
        public void GenerateSchema()
        {
            ReloadTypeDict();
            ISchema userSchema = BuildSchemaFromSDL(_types);
            foreach (ObjectGraphType type in userSchema.AllTypes.Where(t => t is ObjectGraphType))
            {
                if (_typeDict.ContainsKey(type.Name))
                {
                    Types thisType = _typeDict[type.Name];
                    type.Description = thisType.Model.Description;
                    type.DeprecationReason = thisType.Model.Deprecated;
                    foreach (FieldType field in type.Fields)
                    {
                        Column thisColumn = thisType.Model.Fields[field.Name];
                        ResolvedType resolvedTypeInfo = _data.ResolveType(field);
                        field.Description = thisColumn.Description;
                        field.DeprecationReason = thisColumn.Deprecated;
                        field.Resolver = new FuncFieldResolver<dynamic>(context =>
                        {
                            if (!(context.Source is IDictionary<string, dynamic> obj))
                            {
                                return null;
                            }
                            obj = new Dictionary<string, dynamic>(obj);
                            if (resolvedTypeInfo.TypeStack.Contains("array")
                                && _typeDict.ContainsKey(resolvedTypeInfo.Name)
                                && thisColumn.Arguments.Count > 0)
                            {
                                return GetMany(_typeDict[resolvedTypeInfo.Name], BuildFilter(thisType, field.Name, obj as Dictionary<string, dynamic>));
                            }
                            else if (!resolvedTypeInfo.TypeStack.Contains("array")
                                && _typeDict.ContainsKey(resolvedTypeInfo.Name)
                                && thisColumn.Arguments.Count > 0)
                            {
                                return GetOne(_typeDict[resolvedTypeInfo.Name], BuildFilter(thisType, field.Name, obj as Dictionary<string, dynamic>));
                            }
                            else if (!obj.ContainsKey(thisColumn.DataName))
                            {
                                return null;
                            }
                            else if (thisColumn.ColumnType == "File")
                            {
                                return GetFile(thisType, obj[thisColumn.DataName]);
                            }
                            else
                            {
                                return obj[thisColumn.DataName];
                            }
                        });
                        if (thisColumn.AllowedGroups.Count() > 0)
                        {
                            field.AuthorizeWith(string.Join(",", thisColumn.AllowedGroups));
                        }

                    }
                    RegisterType(type);
                    type.GetType();
                    SubscriberType<ObjectGraphType> subscriberType = new SubscriberType<ObjectGraphType>(type);
                    RegisterType(subscriberType);
                    var name = $"{type.Name.Substring(0, 1).ToLower()}{type.Name.Substring(1)}Changed";
                    if (thisType.Subscriptions == null || !thisType.SubscriptionsDict.ContainsKey(name))
                    {
                        Subscription.AddField(new EventStreamFieldType
                        {
                            Name = name,
                            Type = subscriberType.GetType(),
                            ResolvedType = subscriberType.GetNamedType(),
                            Resolver = new FuncFieldResolver<Subscriber<dynamic>>(context =>
                            {
                                return context.Source as Subscriber<dynamic>;
                            }),
                            AsyncSubscriber = new AsyncEventStreamResolver<Subscriber<dynamic>>(async context =>
                            {
                                return await _data.SubscribeToType(thisType);
                            })
                        });
                    }
                    if (thisType.Subscriptions != null)
                    {
                        foreach (Query sub in thisType.Subscriptions)
                        {
                            EventStreamFieldType subscription = new EventStreamFieldType
                            {
                                Name = sub.Name,
                                Arguments = userSchema.Subscription.Fields.Where(s => s.Name == sub.Name).First().Arguments,
                                Description = sub.Description,
                                DeprecationReason = sub.Deprecated,
                                Type = subscriberType.GetType(),
                                ResolvedType = subscriberType.GetNamedType(),
                                Resolver = new FuncFieldResolver<Subscriber<dynamic>>(context =>
                                {
                                    var result = context.Source;
                                    Subscriber<Dictionary<string, dynamic>> dict = result as Subscriber<Dictionary<string, dynamic>>;
                                    Subscriber<dynamic> dyn = new Subscriber<dynamic>
                                    {
                                        OperationName = dict.OperationName,
                                        Value = dict.Value
                                    };
                                    return dyn;
                                })
                            };
                            ResolvedType resolvedTypeInfo = _data.ResolveType(subscription);

                            List<QueryArgument> filterArgs = FilterSubscriptionArgs(thisType, subscription, sub, userSchema);
                            foreach (QueryArgument argument in subscription.Arguments)
                            {
                                if (sub.ArgumentsDict.ContainsKey(argument.Name))
                                {
                                    argument.Description = sub.ArgumentsDict[argument.Name].Description;
                                }
                            }
                            foreach (QueryArgument a in filterArgs)
                            {
                                subscription.Arguments.Add(a);
                            }
                            subscription.Arguments.Add(new QueryArgument<StringGraphType> { Name = "operationName", ResolvedType = new StringGraphType().GetNamedType() });
                            subscription.Arguments.Add(new QueryArgument<StringGraphType> { Name = "operationName_not", ResolvedType = new StringGraphType().GetNamedType() });
                            subscription.Arguments.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = "operationName_in", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                            subscription.Arguments.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = "operationName_notIn", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                            subscription.AsyncSubscriber = new AsyncEventStreamResolver<Subscriber<Dictionary<string, dynamic>>>(async context =>
                            {
                                Dictionary<string, dynamic> filter = BuildQueryFilter(thisType, sub.Arguments, subscription, context);
                                return await _data.Subscription(thisType, filter);
                            });
                            if (sub.AllowedGroups.Count() > 0)
                            {
                                Subscription.AddField(subscription).AuthorizeWith(string.Join(",", sub.AllowedGroups));
                            }
                            else
                            {
                                Subscription.AddField(subscription);
                            }

                        }
                    }
                }
            }
            foreach (InputObjectGraphType type in userSchema.AllTypes.Where(t => t is InputObjectGraphType))
            {
                Types thisType = _types.Where(t => t.InputTypesDict.ContainsKey(type.Name)).SingleOrDefault();
                if (thisType != null)
                {
                    type.Description = thisType.InputTypesDict[type.Name];
                    foreach (var field in type.Fields)
                    {
                        if (thisType.Model.Fields.ContainsKey(field.Name))
                        {
                            field.Description = thisType.Model.Fields[field.Name].Description;
                        }
                    }
                }
                RegisterType(type);
            }
            foreach (FieldType query in userSchema.Query.Fields)
            {
                Types thisType = _typeDict[query.ResolvedType.GetNamedType().Name];
                Query thisQuery = thisType.QueriesDict[query.Name];
                ResolvedType resolvedTypeInfo = _data.ResolveType(query);
                query.Description = thisQuery.Description;
                query.DeprecationReason = thisQuery.Deprecated;

                foreach (QueryArgument argument in query.Arguments)
                {
                    if (thisQuery.ArgumentsDict.ContainsKey(argument.Name))
                    {
                        argument.Description = thisQuery.ArgumentsDict[argument.Name].Description;
                    }
                }

                List<QueryArgument> filterArgs = FilterArgs(thisType, query, thisQuery, userSchema);
                foreach (QueryArgument a in filterArgs)
                {
                    query.Arguments.Add(a);
                }
                if (_settings.ResolversDict.ContainsKey(query.Name))
                {
                    query.Resolver = _settings.ResolversDict[query.Name];
                }
                else
                {
                    query.Resolver = new FuncFieldResolver<dynamic>(context =>
                    {
                        Dictionary<string, dynamic> filter = BuildQueryFilter(thisType, thisQuery.Arguments, query, context);
                        if (thisQuery.Type == "List")
                        {
                            return GetMany(thisType, filter);
                        }
                        else if (thisQuery.Type == "Get")
                        {
                            return GetOne(thisType, filter);
                        }
                        else
                        {
                            return null;
                        }
                    });
                }
                if (thisQuery.AllowedGroups.Count() > 0)
                {
                    Query.AddField(query).AuthorizeWith(string.Join(",", thisQuery.AllowedGroups));
                }
                else
                {
                    Query.AddField(query);
                }


            }
            foreach (FieldType mutation in userSchema.Mutation.Fields)
            {
                Types thisType = _typeDict[mutation.ResolvedType.GetNamedType().Name];
                Query thisQuery = thisType.MutationsDict[mutation.Name];
                ResolvedType resolvedTypeInfo = _data.ResolveType(mutation);
                mutation.Description = thisQuery.Description;
                mutation.DeprecationReason = thisQuery.Deprecated;

                foreach (QueryArgument argument in mutation.Arguments)
                {
                    if (thisQuery.ArgumentsDict.ContainsKey(argument.Name))
                    {
                        argument.Description = thisQuery.ArgumentsDict[argument.Name].Description;
                    }
                }

                List<QueryArgument> filterArgs = FilterArgs(thisType, mutation, thisQuery, userSchema);
                foreach (QueryArgument a in filterArgs)
                {
                    mutation.Arguments.Add(a);
                }
                if (_settings.ResolversDict.ContainsKey(mutation.Name))
                {
                    mutation.Resolver = _settings.ResolversDict[mutation.Name];
                }
                else
                {
                    mutation.Resolver = new FuncFieldResolver<dynamic>(context =>
                    {
                        Dictionary<string, dynamic> filter = BuildQueryFilter(thisType, thisQuery.Arguments, mutation, context);
                        Dictionary<string, dynamic> values = GetValues(thisType, thisQuery.Arguments, mutation, context, filter);
                        List<Dictionary<string, dynamic>> manyValues = GetManyValues(thisType, thisQuery.Arguments, mutation, context, filter);
                        if (thisQuery.Type == "Add")
                        {
                            return AddOne(thisType, values);
                        }
                        else if (thisQuery.Type == "Update")
                        {
                            return UpdateOne(thisType, filter, values);
                        }
                        else if (thisQuery.Type == "Remove")
                        {
                            return RemoveOne(thisType, filter);
                        }
                        else if (thisQuery.Type == "RemoveMany")
                        {
                            return RemoveMany(thisType, filter);
                        }
                        else if (thisQuery.Type == "AddMany")
                        {
                            return AddMany(thisType, manyValues);
                        }
                        else if (thisQuery.Type == "UpdateMany")
                        {
                            return UpdateMany(thisType, filter, values);
                        }
                        else
                        {
                            return null;
                        }
                    });
                }
                if (thisQuery.AllowedGroups.Count() > 0)
                {
                    Mutation.AddField(mutation).AuthorizeWith(string.Join(",", thisQuery.AllowedGroups));
                }
                else
                {
                    Mutation.AddField(mutation);
                }
            }

        }
        public dynamic ResolveField(Types thisType, Column thisColumn, string fieldName, IDictionary<string, dynamic> obj, ResolvedType resolvedTypeInfo)
        {
            obj = new Dictionary<string, dynamic>(obj);
            if (resolvedTypeInfo.TypeStack.Contains("array")
                && _typeDict.ContainsKey(resolvedTypeInfo.Name)
                && thisColumn.Arguments.Count > 0)
            {
                return GetMany(_typeDict[resolvedTypeInfo.Name], BuildFilter(thisType, fieldName, obj as Dictionary<string, dynamic>));
            }
            else if (!resolvedTypeInfo.TypeStack.Contains("array")
                && _typeDict.ContainsKey(resolvedTypeInfo.Name)
                && thisColumn.Arguments.Count > 0)
            {
                return GetOne(_typeDict[resolvedTypeInfo.Name], BuildFilter(thisType, fieldName, obj as Dictionary<string, dynamic>));
            }
            else if (!obj.ContainsKey(thisColumn.DataName))
            {
                return null;
            }
            else
            {
                return obj[thisColumn.DataName];
            }
        }
        public dynamic ResolveUser(string arg, Dictionary<string, dynamic> arguments = null)
        {
            Dictionary<string, dynamic> user = new Dictionary<string, dynamic>();
            var userType = _data.GetTypesType("User").Result;
            var userName = "";
            if (arg.StartsWith("@currentUser"))
            {
                userName = _accessor.HttpContext.User.Identity.Name.Split("\\")[1];
            }
            else if (arg.StartsWith("@user") && Regex.IsMatch(arg.Split(".")[0], "([\\(\\)])"))
            {
                userName = Regex.Match(arg.Split(".")[0], "(?<=\\()(.*?)(?=\\))").Value;
            }
            foreach (var kv in _adData.GetADObject("User", new Dictionary<string, dynamic> { { "sAMAccountName", userName } }))
            {
                user.Add(userType.Model.Columns.Find(c => c.DataName == kv.Key).Name, kv.Value);
            }
            if (arg == "@currentUser")
            {
                return user["sAMAccountName"];
            }
            else if (user.ContainsKey(arg.Split(".")[1]))
            {
                return user[arg.Split(".")[1]];
            }
            else if (arg.Split(".")[1] == "groups")
            {
                var groupNames = new List<string>();
                List<dynamic> groups =
                    _adData.GetADObjects("Group", new Dictionary<string, dynamic> { { "distinguishedName_in", user["memberOf"] } });
                foreach (Dictionary<string, dynamic> g in groups)
                {
                    groupNames.Add(g["name"]);
                }
                return groupNames;
            }
            else
            {
                Dictionary<string, dynamic> userData = new Dictionary<string, dynamic>();
                var userDataType = _data.GetTypesType("UserData").Result;
                foreach (var kv in _mongoData.GetDocument("UserData", new Dictionary<string, dynamic> { { "sid", user["objectSid"] } }).Result)
                {
                    userData.Add(userDataType.Model.Columns.Find(c => c.DataName == kv.Key).Name, kv.Value);
                }
                if (userData.ContainsKey(arg.Split(".")[1]))
                {
                    return user[arg.Split(".")[1]];
                }
                else
                {
                    return null;
                }
            }
        }
        public string GetFile(Types type, string value)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.ReadFileAsBase64(type.Name, value).Result; }
                case "sql": { return ""; }
                case "ad": { return ""; }
                default: return null;
            }
        }
        public List<dynamic> GetMany(Types type, Dictionary<string, dynamic> filter)
        {

            switch (type.Type)
            {
                case "mongo": { return _mongoData.GetDocuments(type.Name, filter).Result; }
                case "sql": { return _sqlData.GetRecords(type.Name, filter).Result; }
                case "ad": { return _adData.GetADObjects(type.Name, filter); }
                default: return null;
            }
        }
        public Dictionary<string, dynamic> GetOne(Types type, Dictionary<string, dynamic> filter)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.GetDocument(type.Name, filter).Result; }
                case "sql": { return _sqlData.GetRecord(type.Name, filter).Result; }
                case "ad": { return _adData.GetADObject(type.Name, filter); }
                default: return null;
            }
        }
        public Dictionary<string, dynamic> AddOne(Types type, Dictionary<string, dynamic> values)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.AddDocument(type.Name, values).Result; }
                case "sql": { return _sqlData.AddRecord(type.Name, values).Result; }
                case "ad": { return _adData.AddADObject(type.Name, values); }
                default: return null;
            }
        }
        public List<dynamic> AddMany(Types type, List<Dictionary<string, dynamic>> manyValues)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.AddDocuments(type.Name, manyValues).Result; }
                //case "sql": { return _sqlData.AddRecords(type.Name, manyValues).Result; }
                //case "ad": { return _adData.AddADObjects(type.Name, manyValues); }
                default: return null;
            }
        }
        public Dictionary<string, dynamic> UpdateOne(Types type, Dictionary<string, dynamic> filter, Dictionary<string, dynamic> update)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.UpdateDocument(type.Name, filter, update).Result; }
                case "sql": { return _sqlData.UpdateRecord(type.Name, filter, update).Result; }
                case "ad": { return _adData.UpdateADObject(type.Name, filter, update); }
                default: return null;
            }
        }
        public List<dynamic> UpdateMany(Types type, Dictionary<string, dynamic> filter, Dictionary<string, dynamic> update)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.UpdateDocuments(type.Name, filter, update).Result; }
                //case "sql": { return _sqlData.UpdateRecords(type.Name, filter, manyUpdate).Result; }
                //case "ad": { return _adData.UpdateADObjects(type.Name, filter, manyUpdate); }
                default: return null;
            }
        }
        public Dictionary<string, dynamic> RemoveOne(Types type, Dictionary<string, dynamic> filter)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.RemoveDocument(type.Name, filter).Result; }
                case "sql": { return _sqlData.RemoveRecord(type.Name, filter).Result; }
                case "ad": { return _adData.RemoveADObject(type.Name, filter); }
                default: return null;
            }
        }
        public List<dynamic> RemoveMany(Types type, Dictionary<string, dynamic> filter)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.RemoveDocuments(type.Name, filter).Result; }
                //case "sql": { return _sqlData.RemoveRecords(type.Name, filter).Result; }
                //case "ad": { return _adData.RemoveADObjects(type.Name, filter); }
                default: return null;
            }
        }
        public ISchema BuildSchemaFromSDL(List<Types> types)
        {
            List<string> typeSchema = new List<string>();
            List<string> queriesSchema = new List<string>();
            List<string> mutationsSchema = new List<string>();
            List<string> subscriptionSchema = new List<string>();
            foreach (Types t in types)
            {
                typeSchema.Add(t.Schema);
                typeSchema.Add($"type {t.Name}Subscriber {{ operationName: String value: {t.Name} }}");
                queriesSchema.Add(string.Join(" ", t.QueriesSchema));
                mutationsSchema.Add(string.Join(" ", t.MutationsSchema));
                subscriptionSchema.Add(string.Join(" ", t.SubscriptionsSchema));
            }
            string typesString = string.Join("\n", typeSchema);
            string queryString = $"type Query {{{string.Join("\n", queriesSchema)}}}";
            string mutationString = $"type Mutation {{{string.Join("\n", mutationsSchema)}}}";
            string subscriptionString = $"type Subscription {{{string.Join("\n", subscriptionSchema)}}}";

            return For($"{typesString}\n{queryString}\n{mutationString}\n{subscriptionString}");
        }
        public bool IsVariable(string value)
        {
            if (value.StartsWith("@"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public dynamic ResolveVariable(string variable, Dictionary<string, dynamic> arguments = null)
        {
            if (variable.StartsWith("@currentUser") || variable.StartsWith("@user"))
            {
                return ResolveUser(variable, arguments);
            }
            else if (variable.StartsWith("@dName"))
            {
                return ResolveDistinguishedName(variable, arguments);
            }
            else if (variable == "@now")
            {
                return DateTime.UtcNow;
            }
            else if (variable.StartsWith("@today"))
            {
                return DateTime.Today;
            }
            else if (variable.StartsWith("@yesterday"))
            {
                return DateTime.Today.AddDays(-1);
            }
            else if (variable.StartsWith("@tomorrow"))
            {
                return DateTime.Today.AddDays(1);
            }
            else if (variable.StartsWith("@addToArray") || variable.StartsWith("@removeFromArray"))
            {
                return ResolveArray(variable, arguments);
            }
            else return null;
        }
        public dynamic ResolveArray(string variable, Dictionary<string, dynamic> arguments = null)
        {
            List<string> args = Regex.Match(variable.Split(".")[0], "(?<=\\()(.*?)(?=\\))").Value.Split("','").ToList();

            return null;
        }
        public dynamic ResolveDistinguishedName(string variable, Dictionary<string, dynamic> arguments = null)
        {
            List<string> distinguishedNames = new List<string>();
            if (arguments == null)
            {
                distinguishedNames = Regex.Match(variable.Split(".")[0], "(?<=\\(')(.*?)(?='\\))").Value.Split("','").ToList();
            }
            else
            {
                foreach (string arg in Regex.Match(variable.Split(".")[0], "(?<=\\()(.*?)(?=\\))").Value.Split("','").ToList())
                {
                    if (new string[] { "memberOf", "member", "distinguishedName", "manager", "directReports" }.Contains(arg))
                    {
                        if (arguments[arg] is string)
                        {
                            distinguishedNames.Add(((string)arguments[arg]).Trim());
                        }
                        else
                        {
                            foreach (string dn in arguments[arg])
                            {
                                distinguishedNames.Add(dn.Trim());
                            }
                        }

                    }
                }

            }
            string field = variable.Split(".")[1].Contains("(") ? "sAMAccountName" : variable.Split(".")[1];

            Dictionary<string, dynamic> keys = new Dictionary<string, dynamic>();
            dynamic result;
            keys.Add("distinguishedName_in", distinguishedNames);
            if (distinguishedNames.Count > 1)
            {
                result = new List<string>();
                foreach (Dictionary<string, dynamic> obj in _adData.GetADObjects("User", keys))
                {
                    if (obj.ContainsKey(field) && obj[field] != null)
                    {
                        result.Add(obj[field]);
                    }
                }
            }
            else
            {
                Dictionary<string, dynamic> obj = _adData.GetADObject("user", keys);
                result = obj.ContainsKey(field) && obj[field] != null ? obj[field] : "";
            }
            return result;
        }
        public List<Dictionary<string, dynamic>> GetManyValues(
            Types type,
            List<Argument> arguments,
            FieldType query,
            IResolveFieldContext context,
            Dictionary<string, dynamic> filter
        )
        {
            List<Dictionary<string, dynamic>> manyValues = new List<Dictionary<string, dynamic>>();
            List<Dictionary<string, dynamic>> manyAllowedValues = new List<Dictionary<string, dynamic>>();
            foreach (Argument arg in arguments)
            {
                if (arg.Key == "items" || arg.Key == "updates")
                {
                    if (arg.Value is string && arg.Value == arg.Name)
                    {
                        foreach (Dictionary<string, dynamic> values in context.GetArgument<List<Dictionary<string, dynamic>>>(arg.Name))
                        {
                            Dictionary<string, dynamic> valuesToAdd = new Dictionary<string, dynamic>();
                            foreach (KeyValuePair<string, dynamic> kv in values)
                            {
                                if (!valuesToAdd.ContainsKey(kv.Key))
                                {
                                    valuesToAdd.Add(kv.Key, kv.Value);
                                }
                            }
                            manyValues.Add(valuesToAdd);
                        }
                    }
                    else
                    {
                        foreach (Dictionary<string, dynamic> argVal in arg.Value)
                        {
                            Dictionary<string, dynamic> valuesToAdd = new Dictionary<string, dynamic>();
                            foreach (KeyValuePair<string, dynamic> kv in argVal)
                            {
                                var value = kv.Value;
                                if (context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    if (kv.Value is string && (string)kv.Value == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                    {
                                        value = context.GetArgument<dynamic>(arg.Name);
                                    }
                                    else if (kv.Value is string && ((string)kv.Value).Split(".")[0] == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                    {
                                        value = context.GetArgument<dynamic>(arg.Name)[((string)kv.Value).Split(".")[1]];
                                    }
                                    else if (kv.Value is string && ((string)kv.Value).StartsWith(arg.Name) && Regex.IsMatch(kv.Value, "(?<=\\()(.*?)(?=\\))"))
                                    {
                                        value = context.GetArgument<dynamic>(arg.Name);

                                        Dictionary<string, dynamic> defaultValues = BsonTypeMapper.MapToDotNetValue(
                                            BsonDocument.Parse(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value)
                                        );
                                        foreach (KeyValuePair<string, dynamic> dv in defaultValues)
                                        {
                                            if (!((Dictionary<string, dynamic>)value).ContainsKey(dv.Key))
                                            {
                                                if (IsVariable(dv.Value))
                                                {
                                                    ((Dictionary<string, dynamic>)value).Add(dv.Key, ResolveVariable(dv.Value));
                                                }
                                                else
                                                {
                                                    ((Dictionary<string, dynamic>)value).Add(dv.Key, dv.Value);
                                                }
                                            }
                                        }
                                        valuesToAdd.Add(kv.Key, value);
                                    }
                                    else if (!(kv.Value is string) &&
                                        ((List<dynamic>)kv.Value).Contains(arg.Name) &&
                                        context.GetArgument<dynamic>(arg.Name) != null)
                                    {
                                        value[((List<dynamic>)value).FindIndex(v => v == arg.Name)] = context.GetArgument<dynamic>(arg.Name);
                                    }
                                }
                                if (!valuesToAdd.ContainsKey(kv.Key))
                                {
                                    valuesToAdd.Add(kv.Key, value);
                                }
                            }
                            manyValues.Add(valuesToAdd);
                        }
                    }
                    foreach (QueryArgument queryArgument in query.Arguments)
                    {
                        if (queryArgument.Name == "items" || queryArgument.Name == "updates")
                        {
                            foreach (Dictionary<string, dynamic> valuesToAdd in context.GetArgument<List<dynamic>>(queryArgument.Name))
                            {
                                if (arguments.FindIndex(a => a.Name == queryArgument.Name) == -1)
                                {
                                    manyValues.Add(valuesToAdd);
                                }
                            }
                        }
                    }

                    foreach (Dictionary<string, dynamic> values in manyValues)
                    {
                        Dictionary<string, dynamic> allowedValues = new Dictionary<string, dynamic>();
                        Dictionary<string, dynamic> obj = GetOne(type, filter);
                        foreach (KeyValuePair<string, dynamic> kv in values)
                        {
                            bool changeAllowed = true;
                            if (type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups != null && type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups.Count > 0)
                            {
                                changeAllowed = false;
                                foreach (string group in type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups)
                                {
                                    if (_accessor.HttpContext.User.IsInRole(group))
                                    {
                                        changeAllowed = true;
                                    }
                                }
                            }
                            if (changeAllowed)
                            {
                                if (kv.Value is string && type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(kv.Value))
                                {
                                    Argument a = type.MutationsDict[query.Name].ArgumentsDict[kv.Value];
                                    allowedValues.Add(kv.Key, context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name));
                                }
                                else if (kv.Value is string && type.Model.Fields.ContainsKey(kv.Value))
                                {
                                    Column a = type.Model.Fields[kv.Value];
                                    allowedValues.Add(kv.Key, obj[a.DataName]);
                                }
                                else if (kv.Value is string &&
                                    type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                                {
                                    Argument a = type.MutationsDict[query.Name].ArgumentsDict[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                                    if (IsVariable(kv.Value))
                                    {
                                        allowedValues.Add(
                                            kv.Key,
                                            ResolveVariable(
                                                kv.Value,
                                                Regex.Replace(
                                                    kv.Value,
                                                    "(?<=\\()(.*?)(?=\\))",
                                                    (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                                )
                                            )
                                        );
                                    }
                                    else
                                    {
                                        allowedValues.Add(
                                            kv.Key,
                                            Regex.Replace(
                                                kv.Value,
                                                "(?<=\\()(.*?)(?=\\))",
                                                (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                            )
                                        );
                                    }
                                }
                                else if (kv.Value is string && type.Model.Fields.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                                {
                                    Column a = type.Model.Fields[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                                    if (IsVariable(kv.Value))
                                    {
                                        allowedValues.Add(kv.Key, ResolveVariable(kv.Value, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName])));
                                    }
                                    else
                                    {
                                        allowedValues.Add(kv.Key, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName]));
                                    }
                                }
                                else if (kv.Value is string && IsVariable(kv.Value))
                                {
                                    allowedValues.Add(kv.Key, ResolveVariable(kv.Value));
                                }
                                else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTime")
                                {
                                    allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                                }
                                else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTimeOffset")
                                {
                                    allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                                }
                                else
                                {
                                    allowedValues.Add(kv.Key, kv.Value);
                                }
                            }
                        }
                        manyAllowedValues.Add(allowedValues);
                    }
                }
            }
            return manyAllowedValues;
        }
        public Dictionary<string, dynamic> GetValues(
            Types type,
            List<Argument> arguments,
            FieldType query,
            IResolveFieldContext context,
            Dictionary<string, dynamic> filter
        )
        {
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            foreach (Argument arg in arguments)
            {
                if (arg.Key == "values" || arg.Key == "update")
                {
                    if (arg.Value is string && arg.Value == arg.Name)
                    {
                        foreach (KeyValuePair<string, dynamic> kv in context.GetArgument<dynamic>(arg.Name))
                        {
                            if (!values.ContainsKey(kv.Key))
                            {
                                values.Add(kv.Key, kv.Value);
                            }
                        }
                    }
                    else
                    {
                        foreach (KeyValuePair<string, dynamic> kv in arg.Value)
                        {
                            var value = kv.Value;
                            if (context.GetArgument<dynamic>(arg.Name) != null)
                            {
                                if (kv.Value is string && (string)kv.Value == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    value = context.GetArgument<dynamic>(arg.Name);
                                }
                                else if (kv.Value is string && ((string)kv.Value).Split(".")[0] == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    value = context.GetArgument<dynamic>(arg.Name)[((string)kv.Value).Split(".")[1]];
                                }
                                else if (kv.Value is string && ((string)kv.Value).StartsWith(arg.Name) && Regex.IsMatch(kv.Value, "(?<=\\()(.*?)(?=\\))"))
                                {
                                    value = context.GetArgument<dynamic>(arg.Name);

                                    Dictionary<string, dynamic> defaultValues = BsonTypeMapper.MapToDotNetValue(
                                        BsonDocument.Parse(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value)
                                    );
                                    foreach (KeyValuePair<string, dynamic> dv in defaultValues)
                                    {
                                        if (!((Dictionary<string, dynamic>)value).ContainsKey(dv.Key))
                                        {
                                            if (IsVariable(dv.Value))
                                            {
                                                ((Dictionary<string, dynamic>)value).Add(dv.Key, ResolveVariable(dv.Value));
                                            }
                                            else
                                            {
                                                ((Dictionary<string, dynamic>)value).Add(dv.Key, dv.Value);
                                            }
                                        }
                                    }
                                    values.Add(kv.Key, value);
                                }
                                else if (!(kv.Value is string) &&
                                    ((List<dynamic>)kv.Value).Contains(arg.Name) &&
                                    context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    value[((List<dynamic>)value).FindIndex(v => v == arg.Name)] = context.GetArgument<dynamic>(arg.Name);
                                }
                            }
                            if (!values.ContainsKey(kv.Key))
                            {
                                values.Add(kv.Key, value);
                            }
                        }
                    }
                }
            }
            foreach (QueryArgument queryArgument in query.Arguments)
            {
                if (queryArgument.Name == "values" || queryArgument.Name == "update")
                {
                    var wat = context.Arguments[queryArgument.Name];//GetArgument<Dictionary<string, dynamic>>(queryArgument.Name);
                    var huh = context.GetArgument<dynamic>(queryArgument.Name);
                    foreach (KeyValuePair<string, dynamic> kv in context.GetArgument<dynamic>(queryArgument.Name))
                    {
                        if (!values.ContainsKey(kv.Key))
                        {
                            values.Add(kv.Key, kv.Value);
                        }
                        else
                        {
                            values[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            Dictionary<string, dynamic> allowedValues = new Dictionary<string, dynamic>();
            Dictionary<string, dynamic> obj = GetOne(type, filter);
            foreach (KeyValuePair<string, dynamic> kv in values)
            {
                bool changeAllowed = true;
                if (type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups != null && type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups.Count > 0)
                {
                    changeAllowed = false;
                    foreach (string group in type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups)
                    {
                        if (_accessor.HttpContext.User.IsInRole(group))
                        {
                            changeAllowed = true;
                        }
                    }
                }
                if (changeAllowed)
                {
                    if (kv.Value is string && type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(kv.Value))
                    {
                        Argument a = type.MutationsDict[query.Name].ArgumentsDict[kv.Value];
                        allowedValues.Add(kv.Key, context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name));
                    }
                    else if (kv.Value is string && type.Model.Fields.ContainsKey(kv.Value))
                    {
                        Column a = type.Model.Fields[kv.Value];
                        allowedValues.Add(kv.Key, obj[a.DataName]);
                    }
                    else if (kv.Value is string &&
                        type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                    {
                        Argument a = type.MutationsDict[query.Name].ArgumentsDict[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                        if (IsVariable(kv.Value))
                        {
                            allowedValues.Add(
                                kv.Key,
                                ResolveVariable(
                                    kv.Value,
                                    Regex.Replace(
                                        kv.Value,
                                        "(?<=\\()(.*?)(?=\\))",
                                        (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                    )
                                )
                            );
                        }
                        else
                        {
                            allowedValues.Add(
                                kv.Key,
                                Regex.Replace(
                                    kv.Value,
                                    "(?<=\\()(.*?)(?=\\))",
                                    (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                )
                            );
                        }
                    }
                    else if (kv.Value is string && type.Model.Fields.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                    {
                        Column a = type.Model.Fields[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                        if (IsVariable(kv.Value))
                        {
                            allowedValues.Add(kv.Key, ResolveVariable(kv.Value, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName])));
                        }
                        else
                        {
                            allowedValues.Add(kv.Key, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName]));
                        }
                    }
                    else if (kv.Value is string && IsVariable(kv.Value))
                    {
                        allowedValues.Add(kv.Key, ResolveVariable(kv.Value));
                    }
                    else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTime")
                    {
                        allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                    }
                    else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTimeOffset")
                    {
                        allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                    }
                    else
                    {
                        allowedValues.Add(kv.Key, kv.Value);
                    }
                }
            }
            return allowedValues;
        }
        public Dictionary<string, dynamic> BuildQueryFilter(Types type, List<Argument> arguments, FieldType query, IResolveFieldContext context)
        {
            Dictionary<string, Argument> args = new Dictionary<string, Argument>();
            Dictionary<string, dynamic> filter = new Dictionary<string, dynamic>();
            foreach (Argument arg in arguments)
            {
                args.Add(arg.Name, arg);
                if (!filter.ContainsKey(arg.Name) && arg.Key != "values" && arg.Key != "update" && arg.Key != "manyValues" && arg.Key != "updates" && !filter.ContainsKey(arg.Key))
                {
                    if (arg.Value is string)
                    {
                        if (arg.Value == arg.Name)
                        {
                            if (context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name) != null)
                            {
                                filter.Add(arg.Key, context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name));
                            }
                        }
                        else if (Regex.IsMatch(arg.Value, $"(?<=\\(){arg.Name}(?=\\))"))
                        {
                            if (context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name) != null)
                            {
                                if (IsVariable(arg.Value))
                                {
                                    filter.Add(arg.Key, ResolveVariable(Regex.Replace(arg.Value, $"(?<=\\(){arg.Name}(?=\\))", (string)context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name))));
                                }
                                else
                                {
                                    filter.Add(arg.Key, Regex.Replace(arg.Value, $"(?<=\\(){arg.Name}(?=\\))", (string)context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name)));
                                }
                            }
                        }
                        else if (IsVariable(arg.Value))
                        {
                            filter.Add(arg.Key, ResolveVariable(arg.Value));
                        }
                        else if (arg.Type == "DateTime")
                        {
                            filter.Add(arg.Key, new DateTimeGraphType().ParseValue(arg.Value));
                        }
                        else if (arg.Type == "DateTimeOffset")
                        {
                            filter.Add(arg.Key, new DateTimeOffsetGraphType().ParseValue(arg.Value));
                        }
                        else
                        {
                            filter.Add(arg.Key, arg.Value);
                        }
                    }
                    else
                    {
                        filter.Add(arg.Key, arg.Value);
                    }
                }
            }
            foreach (QueryArgument queryArgument in query.Arguments)
            {
                if (!filter.ContainsKey(queryArgument.Name))
                {
                    if (queryArgument.Name != "values" && queryArgument.Name != "update")
                    {
                        if (!args.ContainsKey(queryArgument.Name))
                        {
                            if (context.GetArgument<dynamic>(
                                    queryArgument.Name
                                ) != null)
                            {
                                filter.Add(
                                    queryArgument.Name,
                                    context.GetArgument<dynamic>(
                                        queryArgument.Name
                                    )
                                );
                            }
                        }
                    }
                }
            }
            return filter;
        }
        public Dictionary<string, dynamic> BuildFilter(Types thisType, string field, Dictionary<string, dynamic> obj)
        {
            Dictionary<string, dynamic> filters = new Dictionary<string, dynamic>();
            foreach (Argument arg in thisType.Model.Fields[field].Arguments)
            {
                if (arg.Value is string && thisType.Model.Fields.ContainsKey(arg.Value) && obj.ContainsKey(thisType.Model.Fields[arg.Value].DataName))
                {
                    if (obj[thisType.Model.Fields[arg.Value].DataName] != null)
                    {
                        filters.Add(arg.Key, obj[thisType.Model.Fields[arg.Value].DataName]);
                    }
                }
                else if (arg.Value is string && thisType.Model.Fields.ContainsKey(arg.Value) && !obj.ContainsKey(thisType.Model.Fields[arg.Value].DataName))
                {
                    switch (arg.Type)
                    {
                        case "List":
                            {
                                filters.Add(arg.Key, new List<dynamic>());
                                break;
                            }
                        case "Object":
                            {
                                filters.Add(arg.Key, new Dictionary<string, dynamic>());
                                break;
                            }
                        default:
                            {
                                filters.Add(arg.Key, "");
                                break;
                            }
                    }
                }
                else
                {
                    if (arg.Value is string)
                    {
                        if (arg.Value == arg.Name)
                        {
                            filters.Add(arg.Key, arg.Value);
                        }
                        else if (Regex.IsMatch(arg.Value, "(?<=\\()(.*?)(?=\\))"))
                        {
                            Dictionary<string, dynamic> arguments = new Dictionary<string, dynamic>();

                            foreach (string c in Regex.Match(arg.Value, "(?<=\\()(.*?)(?=\\))").Value.Split(","))
                            {
                                if (thisType.Model.Fields.ContainsKey(c) && obj[c] != null)
                                {
                                    arguments.Add(c, obj[thisType.Model.Fields[c].DataName]);
                                }
                                else if (thisType.Model.Fields.ContainsKey(c) && obj.Count == 0)
                                {
                                    arguments.Add(c, null);
                                }
                            }
                            filters.Add(arg.Key, ResolveVariable(arg.Value, arguments));
                        }
                        else if (IsVariable(arg.Value))
                        {
                            filters.Add(arg.Key, ResolveVariable(arg.Value, new Dictionary<string, dynamic>()));
                        }
                        else if (arg.Type == "DateTime")
                        {
                            filters.Add(arg.Key, new DateTimeGraphType().ParseValue(arg.Value));
                        }
                        else if (arg.Type == "DateTimeOffset")
                        {
                            filters.Add(arg.Key, new DateTimeOffsetGraphType().ParseValue(arg.Value));
                        }
                        else
                        {
                            filters.Add(arg.Key, arg.Value);
                        }
                    }
                    else
                    {
                        filters.Add(arg.Key, arg.Value);
                    }
                }
            }
            return filters;
        }
        public List<QueryArgument> FilterArgs(Types type, FieldType query, Query thisQuery, ISchema schema)
        {
            //TODO ADD DESCRIPTIONS TO ALL THESE HERE ARGUMENTS
            List<QueryArgument> filterArgs = new List<QueryArgument>();
            foreach (QueryArgument a in query.Arguments)
            {
                if (type.Model.Fields.ContainsKey(a.Name) && !thisQuery.ArgumentsDict.ContainsKey(a.Name) && a.Name != "values" && a.Name != "update")
                {
                    ResolvedType resolvedArgType = _data.ResolveType(a);
                    if (resolvedArgType.Name == "ID")
                    {
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_in", Description = $"Field {a.Name}'s value is in the supplied array" });
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_notIn", Description = $"Field {a.Name}'s value is not in the supplied array" });
                    }
                    else if (resolvedArgType.Name == "String")
                    {
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_startsWith" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_endsWith" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_notStartsWith" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_notEndsWith" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_contains" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_notContains" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_lte" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_lt" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_gte" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_gt" });
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_in" });
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_notIn" });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_not" });
                    }
                    else if (resolvedArgType.Name == "Int")
                    {
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_lte" });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_lt" });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_gte" });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_gt" });
                        filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_in" });
                        filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_notIn" });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_not" });
                    }
                    else if (resolvedArgType.Name == "Float")
                    {
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_lte" });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_lt" });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_gte" });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_gt" });
                        filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_in" });
                        filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_notIn" });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_not" });
                    }
                    else if (resolvedArgType.Name == "Boolean")
                    {
                        filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_not" });
                    }
                    else if (resolvedArgType.Name == "DateTime")
                    {
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_lte" });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_lt" });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_gte" });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_gt" });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_in" });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_notIn" });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_not" });
                    }
                    else if (resolvedArgType.Name == "DateTimeOffset")
                    {
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_lte" });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_lt" });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_gte" });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_gt" });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_in" });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_notIn" });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_not" });
                    }
                    else if (resolvedArgType.TypeStack.Contains("array"))
                    {
                        if (resolvedArgType.Name == "ID")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyEq" });
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyNe" });
                        }
                        else if (resolvedArgType.Name == "String")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyEq" });
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyNe" });
                        }
                        else if (resolvedArgType.Name == "Int")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_anyEq" });
                            filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_anyNe" });
                        }
                        else if (resolvedArgType.Name == "Float")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_anyEq" });
                            filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_anyNe" });
                        }
                        else if (resolvedArgType.Name == "Boolean")
                        {
                            filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_not" });
                        }
                        else if (resolvedArgType.Name == "DateTime")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_anyEq" });
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_anyNe" });
                        }
                        else if (resolvedArgType.Name == "DateTimeOffset")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_anyEq" });
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_anyNe" });
                        }
                    }
                    if (schema.FindType(resolvedArgType.Name) is InputObjectGraphType subType &&
                        type.Type == "mongo" &&
                        resolvedArgType.TypeStack.Contains("array")
                    )
                    {
                        foreach (FieldType sf in subType.Fields)
                        {
                            ResolvedType rt = _data.ResolveType(sf);
                            if (resolvedArgType.Name == "ID")
                            {
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<ListGraphType<IdGraphType>> { Name = $"{a.Name}_{sf.Name}_in" });
                                filterArgs.Add(new QueryArgument<ListGraphType<IdGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn" });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_last" });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot" });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_first" });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot" });
                                filterArgs.Add(new QueryArgument<IndexIdType> { Name = $"{a.Name}_{sf.Name}_atIndex" });
                                filterArgs.Add(new QueryArgument<IndexIdType> { Name = $"{a.Name}_{sf.Name}_atIndexNot" });
                            }
                            else if (rt.Name == "String")
                            {
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_startsWith" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_endsWith" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_notStartsWith" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_notEndsWith" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_contains" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_notContains" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_lte" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_lt" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_gte" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_gt" });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_in" });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_not" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_last" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_first" });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot" });
                                filterArgs.Add(new QueryArgument<IndexStringType> { Name = $"{a.Name}_{sf.Name}_atIndex" });
                                filterArgs.Add(new QueryArgument<IndexStringType> { Name = $"{a.Name}_{sf.Name}_atIndexNot" });
                            }
                            else if (rt.Name == "Int")
                            {
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_lte" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_lt" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_gte" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_gt" });
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_in" });
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_not" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_last" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_first" });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot" });
                                filterArgs.Add(new QueryArgument<IndexIntType> { Name = $"{a.Name}_{sf.Name}_atIndex" });
                                filterArgs.Add(new QueryArgument<IndexIntType> { Name = $"{a.Name}_{sf.Name}_atIndexNot" });
                            }
                            else if (rt.Name == "Float")
                            {
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_lte" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_lt" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_gte" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_gt" });
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_in" });
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_not" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_last" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_first" });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot" });
                                filterArgs.Add(new QueryArgument<IndexFloatType> { Name = $"{a.Name}_{sf.Name}_atIndex" });
                                filterArgs.Add(new QueryArgument<IndexFloatType> { Name = $"{a.Name}_{sf.Name}_atIndexNot" });
                            }
                            else if (rt.Name == "Boolean")
                            {
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_not" });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_last" });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot" });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_first" });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot" });
                                filterArgs.Add(new QueryArgument<IndexBooleanType> { Name = $"{a.Name}_{sf.Name}_atIndex" });
                                filterArgs.Add(new QueryArgument<IndexBooleanType> { Name = $"{a.Name}_{sf.Name}_atIndexNot" });
                            }
                            else if (rt.Name == "DateTime")
                            {
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_lte" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_lt" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_gte" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_gt" });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_in" });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_not" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_last" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_first" });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot" });
                                filterArgs.Add(new QueryArgument<IndexDateTimeType> { Name = $"{a.Name}_{sf.Name}_atIndex" });
                                filterArgs.Add(new QueryArgument<IndexDateTimeType> { Name = $"{a.Name}_{sf.Name}_atIndexNot" });
                            }
                            else if (rt.Name == "DateTimeOffset")
                            {
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_lte" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_lt" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_gte" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_gt" });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_in" });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_not" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_last" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_first" });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot" });
                                filterArgs.Add(new QueryArgument<IndexDateTimeOffsetType> { Name = $"{a.Name}_{sf.Name}_atIndex" });
                                filterArgs.Add(new QueryArgument<IndexDateTimeOffsetType> { Name = $"{a.Name}_{sf.Name}_atIndexNot" });
                            }
                            if (resolvedArgType.Name == "ID")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq" });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe" });
                            }
                            else if (resolvedArgType.Name == "String")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq" });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe" });
                            }
                            else if (resolvedArgType.Name == "Int")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq" });
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe" });
                            }
                            else if (resolvedArgType.Name == "Float")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq" });
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe" });
                            }
                            else if (resolvedArgType.Name == "Boolean")
                            {
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_not" });
                            }
                            else if (resolvedArgType.Name == "DateTime")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq" });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe" });
                            }
                            else if (resolvedArgType.Name == "DateTimeOffset")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq" });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe" });
                            }
                        }
                    }
                }
            }
            if (!(thisQuery.Arguments.FindAll(k => k.Key == "_limit").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_start").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_orderBy").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_orderBy_desc").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_upsert").Count > 0))
            {
                filterArgs.Add(new QueryArgument<IntGraphType> { Name = "_limit" });
                filterArgs.Add(new QueryArgument<IntGraphType> { Name = "_start" });
                filterArgs.Add(new QueryArgument<StringGraphType> { Name = "_orderBy" });
                filterArgs.Add(new QueryArgument<StringGraphType> { Name = "_orderBy_desc" });
                if (type.Type == "mongo")
                {
                    filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = "_upsert" });
                }
            }
            return filterArgs;
        }
        public List<QueryArgument> FilterSubscriptionArgs(Types type, FieldType query, Query thisQuery, ISchema schema)
        {
            //TODO ADD DESCRIPTIONS TO ALL THESE HERE ARGUMENTS
            List<QueryArgument> filterArgs = new List<QueryArgument>();
            foreach (QueryArgument a in query.Arguments)
            {
                if (type.Model.Fields.ContainsKey(a.Name) && !thisQuery.ArgumentsDict.ContainsKey(a.Name) && a.Name != "values" && a.Name != "update")
                {
                    ResolvedType resolvedArgType = _data.ResolveType(a);
                    if (resolvedArgType.Name == "ID")
                    {
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_in", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() }, Description = $"Field {a.Name}'s value is in the supplied array" });
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_notIn", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() }, Description = $"Field {a.Name}'s value is not in the supplied array" });
                    }
                    else if (resolvedArgType.Name == "String")
                    {
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_startsWith", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_endsWith", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_notStartsWith", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_notEndsWith", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_contains", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_notContains", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_lte", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_lt", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_gte", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_gt", ResolvedType = new StringGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_in", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_notIn", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_not", ResolvedType = new StringGraphType().GetNamedType() });
                    }
                    else if (resolvedArgType.Name == "Int")
                    {
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_lte", ResolvedType = new IntGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_lt", ResolvedType = new IntGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_gte", ResolvedType = new IntGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_gt", ResolvedType = new IntGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_in", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_notIn", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_not", ResolvedType = new IntGraphType().GetNamedType() });
                    }
                    else if (resolvedArgType.Name == "Float")
                    {
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_lte", ResolvedType = new FloatGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_lt", ResolvedType = new FloatGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_gte", ResolvedType = new FloatGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_gt", ResolvedType = new FloatGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_in", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_notIn", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_not", ResolvedType = new FloatGraphType().GetNamedType() });
                    }
                    else if (resolvedArgType.Name == "Boolean")
                    {
                        filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_not", ResolvedType = new BooleanGraphType().GetNamedType() });
                    }
                    else if (resolvedArgType.Name == "DateTime")
                    {
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_lte", ResolvedType = new DateTimeGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_lt", ResolvedType = new DateTimeGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_gte", ResolvedType = new DateTimeGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_gt", ResolvedType = new DateTimeGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_in", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_notIn", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_not", ResolvedType = new DateTimeGraphType().GetNamedType() });
                    }
                    else if (resolvedArgType.Name == "DateTimeOffset")
                    {
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_lte", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_lt", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_gte", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_gt", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_in", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_notIn", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                        filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_not", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                    }
                    else if (resolvedArgType.TypeStack.Contains("array"))
                    {
                        if (resolvedArgType.Name == "ID")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyEq", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyNe", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                        }
                        else if (resolvedArgType.Name == "String")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyEq", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                            filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_anyNe", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                        }
                        else if (resolvedArgType.Name == "Int")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_anyEq", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                            filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_anyNe", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                        }
                        else if (resolvedArgType.Name == "Float")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_anyEq", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                            filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_anyNe", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                        }
                        else if (resolvedArgType.Name == "Boolean")
                        {
                            filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_not", ResolvedType = new BooleanGraphType().GetNamedType() });
                        }
                        else if (resolvedArgType.Name == "DateTime")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_anyEq", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_anyNe", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                        }
                        else if (resolvedArgType.Name == "DateTimeOffset")
                        {
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_anyEq", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                            filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_anyNe", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                        }
                    }
                    if (schema.FindType(resolvedArgType.Name) is InputObjectGraphType subType &&
                        type.Type == "mongo" &&
                        resolvedArgType.TypeStack.Contains("array")
                    )
                    {
                        foreach (FieldType sf in subType.Fields)
                        {
                            ResolvedType rt = _data.ResolveType(sf);
                            if (resolvedArgType.Name == "ID")
                            {
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}", ResolvedType = new IdGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<ListGraphType<IdGraphType>> { Name = $"{a.Name}_{sf.Name}_in", ResolvedType = new ListGraphType<IdGraphType> { ResolvedType = new IdGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<IdGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn", ResolvedType = new ListGraphType<IdGraphType> { ResolvedType = new IdGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_last", ResolvedType = new IdGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot", ResolvedType = new IdGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_first", ResolvedType = new IdGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IdGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot", ResolvedType = new IdGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexIdType> { Name = $"{a.Name}_{sf.Name}_atIndex", ResolvedType = FindType("IndexIdInput").GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexIdType> { Name = $"{a.Name}_{sf.Name}_atIndexNot", ResolvedType = FindType("IndexIdInput").GetNamedType() });
                            }
                            else if (rt.Name == "String")
                            {
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_startsWith", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_endsWith", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_notStartsWith", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_notEndsWith", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_contains", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_notContains", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_lte", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_lt", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_gte", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_gt", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_in", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_not", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_last", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_first", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<StringGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot", ResolvedType = new StringGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexStringType> { Name = $"{a.Name}_{sf.Name}_atIndex", ResolvedType = FindType("IndexStringInput").GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexStringType> { Name = $"{a.Name}_{sf.Name}_atIndexNot", ResolvedType = FindType("IndexStringInput").GetNamedType() });
                            }
                            else if (rt.Name == "Int")
                            {
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_lte", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_lt", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_gte", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_gt", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_in", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_not", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_last", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_first", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IntGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot", ResolvedType = new IntGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexIntType> { Name = $"{a.Name}_{sf.Name}_atIndex", ResolvedType = FindType("IndexIntInput").GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexIntType> { Name = $"{a.Name}_{sf.Name}_atIndexNot", ResolvedType = FindType("IndexIntInput").GetNamedType() });
                            }
                            else if (rt.Name == "Float")
                            {
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_lte", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_lt", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_gte", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_gt", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_in", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_not", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_last", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_first", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<FloatGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot", ResolvedType = new FloatGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexFloatType> { Name = $"{a.Name}_{sf.Name}_atIndex", ResolvedType = FindType("IndexFloatInput").GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexFloatType> { Name = $"{a.Name}_{sf.Name}_atIndexNot", ResolvedType = FindType("IndexFloatInput").GetNamedType() });
                            }
                            else if (rt.Name == "Boolean")
                            {
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}" });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_not", ResolvedType = new BooleanGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_last", ResolvedType = new BooleanGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot", ResolvedType = new BooleanGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_first", ResolvedType = new BooleanGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot", ResolvedType = new BooleanGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexBooleanType> { Name = $"{a.Name}_{sf.Name}_atIndex", ResolvedType = FindType("IndexBooleanInput").GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexBooleanType> { Name = $"{a.Name}_{sf.Name}_atIndexNot", ResolvedType = FindType("IndexBooleanInput").GetNamedType() });
                            }
                            else if (rt.Name == "DateTime")
                            {
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_lte", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_lt", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_gte", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_gt", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_in", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_not", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_last", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_first", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot", ResolvedType = new DateTimeGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexDateTimeType> { Name = $"{a.Name}_{sf.Name}_atIndex", ResolvedType = FindType("IndexDateTimeInput").GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexDateTimeType> { Name = $"{a.Name}_{sf.Name}_atIndexNot", ResolvedType = FindType("IndexDateTimeInput").GetNamedType() });
                            }
                            else if (rt.Name == "DateTimeOffset")
                            {
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_lte", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_lt", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_gte", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_gt", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_in", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_notIn", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_not", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_last", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_lastNot", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_first", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<DateTimeOffsetGraphType> { Name = $"{a.Name}_{sf.Name}_firstNot", ResolvedType = new DateTimeOffsetGraphType().GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexDateTimeOffsetType> { Name = $"{a.Name}_{sf.Name}_atIndex", ResolvedType = FindType("IndexDateTimeOffsetInput").GetNamedType() });
                                filterArgs.Add(new QueryArgument<IndexDateTimeOffsetType> { Name = $"{a.Name}_{sf.Name}_atIndexNot", ResolvedType = FindType("IndexDateTimeOffsetInput").GetNamedType() });
                            }
                            if (resolvedArgType.Name == "ID")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                            }
                            else if (resolvedArgType.Name == "String")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<StringGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe", ResolvedType = new ListGraphType<StringGraphType> { ResolvedType = new StringGraphType().GetNamedType() } });
                            }
                            else if (resolvedArgType.Name == "Int")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<IntGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe", ResolvedType = new ListGraphType<IntGraphType> { ResolvedType = new IntGraphType().GetNamedType() } });
                            }
                            else if (resolvedArgType.Name == "Float")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<FloatGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe", ResolvedType = new ListGraphType<FloatGraphType> { ResolvedType = new FloatGraphType().GetNamedType() } });
                            }
                            else if (resolvedArgType.Name == "Boolean")
                            {
                                filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = $"{a.Name}_{sf.Name}_not", ResolvedType = new BooleanGraphType().GetNamedType() });
                            }
                            else if (resolvedArgType.Name == "DateTime")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe", ResolvedType = new ListGraphType<DateTimeGraphType> { ResolvedType = new DateTimeGraphType().GetNamedType() } });
                            }
                            else if (resolvedArgType.Name == "DateTimeOffset")
                            {
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_anyEq", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                                filterArgs.Add(new QueryArgument<ListGraphType<DateTimeOffsetGraphType>> { Name = $"{a.Name}_{sf.Name}_anyNe", ResolvedType = new ListGraphType<DateTimeOffsetGraphType> { ResolvedType = new DateTimeOffsetGraphType().GetNamedType() } });
                            }
                        }
                    }
                }
            }
            if (type.SubscriptionsDict.ContainsKey(thisQuery.Name))
            {
                return filterArgs;
            }
            if (!(thisQuery.Arguments.FindAll(k => k.Key == "_limit").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_start").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_orderBy").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_orderBy_desc").Count > 0
                || thisQuery.Arguments.FindAll(k => k.Key == "_upsert").Count > 0))
            {
                filterArgs.Add(new QueryArgument<IntGraphType> { Name = "_limit" });
                filterArgs.Add(new QueryArgument<IntGraphType> { Name = "_start" });
                filterArgs.Add(new QueryArgument<StringGraphType> { Name = "_orderBy" });
                filterArgs.Add(new QueryArgument<StringGraphType> { Name = "_orderBy_desc" });
                if (type.Type == "mongo")
                {
                    filterArgs.Add(new QueryArgument<BooleanGraphType> { Name = "_upsert" });
                }
            }
            return filterArgs;
        }

    }
}
