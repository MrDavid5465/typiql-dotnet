using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataCrush.TypiQL;
using TypiQLDebug.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using DataCrush.TypiQL.Models;
using TypiQL.Models;
using GraphQL;
using TypiQLDebug.Services;
using TypiQLDebug.Models.Mongo;
using Microsoft.AspNetCore.Http;
using System.Linq.Expressions;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using GraphQL.Resolvers;
using TypiQLDebug.Models.Mongo.Types;

namespace TypiQLDebug
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        readonly string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            //services.AddCors(options =>
            //{
            //    options.AddPolicy(
            //        name: MyAllowSpecificOrigins,
            //        builder => builder.AllowAnyOrigin()
            //    );
            //});
            var appSettingsSection = Configuration.GetSection("AppSettings");
            Settings settings = new Settings
            {
                ConnectionString = Configuration.GetSection("MongoConnection:ConnectionString").Value,
                Database = Configuration.GetSection("MongoConnection:Database").Value,
                Secret = Configuration.GetSection("Authentication:Secret").Value
            };
            services.Configure<Settings>(options =>
            {
                options.ConnectionString = Configuration.GetSection("MongoConnection:ConnectionString").Value;
                options.Database = Configuration.GetSection("MongoConnection:Database").Value;
                options.Secret = Configuration.GetSection("Authentication:Secret").Value;
            });
            var key = Encoding.ASCII.GetBytes(Configuration.GetSection("Authentication:Secret").Value);
            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidAudience = "feedme.com",
                    ValidIssuer = "feedme.com",
                };
                x.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        context.Token = context.Request.Cookies["accessToken"];
                        return Task.CompletedTask;
                    },
                };
            });
            
            services.AddSingleton<UserService>();
            services.AddSingleton<MongoContext>();
            services.AddSingleton<TestData>();
            services.AddTypiQL(
                new TypiQLSettings
                {
                    ConnectionString = Configuration.GetSection("TypiQLConfig:ConnectionString").Value,
                    Database = Configuration.GetSection("TypiQLConfig:ConfigDatabase").Value,
                    AdminRole = Configuration.GetSection("TypiQLConfig:AdminRole").Value,
                    Resolvers = new List<CustomResolver> {
                        new CustomResolver(
                            "register",
                            new Func<IServiceProvider, IFieldResolver>((sp) => {
                                TestData data = sp.GetRequiredService<TestData>();
                                return new FuncFieldResolver<dynamic>((context) => {
                                    return data.Register(context.GetArgument<User>("values")).Result.AsDictionary();
                                });
                            }) 
                        ),
                        new CustomResolver(
                            "refresh", 
                            new Func<IServiceProvider, IFieldResolver>(sp => {
                                TestData data = sp.GetRequiredService<TestData>();
                                return new FuncFieldResolver<dynamic>(context =>
                                {
                                    return data.Refresh().Result.AsDictionary();
                                });
                            })
                        ),
                        new CustomResolver(
                            "login",
                            new Func<IServiceProvider, IFieldResolver>(sp => {
                                TestData data = sp.GetRequiredService<TestData>();
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
                            new Func<IServiceProvider, IFieldResolver>(sp => {
                                TestData data = sp.GetRequiredService<TestData>();
                                return new FuncFieldResolver<dynamic>(context =>
                                {
                                    data.Logout();
                                    return new User().AsDictionary();
                                });
                            })
                        )
                    },
                    Roles = new List<TypiQLRole>
                    {
                        new TypiQLRole("Authorized", p => p.RequireAuthenticatedUser()),
                        new TypiQLRole("Admin", p => p.RequireRole("Admin")),
                        new TypiQLRole("otherRole", p => p.RequireRole("otherRole"))
                    }
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            //app.UseCors(MyAllowSpecificOrigins);

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseTypiQL();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
