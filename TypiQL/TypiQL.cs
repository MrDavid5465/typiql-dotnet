using DataCrush.TypiQL.Models;
using DataCrush.TypiQL.Models.AD;
using DataCrush.TypiQL.Models.Mongo;
using DataCrush.TypiQL.Models.Sql;
using GraphQL.Server;
using GraphQL.Server.Ui.GraphiQL;
using GraphQL.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DataCrush.TypiQL
{
    public static class TypiQLStartupExtensions
    {
        public static IServiceCollection AddTypiQL(this IServiceCollection services, List<TypiQLRole> roles)
        {
            services.AddHttpContextAccessor()
            .AddSingleton<TypiQLMongoContext>()
            .AddSingleton<BaseSchema>()
            .AddSingleton<TypesRepo>()
            .AddSingleton<ConnectionsRepo>()
            .AddSingleton<ConfigData>()
            .AddSingleton<MongoData>()
            .AddSingleton<SqlData>()
            .AddSingleton<ADData>()
            .AddSingleton<Queries>()
            .AddSingleton<Mutations>()
            .AddSingleton<Subscriptions>()
            .AddGraphQL(_ =>
            {
                _.EnableMetrics = false;
            })
            .AddGraphQLAuthorization(options => {
                foreach (TypiQLRole role in roles)
                {
                    options.AddPolicy(role.Name, role.Builder);
                }
            })
            .AddSystemTextJson(deserializerSettings => { }, serializerSettings => { })
            .AddWebSockets()
            .AddDataLoader()
            .AddGraphTypes(typeof(BaseSchema));
            try
            {
                services.AddSingleton<ISchema, OrgSchema>();
            }
            catch (Exception)
            {
                services.AddSingleton<ISchema, BaseSchema>();
            }
            return services;
        }
        public static IApplicationBuilder UseTypiQL(this IApplicationBuilder app)
        {
            app.UseWebSockets();
            app.UseGraphQLWebSockets<ISchema>("/graphql");
            app.UseGraphQL<ISchema, TypiQLMiddleware<ISchema>>("/graphql");
            app.UseGraphiQLServer(new GraphiQLOptions { GraphQLEndPoint = "/typiql/graphql"});
            return app;
        }
        
    }
    public static class ObjectExtensions
    {
        public static T ToObject<T>(this IDictionary<string, object> source)
            where T : class, new()
        {
            var someObject = new T();
            var someObjectType = someObject.GetType();

            foreach (var item in source)
            {
                someObjectType
                         .GetProperty(item.Key)
                         .SetValue(someObject, item.Value, null);
            }

            return someObject;
        }

        public static IDictionary<string, object> AsDictionary(this object source, BindingFlags bindingAttr = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
        {
            return source.GetType().GetProperties(bindingAttr).ToDictionary
            (
                propInfo => propInfo.Name,
                propInfo => propInfo.GetValue(source, null)
            );
        }
    }
}
