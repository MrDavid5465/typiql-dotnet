using GraphQL.Server.Transports.AspNetCore;
using GraphQL.Server.Transports.AspNetCore.Common;
using GraphQL.Types;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DataCrush.TypiQL.Models
{
    public class TypiQLMiddleware<TSchema> : GraphQLHttpMiddleware<TSchema> where TSchema : ISchema
    {
        private readonly BaseSchema _baseSchema;
        private ISchema _schema;
        private readonly ConfigData _data;

        public TypiQLMiddleware(ISchema schema, BaseSchema baseSchema, ConfigData data, RequestDelegate next,
            PathString path, IGraphQLRequestDeserializer requestDeserializer) : base(next, path, requestDeserializer)
        {
            _baseSchema = baseSchema;
            _schema = schema;
            _data = data;
        }
        protected override CancellationToken GetCancellationToken(HttpContext context)
        {
            try
            {
                //if (_data._live)
                //{
                //    _schema = new OrgSchema(_provider);
                //}
                //else
                //{
                ((OrgSchema)_schema).ReloadTypeDict();
                //}
            }
            catch (Exception e)
            {
                //Log.Error("{ErrorMessage} {Source} {StackTrace}", e.Message, e.Source, e.StackTrace);
                _schema = _baseSchema;
            }
            return base.GetCancellationToken(context);
        }
        protected override Task RequestExecutedAsync(in GraphQLRequestExecutionResult requestExecutionResult)
        {
            
            //if (requestExecutionResult.Result.Errors != null)
            //{
            //    foreach (var e in requestExecutionResult.Result.Errors)
            //        Log.Error("{ErrorMessage} {Source} {StackTrace}", e.Message, e.Source, e.StackTrace);
            //}

            return base.RequestExecutedAsync(requestExecutionResult);
        }

        //protected override CancellationToken GetCancellationToken(HttpContext context)
        //{
        //    // custom CancellationToken example 
        //    var cts = CancellationTokenSource.CreateLinkedTokenSource(base.GetCancellationToken(context), new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        //    return cts.Token;
        //}
    }
}
