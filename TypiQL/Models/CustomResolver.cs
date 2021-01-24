using GraphQL;
using GraphQL.Resolvers;
using System;
using System.Collections.Generic;
using System.Text;

namespace TypiQL.Models
{
    public class CustomResolver
    {
        public string Name { get; }
        public Func<IServiceProvider, IFieldResolver> ResolverSetup { get; }
        public IFieldResolver Resolver { get; set; }
        public CustomResolver (string name, Func<IServiceProvider, IFieldResolver> resolver)
        {
            Name = name;
            ResolverSetup = resolver;
        }

        public void GetFieldResolver (IServiceProvider provider)
        {
            Resolver = ResolverSetup.Invoke(provider);
        }
    }
}
