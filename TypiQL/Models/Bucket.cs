using GraphQL.Types;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataCrush.TypiQL.Models
{
    public class Bucket
    {
        [BsonId]
        public ObjectId Id { get; set; }
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("owner")]
        public string Owner { get; set; }
        [BsonElement("type")]
        public string BucketType { get; set; }
        [BsonElement("objectString")]
        public string BucketObjectString { get; set; }
    }
    public class BucketType : ObjectGraphType<Bucket>
    {
        public BucketType(ConfigData data)
        {
            Name = "Bucket";
            Field<IdGraphType>("id", resolve: context => context.Source.Id);
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("type", resolve: context => context.Source.BucketType);
            Field<StringGraphType>("object", resolve: context => {
                return JsonConvert.DeserializeObject<JObject>(context.Source.BucketObjectString);
            });
        }
    }
    public class BucketInputType : InputObjectGraphType<Bucket>
    {
        public BucketInputType(ConfigData data)
        {
            Name = "BucketInput";
            Field<StringGraphType>("name");
            Field<StringGraphType>("type");
            Field<StringGraphType>("object");
        }
    }
}
