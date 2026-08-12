using GraphQL.Types;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DataCrush.TypiQL.Models
{
    public class FileUpload
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; }
        // public Stream Stream { get; set; }
        public string File { get; set; }
    }
    public class FileUploadType : ObjectGraphType<FileUpload>
    {
        public FileUploadType()
        {
            Name = "File";
            Field<IdGraphType>("id", resolve: context => context.Source.Id);
            Field<StringGraphType>("name", resolve: context => context.Source.Name);
            Field<StringGraphType>("file", resolve: context => context.Source.File);
        }
    }
}
