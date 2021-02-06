using DataCrush.TypiQL;
using GraphQL;
using GraphQL.Resolvers;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TypiQLDebug.Models.Mongo.Types;

namespace TypiQLDebug.Models
{
    public class TestTypiQL : TypiQLSettings
    {
        public Settings _settings;
        public TestTypiQL(TestData data, IOptions<Settings> settings)
        {
            _settings = settings.Value;
            TypiQLConnectionString = _settings.TypiQLConnectionString;
            TypiQLDatabase = _settings.TypiQLDatabase;
            TypiQLAdminRole = _settings.TypiQLAdminRole;
            UserCRUD = new UserCRUD
            {
                UserNameProperty = _settings.UserNameProperty
            };
            Resolvers = new List<CustomResolver> {
                new CustomResolver(
                    "register",
                    new Func<IFieldResolver>(() => {
                        return new FuncFieldResolver<dynamic>((context) => {
                            return data.Register(context.GetArgument<User>("values")).Result.AsDictionary();
                        });
                    })
                ),
                new CustomResolver(
                    "refresh",
                    new Func<IFieldResolver>(() => {
                        return new FuncFieldResolver<dynamic>(context =>
                        {
                            return data.Refresh().Result.AsDictionary();
                        });
                    })
                ),
                new CustomResolver(
                    "refreshToken",
                    new Func<IFieldResolver>(() => {
                        return new FuncFieldResolver<dynamic>(context =>
                        {
                            User user = data.Refresh().Result;
                            return user.userName != null;
                        });
                    })
                ),
                new CustomResolver(
                    "login",
                    new Func<IFieldResolver>(() => {
                        return new FuncFieldResolver<dynamic>(context => {
                            var wat = data.Authenticate(
                                context.GetArgument<string>("username"),
                                context.GetArgument<string>("password")).Result.AsDictionary();
                            return wat;
                        });
                    })
                ),
                new CustomResolver(
                    "logout",
                    new Func<IFieldResolver>(() => {
                        return new FuncFieldResolver<dynamic>(context =>
                        {
                            data.Logout();
                            return new User().AsDictionary();
                        });
                    })
                )
            };
            Roles = _settings.Roles;
        }
    }
}
