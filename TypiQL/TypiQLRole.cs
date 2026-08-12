using GraphQL.Introspection;
using GraphQL.Types;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Text;

namespace DataCrush.TypiQL
{
    public class TypiQLRole
    {
        public string Name { get; }
        public string Value { get; }
        public Action<AuthorizationPolicyBuilder> Builder { get; }
        public TypiQLRole (string name, string value)
        {
            Name = name;
            Value = value;
            Builder = p => p.RequireRole(value);
        }
    }
    public class TypiQLRoleType : ObjectGraphType<TypiQLRole>
    {
        public TypiQLRoleType()
        {
            Name = "TypiQLRole";
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("value", resolve: context => context.Source.Value);
        }
    }
}
