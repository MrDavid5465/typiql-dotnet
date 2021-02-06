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

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            var appSettingsSection = Configuration.GetSection("AppSettings");

            Settings settings = new Settings
            {
                ConnectionString = Configuration.GetSection("MongoConnection:ConnectionString").Value,
                Database = Configuration.GetSection("MongoConnection:Database").Value,
                Secret = Configuration.GetSection("Authentication:Secret").Value,
                TypiQLConnectionString = Configuration.GetSection("TypiQLConfig:ConnectionString").Value,
                TypiQLDatabase = Configuration.GetSection("TypiQLConfig:ConfigDatabase").Value,
                TypiQLAdminRole = Configuration.GetSection("TypiQLConfig:AdminRole").Value,
                UserNameProperty = Configuration.GetSection("TypiQLConfig:UserNameProperty").Value
            };
            settings.GetRoles();

            services.Configure<Settings>(options =>
            {
                options.ConnectionString = settings.ConnectionString;
                options.Database = settings.Database;
                options.Secret = settings.Secret;
                options.TypiQLConnectionString = settings.TypiQLConnectionString;
                options.TypiQLDatabase = settings.TypiQLDatabase;
                options.TypiQLAdminRole = settings.TypiQLAdminRole;
                options.UserNameProperty = settings.UserNameProperty;
                options.Roles = settings.Roles;
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
                    ValidAudience = "data-crush.com",
                    ValidIssuer = "data-crush.com",
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
            
            services.AddSingleton<IUserService, UserService>();
            services.AddSingleton<TestData>();
            services.AddSingleton<TypiQLSettings, TestTypiQL>();
            services.AddHttpContextAccessor();

            services.AddTypiQL(settings.Roles);
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
