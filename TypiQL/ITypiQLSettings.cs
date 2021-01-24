using GraphQL;
using GraphQL.Resolvers;
using System;
using System.Collections.Generic;
using System.Text;
using TypiQL.Models;

namespace DataCrush.TypiQL
{
    public interface ITypiQLSettings
    {
        string ConnectionString { get; set; }
        string Database { get; set; }
        string AdminRole { get; set; }
        public List<TypiQLRole> Roles { get; set; }
        public List<string> RoleNames { get; }
        public List<CustomResolver> Resolvers { get; set; }
        public Dictionary<string, IFieldResolver> ResolversDict { get; }
    }
}
