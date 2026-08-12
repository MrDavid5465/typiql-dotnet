using GraphQL.DataLoader;
using GraphQL.Types;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace DataCrush.TypiQL.Models
{
    public class Types
    {
        [BsonId]
        public ObjectId Id { get; set; }
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("type")]
        public string Type { get; set; }
        [BsonElement("schema")]
        public string Schema { get; set; }
        [BsonElement("inputTypes")]
        public List<InputType> InputTypes { get; set; }
        public Dictionary<string, string> InputTypesDict
        {
            get
            {
                Dictionary<string, string> types = new Dictionary<string, string>();
                if (InputTypes != null)
                {
                    foreach (InputType type in InputTypes)
                    {
                        types.Add(type.Name, type.Description);
                    }
                }                
                return types;
            }
        }
        [BsonElement("queries")]
        public List<Query> Queries { get; set; }
        public Dictionary<string, Query> QueriesDict
        {
            get
            {
                Dictionary<string, Query> _queries = new Dictionary<string, Query>();
                if (Queries != null) 
                { 
                    foreach (Query q in Queries)
                    {
                        _queries.Add(q.Name, q);
                    }
                }
                return _queries;
            }
        }
        public List<string> QueriesSchema {
            get
            {
                List<string> _queries = new List<string>();
                if (Queries != null)
                {
                    foreach (Query q in Queries)
                    {
                        _queries.Add(q.Schema);
                    }
                }
                
                return _queries;
            }
        }
        [BsonElement("mutations")]
        public List<Query> Mutations { get; set; }
        public Dictionary<string, Query> MutationsDict
        {
            get
            {
                Dictionary<string, Query> _queries = new Dictionary<string, Query>();
                if (Mutations != null)
                {
                    foreach (Query q in Mutations)
                    {
                        _queries.Add(q.Name, q);
                    }
                }
                    
                return _queries;
            }
        }
        public List<string> MutationsSchema
        {
            get
            {
                List<string> _queries = new List<string>();
                if (Mutations != null)
                {
                    foreach (Query q in Mutations)
                    {
                        _queries.Add(q.Schema);
                    }
                }
                return _queries;
            }
        }
        [BsonElement("subscriptions")]
        public List<Query> Subscriptions { get; set; }
        public Dictionary<string, Query> SubscriptionsDict
        {
            get
            {
                Dictionary<string, Query> _queries = new Dictionary<string, Query>();
                if (Subscriptions != null)
                {
                    foreach (Query q in Subscriptions)
                    {
                        _queries.Add(q.Name, q);
                    }
                }

                return _queries;
            }
        }
        public List<string> SubscriptionsSchema
        {
            get
            {
                List<string> _queries = new List<string>();
                if (Subscriptions != null)
                {
                    foreach (Query q in Subscriptions)
                    {
                        _queries.Add(q.Schema);
                    }
                }
                return _queries;
            }
        }
        [BsonElement("model")]
        public Model Model { get; set; }
        [BsonElement("connection")]
        public string Connection { get; set; }
    }
    public class TypesType : ObjectGraphType<Types>
    {
        public TypesType (ConfigData data, IDataLoaderContextAccessor dataLoader)
        {
            Name = "Types";
            Field<IdGraphType>("id", resolve: context => context.Source.Id);
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("type", resolve: context => context.Source.Type);
            Field<StringGraphType>("schema", resolve: context => context.Source.Schema);
            Field<ListGraphType<InputTypesType>>("inputTypes", resolve: context => context.Source.InputTypes);
            Field<ListGraphType<QueryDefinitionType>>("queries", resolve: context => context.Source.Queries);
            Field<ListGraphType<QueryDefinitionType>>("mutations", resolve: context => context.Source.Mutations);
            Field<ListGraphType<QueryDefinitionType>>("subscriptions", resolve: context => context.Source.Subscriptions);
            Field<ModelType>("model", resolve: context => context.Source.Model);
            Field<ConnectionType>("connection", resolve: context => data.GetConnection(new ObjectId(context.Source.Connection)));
            Field<ConnectionType, Connection>().Name("connectionBatch").ResolveAsync(context => {
                var loader = dataLoader.Context.GetOrAddBatchLoader<ObjectId, Connection>("GetConnectionsById", async (ids, CancellationToken) => {
                    var result = await data.GetConnectionsByIds((List<ObjectId>)ids);
                    Dictionary<ObjectId, Connection> res = new Dictionary<ObjectId, Connection>();
                    foreach (var c in result) {
                        res.Add(c.Id, c);
                    }
                    return res;
                });
                return loader.LoadAsync(new ObjectId(context.Source.Connection));
            });
        }
    }
    public class InputType
    {
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("description")]
        public string Description { get; set; }
    }
    public class InputTypesType : ObjectGraphType<InputType>
    {
        public InputTypesType() 
        { 
            Name = "InputTypeDescription";
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("description", resolve: context => context.Source.Description);
        }
    }
    public class InputTypesInputType : InputObjectGraphType<InputType>
    {
        public InputTypesInputType()
        {
            Name = "InputTypeDescriptionInput";
            Field<StringGraphType>("name");
            Field<StringGraphType>("description");
        }
    }
    public class TypesInputType : InputObjectGraphType<Types>
    {
        public TypesInputType(ConfigData data)
        {
            Name = "TypesInput";
            Field<StringGraphType>("name");
            Field<StringGraphType>("type");
            Field<StringGraphType>("schema");
            Field<ListGraphType<InputTypesInputType>>("inputTypes");
            Field<ListGraphType<QueryDefinitionInputType>>("queries");
            Field<ListGraphType<QueryDefinitionInputType>>("mutations");
            Field<ListGraphType<QueryDefinitionInputType>>("subscriptions");
            Field<ModelInputType>("model");
            Field<StringGraphType>("connection");
        }
    }
    public class Model
    {
        [BsonElement("modelType")]
        public string ModelType { get; set; }
        [BsonElement("key")]
        public List<string> Key { get; set; }
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("columns")]
        public List<Column> Columns { get; set; }
        [BsonElement("description")]
        public string Description { get; set; }
        [BsonElement("deprecated")]
        public string Deprecated { get; set; }

        public Dictionary<string, Column> Fields { get {
                Dictionary<string, Column> _fields = new Dictionary<string, Column>();
                foreach (Column c in Columns)
                {
                    _fields.Add(c.Name, c);
                } return _fields;
            }
        }
    }
    public class ModelType : ObjectGraphType<Model>
    {
        public ModelType(ConfigData data)
        {
            Name = "Model";
            Field<StringGraphType>("modelType", resolve: context => context.Source.ModelType);
            Field<ListGraphType<StringGraphType>>("key", resolve: context => context.Source.Key);
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<ListGraphType<ColumnType>>("columns", resolve: context => context.Source.Columns);
            Field<StringGraphType>("description", resolve: context => context.Source.Description);
            Field<StringGraphType>("deprecated", resolve: context => context.Source.Deprecated);
        }
    }
    public class ModelInputType : InputObjectGraphType<Model>
    {
        public ModelInputType(ConfigData data)
        {
            Name = "ModelInput";
            Field<StringGraphType>("modelType");
            Field<ListGraphType<StringGraphType>>("key");
            Field<StringGraphType>("name");
            Field<ListGraphType<ColumnInputType>>("columns");
            Field<StringGraphType>("description");
            Field<StringGraphType>("deprecated");
        }
    }
    public class Column
    {
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("dataName")]
        public string DataName { get; set; }
        [BsonElement("columnType")]
        public string ColumnType { get; set; }
        [BsonElement("columnGraphType")]
        public string ColumnGraphType { get; set; }
        [BsonElement("allowedGroups")]
        public List<string> AllowedGroups { get; set; }
        [BsonElement("arguments")]
        public List<Argument> Arguments { get; set; }
        [BsonElement("description")]
        public string Description { get; set; }
        [BsonElement("deprecated")]
        public string Deprecated { get; set; }
        [BsonElement("log")]
        public bool Log { get; set; }
    }
    public class ColumnType : ObjectGraphType<Column>
    {
        public ColumnType(ConfigData data)
        {
            Name = "Column";
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("dataName", resolve: context => context.Source.DataName);
            Field<StringGraphType>("columnType", resolve: context => context.Source.ColumnType);
            Field<StringGraphType>("columnGraphType", resolve: context => context.Source.ColumnGraphType);
            Field<ListGraphType<ArgumentType>>("arguments", resolve: context => context.Source.Arguments);
            Field<StringGraphType>("description", resolve: context => context.Source.Description);
            Field<StringGraphType>("deprecated", resolve: context => context.Source.Deprecated);
            Field<ListGraphType<ColumnType>>("subColumns", resolve: context => data.GetTypesType(context.Source.ColumnGraphType.Trim(new char[] { '[', ']', '!' })).Result.Model.Columns);
            Field<ListGraphType<ColumnType>>("filterColumns", resolve: context => data.GetFilterColumns(context.Source.ColumnGraphType.Trim(new char[] { '[', ']', '!' }), null));
            Field<ListGraphType<StringGraphType>>("allowedGroups", resolve: context => context.Source.AllowedGroups);
            Field<StringGraphType>("connectionType", resolve: context => data.GetTypesType(context.Source.ColumnGraphType.Trim(new char[] { '[', ']', '!' })).Result.Type);
            Field<BooleanGraphType>("log", resolve: context => context.Source.Log);
        }
    }
    public class ColumnInputType : InputObjectGraphType<Column>
    {
        public ColumnInputType(ConfigData data)
        {
            Name = "ColumnInput";
            Field<StringGraphType>("name");
            Field<StringGraphType>("dataName");
            Field<StringGraphType>("columnType");
            Field<StringGraphType>("columnGraphType");
            Field<ListGraphType<ArgumentInputType>>("arguments");
            Field<StringGraphType>("description");
            Field<StringGraphType>("deprecated");
            Field<ListGraphType<StringGraphType>>("allowedGroups");
            Field<BooleanGraphType>("log");
        }
    }
    public class Query
    {
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("schema")]
        public string Schema { get; set; }
        [BsonElement("arguments")]
        public List<Argument> Arguments { get; set; }
        public Dictionary<string, Argument> ArgumentsDict { get {
                Dictionary<string, Argument> _arguments = new Dictionary<string, Argument>();
                foreach (Argument c in Arguments)
                {
                    _arguments.Add(c.Name, c);
                }
                return _arguments;
            } }
        [BsonElement("type")]
        public string Type { get; set; }
        [BsonElement("allowedGroups")]
        public List<string> AllowedGroups {get;set;}
        [BsonElement("description")]
        public string Description { get; set; }
        [BsonElement("deprecated")]
        public string Deprecated { get; set; }
        [BsonElement("log")]
        public bool Log { get; set; }
    }
    public class QueryDefinitionType : ObjectGraphType<Query>
    {
        public QueryDefinitionType (ConfigData data)
        {
            Name = "QueryDefinition";
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("schema", resolve: context => context.Source.Schema);
            Field<ListGraphType<ArgumentType>>("arguments", resolve: context => context.Source.Arguments);
            Field<StringGraphType>("type", resolve: context => context.Source.Type);
            Field<ListGraphType<StringGraphType>>("allowedGroups", resolve: context => context.Source.AllowedGroups);
            Field<StringGraphType>("description", resolve: context => context.Source.Description);
            Field<StringGraphType>("deprecated", resolve: context => context.Source.Deprecated);
            Field<BooleanGraphType>("log", resolve: context => context.Source.Log);
        }
    }
    public class QueryDefinitionInputType : InputObjectGraphType<Query>
    {
        public QueryDefinitionInputType(ConfigData data)
        {
            Name = "QueryDefinitionInput";
            Field<StringGraphType>("name");
            Field<StringGraphType>("schema");
            Field<ListGraphType<ArgumentInputType>>("arguments");
            Field<StringGraphType>("type");
            Field<ListGraphType<StringGraphType>>("allowedGroups");
            Field<StringGraphType>("description");
            Field<StringGraphType>("deprecated");
            Field<BooleanGraphType>("log");
        }
    }
    public class Argument
    {
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("type")]
        public string Type { get; set; }
        [BsonElement("key")]
        public string Key { get; set; }
        [BsonElement("value")]
        public dynamic Value { get; set; }
        [BsonElement("description")]
        public string Description { get; set; }
    }
    public class ArgumentType : ObjectGraphType<Argument>
    {
        public ArgumentType (ConfigData data)
        {
            Name = "Argument";
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("type", resolve: context => context.Source.Type);
            Field<StringGraphType>("key", resolve: context => context.Source.Key);
            Field<StringGraphType>("value", resolve: context => context.Source.Value);
            Field<StringGraphType>("description", resolve: context => context.Source.Description);
        }
    }
    public class ArgumentInputType : InputObjectGraphType<Argument>
    {
        public ArgumentInputType(ConfigData data)
        {
            Name = "ArgumentInput";
            Field<StringGraphType>("name");
            Field<StringGraphType>("type");
            Field<StringGraphType>("key");
            Field<StringGraphType>("value");
            Field<StringGraphType>("description");
        }
    }
    public class IndexStringType : InputObjectGraphType<Dictionary<string, dynamic>>
    {
        public IndexStringType(ConfigData data)
        {
            Name = "IndexStringInput";
            Field<IntGraphType>("index");
            Field<StringGraphType>("value");
        }
    }
    public class IndexIntType : InputObjectGraphType<Dictionary<string, dynamic>>
    {
        public IndexIntType(ConfigData data)
        {
            Name = "IndexIntInput";
            Field<IntGraphType>("index");
            Field<IntGraphType>("value");
        }
    }
    public class IndexFloatType : InputObjectGraphType<Dictionary<string, dynamic>>
    {
        public IndexFloatType(ConfigData data)
        {
            Name = "IndexFloatInput";
            Field<IntGraphType>("index");
            Field<FloatGraphType>("value");
        }
    }
    public class IndexDateTimeType : InputObjectGraphType<Dictionary<string, dynamic>>
    {
        public IndexDateTimeType(ConfigData data)
        {
            Name = "IndexDateTimeInput";
            Field<IntGraphType>("index");
            Field<DateTimeGraphType>("value");
        }
    }
    public class IndexDateTimeOffsetType : InputObjectGraphType<Dictionary<string, dynamic>>
    {
        public IndexDateTimeOffsetType(ConfigData data)
        {
            Name = "IndexDateTimeOffsetInput";
            Field<IntGraphType>("index");
            Field<DateTimeOffsetGraphType>("value");
        }
    }
    public class IndexIdType : InputObjectGraphType<Dictionary<string, dynamic>>
    {
        public IndexIdType(ConfigData data)
        {
            Name = "IndexIdInput";
            Field<IntGraphType>("index");
            Field<IdGraphType>("value");
        }
    }
    public class IndexBooleanType : InputObjectGraphType<Dictionary<string, dynamic>>
    {
        public IndexBooleanType(ConfigData data)
        {
            Name = "IndexBooleanInput";
            Field<IntGraphType>("index");
            Field<BooleanGraphType>("value");
        }
    }
    public class TypiQLClientType : ObjectGraphType<Dictionary<string, dynamic>>
    {
        public TypiQLClientType(ConfigData data)
        {
            Name = "TypiQLClient";
            Field<StringGraphType>("computerName", resolve: context => context.Source["computerName"]);
            Field<StringGraphType>("ip", resolve: context => context.Source["ip"]);
        }
    }
}
