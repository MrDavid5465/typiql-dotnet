using DataCrush.TypiQL.Models.AD;
using DataCrush.TypiQL.Models.Mongo;
using DataCrush.TypiQL.Models.Sql;
using GraphQL;
using GraphQL.Server.Authorization.AspNetCore;
using GraphQL.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace DataCrush.TypiQL.Models
{

    public class Queries : ObjectGraphType
    {
        private readonly TypiQLSettings _settings;
        public Queries(TypiQLSettings settings, ConfigData data, IHttpContextAccessor httpContext, IServiceProvider provider, IHostApplicationLifetime lifetime)
        {
            _settings = settings;
            Name = "Query";

            Field<BooleanGraphType>(
                "running",
                resolve: context => {
                    var wat = httpContext;
                    return true;
                }
            );
            Field<BooleanGraphType>(
                "inAdminRole",
                resolve: context => {
                    return httpContext.HttpContext.User.IsInRole(_settings.RolesDict[_settings.TypiQLAdminRole].Value);
                }
            );
            Field<StringGraphType>(
                "getSchemaError",
                resolve: context =>
                {
                    try
                    {
                        var schema = new OrgSchema(provider, httpContext, data, settings, lifetime);
                        return null;
                    }
                    catch (Exception e)
                    {
                        return e.ToString();
                    }
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<ConnectionType>>(
                "getConnections",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "type" }
                ),
                resolve: context =>
                {
                    if (context.GetArgument<string>("type") != null)
                    {
                        return data.GetConnections(context.GetArgument<string>("type"));
                    }
                    return data.GetConnections();
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ConnectionType>(
                "getConnection",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" }
                ),
                resolve: context =>
                {
                    return data.GetConnection(new ObjectId(context.GetArgument<string>("id")));
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<SqlColumnType>>(
                "getSQLColumns",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "connection" },
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "table" }
                ),
                resolve: context =>
                {
                    return data.GetSqlColumns(context.GetArgument<string>("connection"), context.GetArgument<string>("table"));                 
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ConnectionType>(
                "getConnectionByName",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "name" }
                ),
                resolve: context =>
                {
                    return data.GetConnection(context.GetArgument<string>("name"));
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<TypesType>>(
                "getTypes",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "type" },
                    new QueryArgument<StringGraphType> { Name = "connection" }
                ),
                resolve: context =>
                {
                    if (context.GetArgument<string>("connection") != null)
                    {
                        return data.GetTypes(new ObjectId(context.GetArgument<string>("connection")));
                    }
                    if (context.GetArgument<string>("type") != null)
                    {
                        return data.GetTypes(context.GetArgument<string>("type"));
                    }
                    return data.GetTypes();
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<StringGraphType>>(
                "getDatabaseNames",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "type" },
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "connectionString" }
                ),
                resolve: context =>
                {
                    return data.GetDatabaseNames(context.GetArgument<string>("type"), context.GetArgument<string>("connectionString"));                    
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<TypesType>(
                "getTypeByName",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "name" }
                ),
                resolve: context =>
                {
                    return data.GetTypesType(context.GetArgument<string>("name"));
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<StringGraphType>(
                "getTypeAsJson",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "id" },
                    new QueryArgument<StringGraphType> { Name = "name" }
                ),
                resolve: context =>
                {
                    if (context.GetArgument<string>("id") != null)
                    {
                        return data.GetTypeAsJson(data.GetTypesType(new ObjectId(context.GetArgument<string>("id"))).Result.Name);
                    }
                    return data.GetTypeAsJson(context.GetArgument<string>("name"));
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<TypesType>(
                "getType",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" }
                ),
                resolve: context =>
                {
                    return data.GetTypesType(new ObjectId(context.GetArgument<string>("id")));
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<ColumnType>>(
                "getFilterColumns",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "name" },
                    new QueryArgument<StringGraphType> { Name = "schema" }
                ),
                resolve: context =>
                {
                    return data.GetFilterColumns(context.GetArgument<string>("name"), context.GetArgument<string>("schema"));
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<StringGraphType>(
                "getServerApiKey",
                resolve: context =>
                {
                    return data.GetServer().Result.AccessToken;
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ServerType>(
                "getServerConfig",
                resolve: context =>
                {
                    return data.GetServer();
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<BucketType>(
                "getBucket",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "id" },
                    new QueryArgument<StringGraphType> { Name = "name" },
                    new QueryArgument<StringGraphType> { Name = "type" }
                ),
                resolve: context =>
                {
                    if (context.GetArgument<string>("id") != null)
                    {
                        return data.GetBucket(new ObjectId(context.GetArgument<string>("id")));
                    }
                    return data.GetBucket(context.GetArgument<string>("name"), context.GetArgument<string>("type"), data.GetUserName());
                    
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<BucketType>>(
                "getBuckets",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "name" },
                    new QueryArgument<StringGraphType> { Name = "type" }
                ),
                resolve: context =>
                {
                    return data.ListBucket(context.GetArgument<string>("name"), context.GetArgument<string>("type"), data.GetUserName());
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<TypiQLRoleType>>(
                "getGroups",
                resolve: context =>
                {
                    return data.GetGroups();
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<ListGraphType<TypiQLRoleType>>(
                "getMyTypiQLRoles",
                resolve: context =>
                {
                    return data.GetMyRoles();
                }
            );
            Field<ListGraphType<TypiQLRoleType>>(
                "getTypiQLRoles",
                resolve: context =>
                {
                    return data.GetRoles();
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<StringGraphType>(
                "getSchemaString",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "name" },
                    new QueryArgument<StringGraphType> { Name = "schema" },
                    new QueryArgument<ListGraphType<StringGraphType>> { Name = "queries" },
                    new QueryArgument<ListGraphType<StringGraphType>> { Name = "mutations" },
                    new QueryArgument<StringGraphType> { Name = "remove" }
                ),
                resolve: context =>
                {
                    return data.GetSchemaString(
                        context.GetArgument<string>("name"),
                        context.GetArgument<string>("schema"),
                        context.GetArgument<List<string>>("queries"),
                        context.GetArgument<List<string>>("mutations"),
                        context.GetArgument<string>("remove")
                    );
                }
            ).AuthorizeWith(_settings.TypiQLAdminRole);
            Field<TypiQLClientType>(
                "getTypiQLClient",
                resolve: context =>
                {
                    return new Dictionary<string, dynamic> {
                    { "computerName", Dns.GetHostEntry(httpContext.HttpContext.Connection.RemoteIpAddress).HostName },
                    { "ip", httpContext.HttpContext.Connection.RemoteIpAddress.ToString() }
                    };
                }
            );
            //Field<FileUploadType>(
            //    "getFileBase64",
            //    arguments: new QueryArguments(
            //        new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" }
            //    ),
            //    resolve: context => mongoData.ReadFileAsBase64("UserData", context.GetArgument<string>("id"))
            //);
        }
    }
}
