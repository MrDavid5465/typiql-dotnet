using DataCrush.TypiQL.Models;
using DataCrush.TypiQL.Models.AD;
using DataCrush.TypiQL.Models.Mongo;
using DataCrush.TypiQL.Models.Sql;
using GraphQL;
using GraphQL.DataLoader;
using GraphQL.Server;
using GraphQL.Server.Transports.AspNetCore;
using GraphQL.Server.Ui.GraphiQL;
using GraphQL.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TypiQL.Models;

namespace DataCrush.TypiQL
{
    public static class TypiQLStartupExtensions
    {
        public static IServiceCollection AddTypiQL(this IServiceCollection services)
        {
            services.AddHttpContextAccessor()
            .AddSingleton<IDataLoaderContextAccessor, DataLoaderContextAccessor>()
            .AddSingleton<DataLoaderDocumentListener>()
            .AddSingleton<TypiQLMongoContext>()
            .AddSingleton<BaseSchema>()
            .AddSingleton<SchemaHelpers>()
            .AddSingleton<TypesRepo>()
            .AddSingleton<ConnectionsRepo>()
            .AddSingleton<ConfigData>()
            .AddSingleton<MongoData>()
            .AddSingleton<SqlData>()
            .AddSingleton<ADData>()
            .AddSingleton<Queries>()
            .AddSingleton<Mutations>()
            .AddSingleton<GraphQLMiddleware>()
            .AddSingleton<Subscriptions>()
            .AddGraphQL(b => {
                b.AddUserContextBuilder(httpContext => new GraphQLUserContext { User = httpContext.User });
                b.AddSystemTextJson();

                try
                {
                    b.AddAutoSchema<OrgSchema>();
                }
                catch (Exception ex)
                {
                    b.AddAutoSchema<BaseSchema>();
                }
                b.AddAuthorizationRule();
            })
            .AddSingleton(new GraphQLSettings
            {
                Path = "/graphql",
                BuildUserContext = ctx => new GraphQLUserContext
                {
                    User = ctx.User
                },
                EnableMetrics = true
            });

            
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
        public static IApplicationBuilder UseTypiQL(this IApplicationBuilder app, List<TypiQLRole> roles)
        {
            app.UseWebSockets();
            var options = new GraphQLHttpMiddlewareOptions()
            {
                AuthorizationRequired = true,
            };
            foreach (TypiQLRole role in roles)
            {
                options.AuthorizedRoles.Add(role.Name);
            }

            app.UseGraphQL<TypiQLMiddleware<ISchema>>("/graphql", options);
                //config =>
            //{
            //    config.AuthorizationRequired = false;
            //    foreach (TypiQLRole role in roles)
            //    {
            //        config.AuthorizedRoles.Add(role.Name);
            //    }
            //});

            //app.UseMiddleware<GraphQLMiddleware>();
            app.UseGraphQLGraphiQL("/ui/graphiql", options: new GraphiQLOptions { GraphQLEndPoint = "/graphql"});
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
