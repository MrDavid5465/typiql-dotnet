using DataCrush.TypiQL;
using System.Collections.Generic;
using TypiQL.Models;

namespace TypiQLDebug.Models
{
    public class Settings
    {
        public string ConnectionString { get; set; }
        public string Database { get; set; }
        public string Secret { get; set; }
    }
}