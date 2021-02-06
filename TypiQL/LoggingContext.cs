using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Text;

namespace DataCrush.TypiQL
{
    public class LoggingContext
    {
        public ObjectId Id { get; set; }
        public DateTime DateTime { get; set; }
        public Dictionary<string, dynamic> Details { get; set; }
    }
}
