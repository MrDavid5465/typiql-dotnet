using GraphQL;
using GraphQL.Resolvers;
using System;
using System.Collections.Generic;
using System.Text;

namespace DataCrush.TypiQL
{
    public class CustomResolver
    {
        public string Name { get; }
        public Func<IFieldResolver> ResolverSetup { get; }
        public IFieldResolver Resolver { get; set; }
        public CustomResolver (string name, Func<IFieldResolver> resolver)
        {
            Name = name;
            ResolverSetup = resolver;
        }

        public void GetFieldResolver ()
        {
            Resolver = ResolverSetup.Invoke();
        }
    }
}
