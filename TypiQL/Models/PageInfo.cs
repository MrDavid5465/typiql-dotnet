using GraphQL.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace TypiQL.Models
{
    public class PageInfo
    {
        public int TotalResults { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
    }

    public class PageInfoType : ObjectGraphType<PageInfo>
    {
        public PageInfoType()
        {
            Name = "PageInfo";
            Field<IntGraphType>("totalResults", resolve: context => context.Source.TotalResults);
            Field<IntGraphType>("page", resolve: context => context.Source.Page);
            Field<IntGraphType>("totalPages", resolve: context => context.Source.TotalPages);
        }
    }
}
