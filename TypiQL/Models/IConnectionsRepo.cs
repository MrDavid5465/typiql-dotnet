using DataCrush.TypiQL.Models.Mongo;
using GraphQL.Resolvers;
using GraphQL.Types;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DataCrush.TypiQL.Models
{
    public interface IConnectionsRepo
    {
        //ConcurrentStack<Connection> AllConnections { get; }
        Connection AddConnection(Connection connection);
        Connection UpdateConnection(Connection connection);
        Connection RemoveConnection(Connection connection);
        //IObservable<ChangeStreamDocument<Connection>> Connection();
        IObservable<Subscriber<Connection>> Connection();
        IObservable<List<Connection>> ConnectionsGetAll();
    }
    public class ConnectionsRepo : IConnectionsRepo
    {
        private readonly ISubject<Subscriber<Connection>> _connectionsStream = new Subject<Subscriber<Connection>>();
        private readonly ISubject<List<Connection>> _allConnectionsStream = new ReplaySubject<List<Connection>>(1);
        //public ConcurrentStack<Connection> AllConnections { get; }
        private TypiQLMongoContext _context;
        private readonly TypiQLSettings _settings;
        public ConnectionsRepo(TypiQLSettings settings, TypiQLMongoContext context)
        {
            //AllConnections = new ConcurrentStack<Connection>();
            _context = context;
            _settings = settings;
        }
        public Connection AddConnection(Connection connection)
        {
            //AllConnections.Push(connection);
            _connectionsStream.OnNext(new Subscriber<Connection>() { OperationName = "Add", Value = connection });
            return connection;
        }
        public Connection UpdateConnection(Connection connection)
        {
            //AllConnections.Push(connection);
            _connectionsStream.OnNext(new Subscriber<Connection>() { OperationName = "Update", Value = connection });
            return connection;
        }
        public Connection RemoveConnection(Connection connection)
        {
            //AllConnections.Push(connection);
            _connectionsStream.OnNext(new Subscriber<Connection>() { OperationName = "Remove", Value = connection });
            return connection;
        }
        //public IObservable<ChangeStreamDocument<Connection>> Connection()
        //{
        //    //var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<Connection>>().Match("{operationType: {$eq: 'insert'}}");
        //    MongoContext myMC = new MongoContext(_settings);
        //    ChangeStreamOptions options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup };
        //    using (var changeStream = myMC.Connections.Watch(options)) //pipeline);
        //    {
        //        while (true)
        //        {
        //            changeStream.MoveNext();
        //            if (changeStream.Current.Count() != 0)
        //            {
        //                var result = changeStream.Current.
        //                return result;
        //            }
        //        }
        //    }
        //}
        public IObservable<Subscriber<Connection>> Connection()
        {
            return _connectionsStream.AsObservable();
        }
        public void AddError(Exception exception)
        {
            _connectionsStream.OnError(exception);
        }
        public IObservable<List<Connection>> ConnectionsGetAll()
        {
            return _allConnectionsStream.AsObservable();
        }
    }
    
}
