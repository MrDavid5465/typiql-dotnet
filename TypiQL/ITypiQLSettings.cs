using DataCrush.TypiQL.Models;
using GraphQL;
using GraphQL.Resolvers;
using System;
using System.Collections.Generic;
using System.Text;

namespace DataCrush.TypiQL
{
    public interface ITypiQLSettings
    {
        string TypiQLConnectionString { get; set; }
        string TypiQLDatabase { get; set; }
        string TypiQLAdminRole { get; set; }
        List<TypiQLRole> Roles { get; set; }
        List<CustomResolver> Resolvers { get; set; }
        UserCRUD UserCRUD { get; set; }
        Func<LoggingContext,dynamic> Logger { get; set; }
    }
}
