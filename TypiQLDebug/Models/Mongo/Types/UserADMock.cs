using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TypiQLDebug.Models.Mongo.Types
{
    public class UserADMock
    {
        [BsonId]
        public ObjectId _id { get; set; }
        [BsonElement("objectSid")]
        public string sid { get; set; }
        [BsonElement("sAMAccountName")]
        public string sAMAccountName { get; set; }
        [BsonElement("distinguishedName")]
        public string distinguishedName { get; set; }
        [BsonElement("displayName")]
        public string displayName { get; set; }
        [BsonElement("mail")]
        public string mail { get; set; }
        [BsonElement("memberOf")]
        public List<string> memberOf { get; set; } 
        [BsonElement("physicalDeliveryOfficeName")]
        public string physicalDeliveryOfficeName { get; set; }
    }
}
