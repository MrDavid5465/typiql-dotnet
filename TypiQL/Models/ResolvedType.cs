using System.Collections.Generic;

namespace DataCrush.TypiQL.Models
{
    public class ResolvedType
    {
        public string Name { get; set; }
        public string ResolvedName { get; set; }
        public List<string> TypeStack { get; set; }
    }
}
