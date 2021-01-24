using GraphQL;
using GraphQL.Server.Authorization.AspNetCore;
using GraphQL.Types;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Linq;

namespace DataCrush.TypiQL.Models
{

    public class Mutations : ObjectGraphType
    {
        public Mutations(TypiQLSettings settings, ConfigData data, IHttpContextAccessor httpContext)
        {
            Name = "Mutation";
            Field<ConnectionType>(
                "addConnection",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<ConnectionInputType>> { Name = "values" }
                ),
                resolve: context =>
                {
                    Dictionary<string, dynamic> values = context.GetArgument<dynamic>("values");
                    Connection connection = new Connection
                    {
                        DatabaseName = values["databaseName"],
                        Name = values["name"],
                        ConnectionString = values["connectionString"],
                        Type = values["type"]
                    };
                    if (connection.DatabaseName == "config" && connection.ConnectionString == "mongodb://localhost:27017" && connection.Type == "mongo")
                    {
                        return new Connection();
                    }
                    return data.AddConnection(connection);
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<TypesType>(
                "addType",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<TypesInputType>> { Name = "values" }
                ),
                resolve: context =>
                {
                    return data.AddType(context.GetArgument<dynamic>("values"));
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<ConnectionType>(
                "updateConnection",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" },
                    new QueryArgument<NonNullGraphType<ConnectionInputType>> { Name = "update" }
                ),
                resolve: context =>
                {
                    Connection connection = data.GetConnection(new ObjectId(context.GetArgument<string>("id"))).Result;
                    Dictionary<string, dynamic> connectionUpdate = context.GetArgument<dynamic>("update");
                    connection.Type = connectionUpdate.ContainsKey("type") ? connectionUpdate["type"] : connection.Type;
                    connection.DatabaseName = connectionUpdate.ContainsKey("databaseName") ? connectionUpdate["databaseName"] : connection.DatabaseName;
                    connection.ConnectionString = connectionUpdate.ContainsKey("connectionString") ? connectionUpdate["connectionString"] : connection.ConnectionString;
                    if (connection.DatabaseName == "config" && connection.ConnectionString == "mongodb://localhost:27017" && connection.Type == "mongo")
                    {
                        return new Connection();
                    }
                    return data.UpdateConnection(new ObjectId(context.GetArgument<string>("id")), context.GetArgument<dynamic>("update"));
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<TypesType>(
                "updateType",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" },
                    new QueryArgument<NonNullGraphType<TypesInputType>> { Name = "update" }
                ),
                resolve: context =>
                {
                    Dictionary<string, dynamic> t = context.GetArgument<dynamic>("update");
                    return data.UpdateType(new ObjectId(context.GetArgument<string>("id")), t);
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<ServerType>(
                "updateServerConfig",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<ServerInputType>> { Name = "update" }
                ),
                resolve: context =>
                {
                    Dictionary<string, dynamic> t = context.GetArgument<dynamic>("update");
                    return data.UpdateServer(t);
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<ConnectionType>(
                "removeConnection",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" }
                ),
                resolve: context =>
                {
                    return data.RemoveConnection(new ObjectId(context.GetArgument<string>("id")));
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<TypesType>(
                "removeType",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" }
                ),
                resolve: context =>
                {
                    return data.RemoveType(new ObjectId(context.GetArgument<string>("id")));
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<ListGraphType<ColumnType>>(
                "validateTypeSchema",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "name" },
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "schema" },
                    new QueryArgument<NonNullGraphType<ListGraphType<StringGraphType>>> { Name = "queries" },
                    new QueryArgument<NonNullGraphType<ListGraphType<StringGraphType>>> { Name = "mutations" }
                ),
                resolve: context =>
                {
                    return data.ValidateTypeSchema(
                        context.GetArgument<string>("name"),
                        context.GetArgument<string>("schema"),
                        context.GetArgument<List<string>>("queries"),
                        context.GetArgument<List<string>>("mutations")
                    );
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<BucketType>(
                "saveBucket",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "name" },
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "type" },
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "object" }
                ),
                resolve: context =>
                {
                    Dictionary<string, dynamic> b = new Dictionary<string, dynamic>();
                    b.Add("name", context.GetArgument<string>("name"));
                    b.Add("type", context.GetArgument<string>("type"));
                    b.Add("owner", data.GetUserName());
                    b.Add("objectString", context.GetArgument<string>("object"));
                    return data.SaveBucket(context.GetArgument<string>("name"), context.GetArgument<string>("type"), data.GetUserName(), b);
                }
            ).AuthorizeWith(settings.AdminRole);
            Field<BucketType>(
                "removeBucket",
                arguments: new QueryArguments(
                    new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id" }
                ),
                resolve: context =>
                {
                    Dictionary<string, dynamic> b = new Dictionary<string, dynamic>();
                    b.Add("name", context.GetArgument<string>("name"));
                    b.Add("type", context.GetArgument<string>("type"));
                    return data.EmptyBucket(new ObjectId(context.GetArgument<string>("id")));
                }
            ).AuthorizeWith(settings.AdminRole);
        }
    }
}