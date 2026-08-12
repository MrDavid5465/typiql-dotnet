using GraphQL;
using GraphQL.Resolvers;
using System;
using System.Collections.Generic;

namespace DataCrush.TypiQL
{
    public class TypiQLSettings : ITypiQLSettings
    {
        public string TypiQLConnectionString { get; set; }
        public string TypiQLDatabase { get; set; }
        public string TypiQLAdminRole { get; set; }
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
        public Dictionary<string, TypiQLRole> RolesDict 
        {
            get
            {
                Dictionary<string, TypiQLRole> names = new Dictionary<string, TypiQLRole>();
                foreach (TypiQLRole role in Roles)
                {
                    names.Add(role.Name, role);
                }
                return names;
            }
        }
        public List<CustomResolver> Resolvers { get; set; }
        public Dictionary<string, CustomResolver> ResolversDict 
        {
            get
            {
                Dictionary<string, CustomResolver> resolvers = new Dictionary<string, CustomResolver>();
                foreach(var resolver in Resolvers)
                {
                    resolvers.Add(resolver.Name, resolver);
                }
                return resolvers;
            } 
        }
        public UserCRUD UserCRUD { get; set; }
        public Func<LoggingContext, dynamic> Logger { get; set; }
    }
}
