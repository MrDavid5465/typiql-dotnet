using GraphQL.Introspection;
using GraphQL.Types;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Text;

namespace TypiQL.Models
{
    public class TypiQLRole
    {
        public string Name { get; }
        public Action<AuthorizationPolicyBuilder> Builder { get; }
        public TypiQLRole (string name, Action<AuthorizationPolicyBuilder> builder)
        {
            Name = name;
            Builder = builder;
        }
    }
}
