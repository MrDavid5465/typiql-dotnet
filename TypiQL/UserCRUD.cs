using DataCrush.TypiQL.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace DataCrush.TypiQL
{
    public class UserCRUD
    {
        public string UserNameProperty { get; set; }
        public Func<string, Dictionary<string, dynamic>> GetUser { get; set; }
        public Func<List<Dictionary<string, dynamic>>> ListUsers { get; set; }
        //public List<CustomResolver> Queries { get; set; }
        //public List<CustomResolver> Mutations { get; set; }
        //public List<CustomResolver> Fields { get; set; }
    }
}
