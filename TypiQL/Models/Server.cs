using GraphQL.Types;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace DataCrush.TypiQL.Models
{
    public class Server
    {
        [BsonId]
        public ObjectId Id { get; set; }
        [BsonElement("accessToken")]
        public string AccessToken { get; set; }
        [BsonElement("expiresOn")]
        public DateTime ExpiresOn { get; set; }
        [BsonElement("groupFilter")]
        public string GroupFilter { get; set; }
        [BsonElement("live")]
        public bool Live { get; set; }
    }
    public class ServerType : ObjectGraphType<Server>
    {
        public ServerType (ConfigData data)
        {
            Name = "TypiQLServer";
            Field<IdGraphType>("id", resolve: context => context.Source.Id);
            Field<DateTimeGraphType>("expiresOn", resolve: context => context.Source.ExpiresOn);
            Field<StringGraphType>("groupFilter", resolve: context => context.Source.GroupFilter);
            Field<BooleanGraphType>("live", resolve: context => context.Source.Live);
        }
    }
    public class ServerInputType : InputObjectGraphType<Server>
    {
        public ServerInputType(ConfigData data)
        {
            Name = "TypiQLServerInput";
            Field<StringGraphType>("groupFilter");
            Field<BooleanGraphType>("live");
        }
    }
}
