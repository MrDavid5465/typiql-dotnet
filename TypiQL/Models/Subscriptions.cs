using DataCrush.TypiQL.Models.Mongo;
using GraphQL;
using GraphQL.Resolvers;
using GraphQL.Server.Authorization.AspNetCore;
using GraphQL.Subscription;
using GraphQL.Types;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace DataCrush.TypiQL.Models
{
    public class Subscriptions : ObjectGraphType
    {
        private readonly ConfigData _data;
        private TypiQLMongoContext _context;
        private TypiQLSettings _settings;
        public Subscriptions(TypiQLSettings settings, ConfigData data, TypiQLMongoContext context)
        {
            _data = data;
            _context = context;
            _settings = settings;

            Name = "Subscription";
            AddField(new EventStreamFieldType
            {
                Name = "typesGetAll",
                Type = typeof(ListGraphType<TypesType>),
                Resolver = new FuncFieldResolver<List<Types>>(context => context.Source as List<Types>),
                Subscriber = new EventStreamResolver<List<Types>>(SubscribeToAllTypes)
            }).AuthorizeWith(settings.AdminRole);
            AddField(new EventStreamFieldType
            {
                Name = "typeByName",
                Arguments = new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "name" }
                ),
                Type = typeof(TypesType),
                Resolver = new FuncFieldResolver<Types>(GetTypesType),
                Subscriber = new EventStreamResolver<Types>(SubscribeToType)
            }).AuthorizeWith(settings.AdminRole);
            AddField(new EventStreamFieldType
            {
                Name = "typeAdded",
                Type = typeof(TypesType),
                Resolver = new FuncFieldResolver<Types>(GetTypesType),
                Subscriber = new EventStreamResolver<Types>(Subscribe)
            }).AuthorizeWith(settings.AdminRole);
        }
        private IObservable<List<Types>> SubscribeToAllTypes(IResolveEventStreamContext context)
        {
            return _data._typesRepo.TypesGetAll();
        }
        private IObservable<Types> SubscribeToType(IResolveEventStreamContext context)
        {
            string name = context.GetArgument<string>("name");
            return _data._typesRepo.Types().Where(t => t.Name == name);
        }
        private Types GetTypesType(IResolveFieldContext context)
        {
            return context.Source as Types;
        }
        private IObservable<Types> Subscribe(IResolveEventStreamContext context)
        {
            return _data._typesRepo.Types();
        }
    }
    public class Subscriber<T>
    {
        public string OperationName { get; set; }
        public T Value { get; set; }
    }
    public class SubscriberType<TSourceType> : ObjectGraphType<Subscriber<dynamic>> where TSourceType : IObjectGraphType, new()
    {
        public SubscriberType()
        {
            Name = $"{new TSourceType().Name}Subscriber";
            Field<StringGraphType>("operationName", resolve: context => context.Source.OperationName);
            AddField(new FieldType
            {
                Name = "value",
                Type = new TSourceType().GetType(),
                ResolvedType = new TSourceType().GetNamedType(),
                Resolver = new FuncFieldResolver<Subscriber<dynamic>, dynamic>(
                    context =>
                    {
                        return context.Source.Value;
                    })
            });
        }
        public SubscriberType(IObjectGraphType type)
        {
            Name = $"{type.Name}Subscriber";
            Field<StringGraphType>("operationName", resolve: context => context.Source.OperationName);
            AddField(new FieldType
            {
                Name = "value",
                Type = type.GetType(),
                ResolvedType = type.GetNamedType(),
                Resolver = new FuncFieldResolver<Subscriber<dynamic>, dynamic>(
                    context =>
                    {
                        return context.Source.Value;
                    })
            });
        }
    }
    public interface ISubscriberRepo<T>
    {
        Task<IObservable<Subscriber<T>>> SubscribeToType(Types t);
        Task<IObservable<Subscriber<Dictionary<string, dynamic>>>> Subscription(Types t, Dictionary<string, dynamic> filter);
        T ChangeEntity(Types t, string operationName, T entity);
    }
    public class SubscriberRepo<T> : ISubscriberRepo<T>
    {
        private ISubject<Subscriber<T>> _entityStream = new Subject<Subscriber<T>>();
        private ISubject<Subscriber<Dictionary<string, dynamic>>> _dictionaryStream = new Subject<Subscriber<Dictionary<string,dynamic>>>();
        public SubscriberRepo()
        {
            
        }
        public T ChangeEntity(Types t, string operationName, T entity)
        {
            _entityStream.OnNext(new Subscriber<T> { OperationName = operationName, Value = entity });
            if (entity is List<dynamic>)
            {
                foreach (Dictionary<string, dynamic> values in entity as List<dynamic>)
                {
                    foreach (Column c in t.Model.Columns)
                    {
                        if (!values.ContainsKey(c.DataName))
                        {
                            values.Add(c.DataName, null);
                        }
                    }
                    _dictionaryStream.OnNext(new Subscriber<Dictionary<string, dynamic>> { OperationName = operationName, Value = values });
                }
                
            } else
            {
                var values = entity as Dictionary<string, dynamic>;
                foreach (Column c in t.Model.Columns)
                {
                    if (!values.ContainsKey(c.DataName))
                    {
                        values.Add(c.DataName, null);
                    }
                }
                _dictionaryStream.OnNext(new Subscriber<Dictionary<string, dynamic>> { OperationName = operationName, Value = values });
            }
            
   
            return entity;
        }
        public async Task<IObservable<Subscriber<Dictionary<string, dynamic>>>> Subscription(Types t, Dictionary<string,dynamic> keys)
        {            
            return _dictionaryStream.Where(s => {
                List<string> filters = new List<string>();
                List<string> sort = new List<string>();
                List<bool> result = new List<bool>();
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
                            sort.Add(field.Trim());
                        }
                    }
                    else if (key.Key.Split("_")[0] == "operationName")
                    {
                        if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "not")
                        {
                            result.Add(s.OperationName != value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "in")
                        {
                            result.Add(((List<dynamic>)value).Contains(s.OperationName));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notIn")
                        {
                            result.Add(!((List<dynamic>)value).Contains(s.OperationName));
                        }
                        else
                        {
                            result.Add(s.OperationName == value);
                        }
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "startsWith")
                    {
                        result.Add(((string)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).StartsWith(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "endsWith")
                    {
                        result.Add(((string)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).EndsWith(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notStartsWith")
                    {
                        result.Add(!((string)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).StartsWith(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notEndsWith")
                    {
                        result.Add(!((string)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).EndsWith(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "contains")
                    {
                        result.Add(((string)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).Contains(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notContains")
                    {
                        result.Add(!((string)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).Contains(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lte")
                    {
                        result.Add(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName] <= value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lt")
                    {
                        result.Add(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName] < value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gte")
                    {
                        result.Add(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName] >= value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gt")
                    {
                        result.Add(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName] > value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "in")
                    {
                        result.Add(((List<dynamic>)value).Contains(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notIn")
                    {
                        result.Add(!((List<dynamic>)value).Contains(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "anyEq")
                    {
                        result.Add(((List<dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).Contains(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "anyNe")
                    {
                        result.Add(!((List<dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).Contains(value));
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "not")
                    {
                        result.Add(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName] != value);
                    }
                    else if (key.Key.Split("_").Length > 1 && key.Key.Split("_").Length == 3)
                    {
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
                            result.Add(((string)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).StartsWith(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "endsWith")
                        {
                            result.Add(((string)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).EndsWith(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notStartsWith")
                        {
                            result.Add(!((string)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).StartsWith(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notEndsWith")
                        {
                            result.Add(!((string)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).EndsWith(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "contains")
                        {
                            result.Add(((string)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).Contains(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notContains")
                        {
                            result.Add(!((string)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).Contains(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "lte")
                        {
                            result.Add(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName] <= value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "lt")
                        {
                            result.Add(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName] < value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "gte")
                        {
                            result.Add(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName] >= value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "gt")
                        {
                            result.Add(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName] > value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "in")
                        {
                            result.Add(((List<dynamic>)value).Contains(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "notIn")
                        {
                            result.Add(!((List<dynamic>)value).Contains(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "not")
                        {
                            result.Add(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName] != value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "anyEq")
                        {
                            result.Add(((List<dynamic>)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).Contains(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "anyNe")
                        {
                            result.Add(!((List<dynamic>)((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName]).Contains(value));
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "last")
                        {
                            result.Add(((List<Dictionary<string, dynamic>>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).Last()[t.Model.Fields[key.Key.Split("_")[1]].DataName] == value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "lastNot")
                        {
                            result.Add(((List<Dictionary<string, dynamic>>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).Last()[t.Model.Fields[key.Key.Split("_")[1]].DataName] != value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "first")
                        {
                            result.Add(((List<Dictionary<string, dynamic>>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).First()[t.Model.Fields[key.Key.Split("_")[1]].DataName] == value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "firstNot")
                        {
                            result.Add(((List<Dictionary<string, dynamic>>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName]).Last()[t.Model.Fields[key.Key.Split("_")[1]].DataName] != value);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "atIndex")
                        {
                            result.Add(((List<Dictionary<string, dynamic>>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[value["index"]][t.Model.Fields[key.Key.Split("_")[1]].DataName] == value["value"]);
                        }
                        else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[2] == "atIndexNot")
                        {
                            result.Add(((List<Dictionary<string, dynamic>>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[value["index"]][t.Model.Fields[key.Key.Split("_")[1]].DataName] != value["value"]);
                        }
                        else
                        {
                            result.Add(((Dictionary<string, dynamic>)s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName])[t.Model.Fields[key.Key.Split("_")[1]].DataName] == value);
                        }
                    }
                    else if (value != null)
                    {
                        result.Add(s.Value[t.Model.Fields[key.Key.Split("_")[0]].DataName] == value);
                    }

                }
                if (result.Contains(false))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }).AsObservable();
        }
        public async Task<IObservable<Subscriber<T>>> SubscribeToType(Types t)
        {
            return _entityStream.AsObservable();
        }
    }
}
