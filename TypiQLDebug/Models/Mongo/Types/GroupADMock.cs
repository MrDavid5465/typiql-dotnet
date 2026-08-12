using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TypiQLDebug.Models.Mongo.Types
{
    public class GroupADMock
    {
        [BsonId]
        public ObjectId _id { get; set; }
        [BsonElement("objectSid")]
        public string sid { get; set; }
        [BsonElement("sAMAccountName")]
        public string sAMAccountName { get; set; }
        [BsonElement("name")]
        public string name { get; set; }
        [BsonElement("distinguishedName")]
        public string distinguishedName { get; set; }
        [BsonElement("members")]
        public List<string> members { get; set; }
    }
}
