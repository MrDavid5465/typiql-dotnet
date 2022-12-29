using DataCrush.TypiQL.Models;
using GraphQL.Types;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace TypiQL.Models
{
    public class ConfigBackup
    {
        [BsonId]
        public ObjectId Id { get; set; }
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("created")]
        public DateTime Created { get; set; }
        [BsonElement("connections")]
        public List<Connection> Connections { get; set; }
        [BsonElement("types")]
        public List<Types> Types { get; set; }
    }
    public class ConfigBackupType : ObjectGraphType<ConfigBackup>
    {
        public ConfigBackupType()
        {
            Name = "ConfigBackup";
            Field<IdGraphType>("Id", resolve: context => context.Source.Id);
            Field<StringGraphType>("Name", resolve: context => context.Source.Name);
            Field<DateTimeGraphType>("Created", resolve: context => context.Source.Created);
            Field<ListGraphType<ConnectionType>>("Connections", resolve: context => context.Source.Connections);
            Field<ListGraphType<TypesType>>("Types", resolve: context => context.Source.Types);
        }
    }
}
