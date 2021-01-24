using GraphQL;
using GraphQL.Resolvers;
using System;
using System.Collections.Generic;
using TypiQL.Models;

namespace DataCrush.TypiQL.Models
{
    public class TypiQLSettings : ITypiQLSettings
    {
        public string ConnectionString { get; set; }
        public string Database { get; set; }
        public string AdminRole { get; set; }
        public List<TypiQLRole> Roles { get; set; }
        public List<string> RoleNames
        {
            get
            {
                List<string> names = new List<string>();
                foreach (TypiQLRole role in Roles)
                {
                    names.Add(role.Name);
                }
                return names;
            }
        }
        public List<CustomResolver> Resolvers { get; set; }
        public Dictionary<string, IFieldResolver> ResolversDict { get
            {
                Dictionary<string, IFieldResolver> resolvers = new Dictionary<string, IFieldResolver>();
                foreach(var resolver in Resolvers)
                {
                    resolvers.Add(resolver.Name, resolver.Resolver);
                }
                return resolvers;
            } 
        }

    }
}
