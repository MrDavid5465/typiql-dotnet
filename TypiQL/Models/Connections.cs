using GraphQL.Types;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DataCrush.TypiQL.Models
{
    public class Connection
    {
        [BsonId]
        public ObjectId Id { get; set; }
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("type")]
        public string Type { get; set; }
        [BsonElement("connectionString")]
        public string ConnectionString { get; set; }
        [BsonElement("databaseName")]
        public string DatabaseName { get; set; }
    }
    public class ConnectionType : ObjectGraphType<Connection>
    {
        public ConnectionType (ConfigData data)
        {
            Name = "Connection";
            Field<IdGraphType>("id", resolve: context => context.Source.Id);
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("type", resolve: context => context.Source.Type);
            Field<StringGraphType>("connectionString", resolve: context => context.Source.ConnectionString);
            Field<StringGraphType>("databaseName", resolve: context => context.Source.DatabaseName);
            Field<ListGraphType<TypesType>>("types", resolve: context => data.GetTypes(context.Source.Id));
        }
    }
    public class ConnectionInputType : InputObjectGraphType<Connection>
    {
        public ConnectionInputType(ConfigData data)
        {
            Name = "ConnectionInput";
            Field<StringGraphType>("name");
            Field<StringGraphType>("type");
            Field<StringGraphType>("connectionString");
            Field<StringGraphType>("databaseName");
        }
    }
}
