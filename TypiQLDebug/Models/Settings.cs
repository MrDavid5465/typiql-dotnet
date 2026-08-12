using DataCrush.TypiQL;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using TypiQLDebug.Models.Mongo;
using TypiQLDebug.Models.Mongo.Types;

namespace TypiQLDebug.Models
{
    public class Settings
    {
        public string TypiQLConnectionString { get; set; }
        public string TypiQLDatabase { get; set; }
        public string Secret { get; set; }
        public string ConnectionString { get; set; }
        public string Database { get; set; }
        public string TypiQLAdminRole { get; set; }
        public List<CustomResolver> Resolvers { get; set; }
        public string UserNameProperty { get; set; }
        public List<TypiQLRole> Roles { get; set; }
        public void GetRoles()
        {
            MongoContext mongoContext = new MongoContext(ConnectionString, Database);
            List<TypiQLRole> roles = new List<TypiQLRole>
            {
                new TypiQLRole("Admin", "Admin"),
                new TypiQLRole("otherRole", "otherRole")
            };
            List<GroupADMock> groups = mongoContext.GroupsADMock.Find(_ => true).ToListAsync().Result;

            foreach (GroupADMock group in groups)
            {
                roles.Add(new TypiQLRole(group.sAMAccountName, group.sAMAccountName));
            };
            Roles = roles;
        }


    }
}