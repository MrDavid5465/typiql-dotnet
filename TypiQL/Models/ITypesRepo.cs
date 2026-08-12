using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DataCrush.TypiQL.Models
{
    public interface ITypesRepo
    {
        ConcurrentStack<Types> AllTypes { get; }
        Types AddTypes(Types type);
        IObservable<Types> Types();
        IObservable<List<Types>> TypesGetAll();
    }
    public class TypesRepo : ITypesRepo
    {
        private readonly ISubject<Types> _typesStream = new ReplaySubject<Types>(1);
        private readonly ISubject<List<Types>> _allTypesStream = new ReplaySubject<List<Types>>(1);
        public ConcurrentStack<Types> AllTypes {get; }
        public TypesRepo()
        {
            AllTypes = new ConcurrentStack<Types>();
        }
        public Types AddTypes(Types type)
        {
            AllTypes.Push(type);
            _typesStream.OnNext(type);
            return type;
        }
        public IObservable<Types> Types()
        {
            return _typesStream.AsObservable();
        }
        public void AddError(Exception exception)
        {
            _typesStream.OnError(exception);
        }
        public IObservable<List<Types>> TypesGetAll()
        {
            return _allTypesStream.AsObservable();
        }
    }
}
