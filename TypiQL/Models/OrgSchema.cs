using DataCrush.TypiQL.Models.AD;
using DataCrush.TypiQL.Models.Mongo;
using DataCrush.TypiQL.Models.Sql;
using GraphQL;
using GraphQL.DataLoader;
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
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TypiQL.Models;

namespace DataCrush.TypiQL.Models
{
    public class BaseSchema : Schema
    {
        public BaseSchema(IServiceProvider provider, Queries queries, Mutations mutations, Subscriptions subscriptions) : base(provider) 
        {
            Query = queries;
            Mutation = mutations;
            Subscription = subscriptions;
        }
    }
    public class OrgSchema : Schema
    {
        private readonly ConfigData _data;
        private readonly IHttpContextAccessor _httpContext;
        private List<Types> _types;
        private readonly TypiQLSettings _settings;
        private readonly TypiQLMongoContext _mongoContext;
        private readonly SchemaHelpers _helpers;

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

        public OrgSchema(
            IServiceProvider provider,
            IHttpContextAccessor accessor,            
            ConfigData data,
            TypiQLSettings settings
            ) : base(provider)
        {
            _httpContext = accessor;
            _data = data;
            _settings = settings;
            _mongoContext = new TypiQLMongoContext(settings);
            _helpers = provider.GetRequiredService<SchemaHelpers>();
            _helpers.Configure(provider.GetRequiredService<MongoData>(), provider.GetRequiredService<SqlData>(), provider.GetRequiredService<ADData>());
            
            foreach (CustomResolver cr in _settings.Resolvers)
            {
                cr.GetFieldResolver();
            }

            Query = new Queries(settings, data, accessor, provider);
            Mutation = new Mutations(settings, data, accessor);
            Subscription = new Subscriptions(settings, data, _mongoContext);

            GenerateSchema();
        }
        public void ReloadTypeDict()
        {
            _types = _data.GetTypes().Result;
        }
        public dynamic Log(Query query, IResolveFieldContext context, dynamic result)
        {
            if (query.Log)
            {
                LoggingContext log = new LoggingContext
                {
                    DateTime = DateTime.UtcNow,
                    Details = new Dictionary<string, dynamic>
                        {
                            { "user", _httpContext.HttpContext.User.Identity.Name },
                            { "operation", query.Type },
                            { "type", context.ReturnType.Name },
                            { context.ParentType.Name, context.FieldName },
                            { "arguments", context.Arguments },
                            { "result", result }
                        }
                };
                if (_settings.Logger != null)
                {
                    _settings.Logger.Invoke(log);
                }
                else
                {
                    LoggingContext _ = _data.AddLog(log).Result;
                }

            }
            return result;
        }
        public dynamic Log(Column query, IResolveFieldContext context, dynamic result)
        {
            if (query.Log)
            {
                LoggingContext log = new LoggingContext
                {
                    DateTime = DateTime.UtcNow,
                    Details = new Dictionary<string, dynamic>
                        {
                            { "user", _httpContext.HttpContext.User.Identity.Name },
                            { "operation", query.ColumnType },
                            { "type", context.ReturnType.Name },
                            { context.ParentType.Name, context.FieldName },
                            { "arguments", context.Arguments },
                            { "result", result }
                        }
                };
                if (_settings.Logger != null)
                {
                    _settings.Logger.Invoke(log);
                }                    
                else
                {
                    LoggingContext _ = _data.AddLog(log).Result;
                }
                    
            }
            return result;
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
                        field.Resolver = new AsyncFieldResolver<dynamic>(async context =>
                        {
                            if (!Allowed(type.Name, field.Name, inSecureByDefault: true))
                            {
                                Log(thisColumn, context, "access denied");
                                return new UnauthorizedAccessException();
                            }
                            if (!(context.Source is IDictionary<string, dynamic> obj))
                            {
                                Log(thisColumn, context, "parent invalid");
                                return null;
                            }
                            obj = new Dictionary<string, dynamic>(obj);
                            if (resolvedTypeInfo.TypeStack.Contains("array")
                                && _typeDict.ContainsKey(resolvedTypeInfo.Name)
                                && thisColumn.Arguments.Count > 0)
                            {
                                var loader = _helpers.BatchMany(
                                    _typeDict[resolvedTypeInfo.Name],
                                    $"Get{resolvedTypeInfo.Name}By{thisType.Name}{string.Join("-", thisColumn.Arguments.Select(a => a.Key).ToArray())}",
                                    thisType,
                                    field.Name
                                    );

                                var json = JsonConvert.SerializeObject(_helpers.BuildFilter(thisType, field.Name, obj as Dictionary<string, dynamic>));
                                return Log(thisColumn, context, loader.LoadAsync(json));
                            }
                            else if (!resolvedTypeInfo.TypeStack.Contains("array")
                                && _typeDict.ContainsKey(resolvedTypeInfo.Name)
                                && thisColumn.Arguments.Count > 0)
                            {
                                var loader = _helpers.BatchOne(
                                    _typeDict[resolvedTypeInfo.Name],
                                    $"Get{resolvedTypeInfo.Name}By{thisType.Name}{string.Join("-", thisColumn.Arguments.Select(a => a.Key).ToArray())}",
                                    thisType,
                                    field.Name
                                    );
                                var json = JsonConvert.SerializeObject(_helpers.BuildFilter(thisType, field.Name, obj as Dictionary<string, dynamic>));
                                return Log(thisColumn, context, loader.LoadAsync(json));
                            }
                            else if (!obj.ContainsKey(thisColumn.DataName))
                            {
                                Log(thisColumn, context, $"{thisColumn.DataName} not found in parent");
                                return null;
                            }
                            else if (thisColumn.ColumnType == "File")
                            {
                                Log(thisColumn, context, $"file: {obj[thisColumn.DataName]}");
                                return _helpers.GetFile(thisType, obj[thisColumn.DataName]);
                            }
                            else if (thisColumn.ColumnType == "Json")
                            {
                                return Log(thisColumn, context, BsonTypeMapper.MapToDotNetValue(BsonDocument.Parse(obj[thisColumn.DataName])));
                            }
                            else
                            {
                                return Log(thisColumn, context, obj[thisColumn.DataName]);
                            }
                        });

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
                                    if (!Allowed(thisType.Name, sub.Name, "subscription", true))
                                    {
                                        Log(sub, context, "access denied");
                                        throw new UnauthorizedAccessException();
                                    }
                                    var result = context.Source;
                                    Subscriber<Dictionary<string, dynamic>> dict = result as Subscriber<Dictionary<string, dynamic>>;
                                    Subscriber<dynamic> dyn = new Subscriber<dynamic>
                                    {
                                        OperationName = dict.OperationName,
                                        Value = dict.Value
                                    };
                                    return Log(sub, context, dyn);
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
                                Dictionary<string, dynamic> filter = _helpers.BuildQueryFilter(thisType, sub.Arguments, subscription, context);
                                return await _data.Subscription(thisType, filter);
                            });
                            Subscription.AddField(subscription);

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
                        if (!Allowed(query.ResolvedType.GetNamedType().Name, query.Name, "query", true))
                        {
                            Log(thisQuery, context, "Access Denied");
                            throw new UnauthorizedAccessException();
                        }
                        Dictionary<string, dynamic> filter = _helpers.BuildQueryFilter(thisType, thisQuery.Arguments, query, context);
                        if (thisQuery.Type == "List")
                        {
                            return Log(thisQuery, context, _helpers.GetMany(context, thisType, filter));
                        }
                        else if (thisQuery.Type == "Get")
                        {
                            return Log(thisQuery, context, _helpers.GetOne(context, thisType, filter));
                        }
                        else
                        {
                            Log(thisQuery, context, "invalid query type");
                            return null;
                        }
                    });
                }
                Query.AddField(query);

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
                        if (!Allowed(mutation.ResolvedType.GetNamedType().Name, mutation.Name, "mutation", true))
                        {
                            Log(thisQuery, context, "Access Denied");
                            throw new UnauthorizedAccessException();
                        }
                        Dictionary<string, dynamic> filter = _helpers.BuildQueryFilter(thisType, thisQuery.Arguments, mutation, context);
                        Dictionary<string, dynamic> values = _helpers.GetValues(thisType, thisQuery.Arguments, mutation, context, filter);
                        List<Dictionary<string, dynamic>> manyValues = _helpers.GetManyValues(thisType, thisQuery.Arguments, mutation, context, filter);
                        if (thisQuery.Type == "Add")
                        {
                            return Log(thisQuery, context, _helpers.AddOne(context, thisType, values));
                        }
                        else if (thisQuery.Type == "Update")
                        {
                            return Log(thisQuery, context, _helpers.UpdateOne(context, thisType, filter, values));
                        }
                        else if (thisQuery.Type == "Remove")
                        {
                            return Log(thisQuery, context, _helpers.RemoveOne(context, thisType, filter));
                        }
                        else if (thisQuery.Type == "RemoveMany")
                        {
                            return Log(thisQuery, context, _helpers.RemoveMany(context, thisType, filter));
                        }
                        else if (thisQuery.Type == "AddMany")
                        {
                            return Log(thisQuery, context, _helpers.AddMany(context, thisType, manyValues));
                        }
                        else if (thisQuery.Type == "UpdateMany")
                        {
                            return Log(thisQuery, context, _helpers.UpdateMany(context, thisType, filter, values));
                        }
                        else
                        {
                            Log(thisQuery, context, "invalid mutation type");
                            return null;
                        }
                    });
                }
                Mutation.AddField(mutation);
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
        public bool Allowed(string type, string field, string location = "field", bool inSecureByDefault = false)
        {
            bool allowed = inSecureByDefault;
            Types securityType = _typeDict[type];
            List<string> allowedGroups;
            switch (location)
            {
                case "field": { allowedGroups = securityType.Model.Fields[field].AllowedGroups; break; }
                case "query": { allowedGroups = securityType.QueriesDict[field].AllowedGroups; break; }
                case "mutation": { allowedGroups = securityType.MutationsDict[field].AllowedGroups; break; }
                case "subscription": { allowedGroups = securityType.SubscriptionsDict[field].AllowedGroups; break; }
                default: { allowedGroups = securityType.Model.Fields[field].AllowedGroups; break; }
            }
            if (allowedGroups != null && allowedGroups.Count > 0)
            {
                allowed = false;
                foreach (string group in allowedGroups)
                {
                    if (_httpContext.HttpContext.User.IsInRole(_settings.RolesDict[group].Value))
                    {
                        allowed = true;
                    }
                }
            }
            return allowed;
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
