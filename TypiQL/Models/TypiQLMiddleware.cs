using GraphQL;
using GraphQL.DataLoader;
using GraphQL.Instrumentation;
using GraphQL.Server.Transports.AspNetCore;
using GraphQL.SystemTextJson;
using GraphQL.Transport;
using GraphQL.Types;
using GraphQL.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TypiQL.Models;

namespace DataCrush.TypiQL.Models
{
    public class TypiQLMiddleware<TSchema> : GraphQLHttpMiddleware<TSchema> where TSchema : ISchema
    {
        private readonly BaseSchema _baseSchema;
        private ISchema _schema;
        private readonly ConfigData _data;
        private readonly IDocumentExecuter _executer;
        private readonly RequestDelegate _next;
        private readonly DataLoaderDocumentListener _listener;

        public TypiQLMiddleware(
            ISchema schema,
            IServiceScopeFactory serviceScopeFactory,
            BaseSchema baseSchema,
            ConfigData data,
            RequestDelegate next,
            IGraphQLTextSerializer serializer, 
            IDocumentExecuter<TSchema> documentExecuter, 
            GraphQLHttpMiddlewareOptions options, 
            IHostApplicationLifetime hostApplicationLifetime,            
            DataLoaderDocumentListener listener
        ) : base(next, serializer, documentExecuter, serviceScopeFactory, options, hostApplicationLifetime)
        {
            _baseSchema = baseSchema;
            _schema = schema;
            _data = data;
            _executer = documentExecuter;
            _next = next;
            _listener = listener;
        }

        public override async Task InvokeAsync(HttpContext context)
        {
            if (string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var request = await JsonSerializer
                    .DeserializeAsync<GraphQLRequest>(
                        context.Request.Body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                var result = await _executer
                    .ExecuteAsync(doc =>
                    {
                        doc.Schema = _schema;
                        doc.Query = request.Query;
                        doc.Variables = request.Variables;
                        doc.Listeners.Add(_listener);
                    }).ConfigureAwait(false);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = 200;

                //await _writer.WriteAsync(context.Response.Body, result);
            }
            else
            {
                await _next(context);
            }
        }
        //protected override CancellationToken GetCancellationToken(HttpContext context)
        //{
        //    try
        //    {
        //        //if (_data._live)
        //        //{
        //        //    _schema = new OrgSchema(_provider);
        //        //}
        //        //else
        //        //{
        //        ((OrgSchema)_schema).ReloadTypeDict();
        //        //}
        //    }
        //    catch (Exception e)
        //    {
        //        //Log.Error("{ErrorMessage} {Source} {StackTrace}", e.Message, e.Source, e.StackTrace);
        //        _schema = _baseSchema;
        //    }
        //    return base.GetCancellationToken(context);
        //}
        //protected override Task RequestExecutedAsync(in GraphQLRequestExecutionResult requestExecutionResult)
        //{

        //    //if (requestExecutionResult.Result.Errors != null)
        //    //{
        //    //    foreach (var e in requestExecutionResult.Result.Errors)
        //    //        Log.Error("{ErrorMessage} {Source} {StackTrace}", e.Message, e.Source, e.StackTrace);
        //    //}

        //    return base.RequestExecutedAsync(requestExecutionResult);
        //}

        //protected override CancellationToken GetCancellationToken(HttpContext context)
        //{
        //    // custom CancellationToken example 
        //    var cts = CancellationTokenSource.CreateLinkedTokenSource(base.GetCancellationToken(context), new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        //    return cts.Token;
        //}
    }
    public class GraphQLMiddleware : IMiddleware
    {
        private readonly GraphQLSettings _settings;
        private readonly IDocumentExecuter<ISchema> _executer;
        private readonly IGraphQLSerializer _serializer;

        public GraphQLMiddleware(
            GraphQLSettings settings,
            IDocumentExecuter<ISchema> executer,
            IGraphQLSerializer serializer)
        {
            _settings = settings;
            _executer = executer;
            _serializer = serializer;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (!IsGraphQLRequest(context))
            {
                await next(context);
                return;
            }

            await ExecuteAsync(context);
        }

        private bool IsGraphQLRequest(HttpContext context)
        {
            return context.Request.Path.StartsWithSegments(_settings.Path)
                && string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ExecuteAsync(HttpContext context)
        {
            var request = await _serializer.ReadAsync<GraphQLRequest>(context.Request.Body, context.RequestAborted);

            var start = DateTime.UtcNow;

            var result = await _executer.ExecuteAsync(_ =>
            {
                _.Query = request?.Query;
                _.OperationName = request?.OperationName;
                _.Variables = request?.Variables;
                _.UserContext = _settings.BuildUserContext?.Invoke(context);
                _.EnableMetrics = _settings.EnableMetrics;
                _.RequestServices = context.RequestServices;
                _.CancellationToken = context.RequestAborted;
            });

            if (_settings.EnableMetrics)
            {
                result.EnrichWithApolloTracing(start);
            }

            await WriteResponseAsync(context, result);
        }

        private async Task WriteResponseAsync(HttpContext context, ExecutionResult result)
        {
            context.Response.ContentType = "application/graphql+json";
            context.Response.StatusCode = result.Executed ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;

            await _serializer.WriteAsync(context.Response.Body, result, context.RequestAborted);
        }
    }
    //public class GraphQLMiddleware
    //{
    //    private readonly RequestDelegate _next;
    //    private readonly GraphQLSettings _settings;
    //    private readonly IDocumentExecuter _executer;
    //    private readonly IDocumentWriter _writer;
    //    private readonly DataLoaderDocumentListener _listener;

    //    public GraphQLMiddleware(
    //        RequestDelegate next,
    //        GraphQLSettings settings,
    //        IDocumentExecuter executer,
    //        IDocumentWriter writer,
    //        DataLoaderDocumentListener listener
    //        )
    //    {
    //        _next = next;
    //        _settings = settings;
    //        _executer = executer;
    //        _writer = writer;
    //        _listener = listener;
    //    }

    //    public async Task Invoke(HttpContext context, ISchema schema)
    //    {
    //        if (!IsGraphQLRequest(context))
    //        {
    //            await _next(context);
    //            return;
    //        }

    //        await ExecuteAsync(context, schema);
    //    }

    //    private bool IsGraphQLRequest(HttpContext context)
    //    {
    //        return context.Request.Path.StartsWithSegments(_settings.Path)
    //            && string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase);
    //    }

    //    private async Task ExecuteAsync(HttpContext context, ISchema schema)
    //    {
    //        var request = await JsonSerializer
    //                .DeserializeAsync<GraphQLRequest>(
    //                    context.Request.Body,
    //                    new JsonSerializerOptions
    //                    {
    //                        PropertyNameCaseInsensitive = true
    //                    });

    //        var result = await _executer.ExecuteAsync(_ =>
    //        {
    //            _.Schema = schema;
    //            _.Query = request.Query;
    //            _.OperationName = request.OperationName;
    //            _.Inputs = request.Variables.ToInputs();
    //            _.UserContext = _settings.BuildUserContext?.Invoke(context);
    //            _.EnableMetrics = _settings.EnableMetrics;
    //            _.Listeners.Add(_listener);
    //            if (_settings.EnableMetrics)
    //            {
    //                _.FieldMiddleware.Use<InstrumentFieldsMiddleware>();
    //            }
    //        });

    //        await WriteResponseAsync(context, result);
    //    }

    //    private async Task WriteResponseAsync(HttpContext context, ExecutionResult result)
    //    {
    //        context.Response.ContentType = "application/json";
    //        context.Response.StatusCode = result.Errors?.Any() == true ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.OK;

    //        await _writer.WriteAsync(context.Response.Body, result);
    //    }
    //}
    //public class GraphQLRequest
    //{
    //    public string OperationName { get; set; }

    //    public string Query { get; set; }

    //    [JsonConverter(typeof(ObjectDictionaryConverter))]
    //    public Dictionary<string, object> Variables
    //    {
    //        get; set;
    //    }
    //}
}
