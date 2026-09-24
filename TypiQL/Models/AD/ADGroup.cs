using GraphQL.Types;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices;

namespace DataCrush.TypiQL.Models.AD
{
    public class ADGroup
    {
        public string SAMAccountName { get; set; }
        public string DistinguishedName { get; set; }
        public string Name { get; set; }
        public List<string> Members { get; set; }
        public string DisplayName { get { return Name; } }

        public ADGroup()
        {

        }
        public ADGroup(DirectoryEntry group)
        {
            if (group.Properties["sAMAccountName"].Value != null)
                SAMAccountName = group.Properties["sAMAccountName"].Value.ToString();
            if (group.Properties["DistinguishedName"].Value != null)
                DistinguishedName = group.Properties["DistinguishedName"].Value.ToString();
            if (group.Properties["Name"].Value != null)
                Name = group.Properties["Name"].Value.ToString();
            List<string> users = new List<string>();
            foreach (string u in group.Properties["Member"])
            {
                users.Add(u);
            }
            Members = users;
        }
        public ADGroup(SearchResult group)
        {
            IEnumerator samAccountName = group.Properties["sAMAccountName"].GetEnumerator();
            IEnumerator name = group.Properties["Name"].GetEnumerator();
            IEnumerator distinguishedName = group.Properties["DistinguishedName"].GetEnumerator();
            IEnumerator members = group.Properties["member"].GetEnumerator();

            if (samAccountName.MoveNext())
                SAMAccountName = samAccountName.Current.ToString();
            if (name.MoveNext())
                Name = name.Current.ToString();
            if (distinguishedName.MoveNext())
                DistinguishedName = distinguishedName.Current.ToString();

            if (members.MoveNext())
            {
                List<string> users = new List<string>();
                do
                {
                    users.Add((string)members.Current);
                } while (members.MoveNext());
                Members = users;
            }

        }
    }
    public class ADGroupType : ObjectGraphType<ADGroup>
    {
        public ADGroupType(ConfigData data)
        {
            Name = "TypiQLGroup";
            Field<IdGraphType>("id", resolve: context => context.Source.SAMAccountName);
            Field<StringGraphType>("sAMAccountName", resolve: context => context.Source.SAMAccountName);
            Field<StringGraphType>("Name", resolve: context => context.Source.Name);
            Field<StringGraphType>("DistinguishedName", resolve: context => context.Source.DistinguishedName);
        }
    }
}
