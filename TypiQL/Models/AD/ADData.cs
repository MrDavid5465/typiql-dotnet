using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Principal;
using Microsoft.AspNetCore.Http.Extensions;

namespace DataCrush.TypiQL.Models.AD
{
    public class ADData
    {
        public Dictionary<string, Connection> _connections;
        private readonly ConfigData _data;
        private readonly SchemaHelpers _helpers;
        public ADData(ConfigData data, SchemaHelpers helpers)
        {
            _data = data;
            _helpers = helpers;
            
            var connections = _data.GetConnections("ad").Result;
            _connections = new Dictionary<string, Connection>();
            foreach (Connection c in connections)
            {
                _connections.Add(c.Id.ToString(), c);
            }
        }

        public string BuildFilter(string type, ref string sort, ref int limit, ref int start, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            Model model = t.Model;
            var parameters = new List<string>();
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            string query;
            bool getAll = true;
            foreach (KeyValuePair<string, dynamic> key in keys)
            {
                if (key.Value == null || key.Value is string && key.Value == "" || key.Value is List<dynamic> && key.Value.Count == 0)
                {
                    getAll = false;
                }
                else if (key.Key == "_orderBy" && key.Value != null)
                {
                    sort = key.Value;
                }
                else if (key.Key == "_start" && key.Value != null)
                {
                    start = int.Parse(key.Value);
                }
                else if (key.Key == "_limit" && key.Value != null)
                {
                    limit = int.Parse(key.Value);
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "startsWith")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}={key.Value}*)");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "endsWith")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}=*{key.Value})");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notStartsWith")
                {
                    parameters.Add($"(!({model.Fields[key.Key.Split("_")[0]].DataName}={key.Value}*))");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notEndsWith")
                {
                    parameters.Add($"(!({model.Fields[key.Key.Split("_")[0]].DataName}=*{key.Value}))");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "contains")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}=*{key.Value}*)");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notContains")
                {
                    parameters.Add($"(!({model.Fields[key.Key.Split("_")[0]].DataName}=*{key.Value}*))");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lte")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}<={key.Value})");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "lt")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}<{key.Value})");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gte")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}>={key.Value})");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "gt")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}>{key.Value})");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "in")
                {
                    List<string> inVals = new List<string>();
                    if (!(key.Value is string) && key.Value is IEnumerable)
                    {
                        foreach (dynamic v in key.Value)
                        {
                            inVals.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}={v})");
                        }
                    }
                    else 
                    {
                        inVals.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}={key.Value})");
                    }                    
                    if (inVals.Count > 0) {
                        parameters.Add($"(|{string.Join("", inVals)})");
                    }
                    
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "notIn")
                {
                    
                    List<string> inVals = new List<string>();
                    if (!(key.Value is string) && key.Value is IEnumerable)
                    {
                        foreach (dynamic v in key.Value)
                        {
                            inVals.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}={v})");
                        }
                    }
                    else
                    {
                        inVals.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}={key.Value})");
                    }                    
                    if (inVals.Count > 0)
                    {
                        parameters.Add($"(!(|{string.Join("", inVals)}))");
                    }
                    
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "anyEq")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}>={key.Value})");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "anyNe")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}>{key.Value})");
                }
                else if (key.Key.Split("_").Length > 1 && key.Key.Split("_")[1] == "not")
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}!={key.Value})");
                }
                else if (key.Value != null)
                {
                    parameters.Add($"({model.Fields[key.Key.Split("_")[0]].DataName}={key.Value})");
                }
            }            
            query = parameters.Count > 0 ? $"(&({string.Join("", parameters)}))" : getAll ? model.Name : "";
            return query;
        }
        public DirectorySearcher createDirectorySearcher(Types type)
        {
            List<string> propertiesToLoad = new List<string>();
            foreach (Column c in type.Model.Columns)
            {
                propertiesToLoad.Add(c.DataName);
            }
            DirectorySearcher searcher = new DirectorySearcher(_connections[type.Connection].ConnectionString);
            searcher.PropertiesToLoad.AddRange(propertiesToLoad.ToArray());
            return searcher;
        }
        public Dictionary<string, dynamic> ResolveSearchResult(SearchResult searchResult, Types type)
        {
            if (searchResult == null)
            {
                return null;
            }
            List<string> properties = new List<string>();
            foreach (Column c in type.Model.Columns)
            {
                properties.Add(c.DataName);
            }
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            foreach (var propName in properties)
            {
                IEnumerator property = searchResult.Properties[propName].GetEnumerator();

                if (property.MoveNext())
                {
                    List<dynamic> values = new List<dynamic>();
                    do
                    {
                        values.Add(property.Current);
                    } while (property.MoveNext());
                    if (propName.ToLower() == "objectsid")
                    {
                        result.Add(propName, new SecurityIdentifier(values[0], 0).ToString());
                    }
                    else if (values.Count == 1)
                    {
                        result.Add(propName, values[0]);
                    }
                    else
                    {
                        result.Add(propName, values);
                    }
                }
            }
            return result;
        }

        public List<Dictionary<string, dynamic>> GetADObjects(string type)
        {
            DirectorySearcher searcher = createDirectorySearcher(_data.typeDict[type]);
            searcher.Filter = _data.typeDict[type].Model.Name;
            List<Dictionary<string, dynamic>> users = new List<Dictionary<string, dynamic>>();
            foreach (SearchResult s in searcher.FindAll())
            {
                users.Add(ResolveSearchResult(s, _data.typeDict[type]));
            }
            return users;
        }

        public List<dynamic> GetADObjects(string type, Dictionary<string, dynamic> keys)
        {
            DirectorySearcher searcher = createDirectorySearcher(_data.typeDict[type]);

            int limit = 1000;
            int start = 0;
            string sort = "";
            
            searcher.Filter = BuildFilter(type, ref sort, ref limit, ref start, keys);
            searcher.Sort = new SortOption(sort, (keys.ContainsKey("_orderBy_desc") && keys["_orderBy_desc"] != null ? SortDirection.Descending : SortDirection.Ascending));
            if (searcher.Filter == "(objectClass=*)")
            {
                return new List<dynamic>();
            }
            List<dynamic> users = new List<dynamic>();
            foreach (SearchResult s in searcher.FindAll())
            {
                users.Add(ResolveSearchResult(s, _data.typeDict[type]));
            }
            var result = users.Skip(start).Take(limit).ToList();
            return result;
        }
        public Dictionary<string, dynamic> GetADObject(string type, Dictionary<string, dynamic> keys)
        {
            DirectorySearcher searcher = createDirectorySearcher(_data.typeDict[type]);
            int limit = 0;
            int start = 0;
            string sort = "";
            searcher.Filter = BuildFilter(type, ref sort, ref limit, ref start, keys);
            if (searcher.Filter == "(objectClass=*)")
            {
                return null;
            }
            return ResolveSearchResult(searcher.FindOne(), _data.typeDict[type]);
        }
        public Dictionary<string, dynamic> UpdateADObject(string type, Dictionary<string, dynamic> keys, Dictionary<string, dynamic> update)
        {
            Types t = _data.typeDict[type];
            DirectorySearcher searcher = createDirectorySearcher(t);
            int limit = 0;
            int start = 0;
            string sort = "";
            searcher.Filter = BuildFilter(type, ref sort, ref limit, ref start, keys);
            if (searcher.Filter == "(objectClass=*)")
            {
                return new Dictionary<string, dynamic>();
            }
            DirectoryEntry adObject = searcher.FindOne().GetDirectoryEntry();
            foreach (KeyValuePair<string, dynamic> field in update)
            {
                if (field.Key == "thumbnailPhoto")
                {
                    adObject.Properties[field.Key].Clear();
                    if (field.Value != "")
                    {
                        byte[] bytes = Convert.FromBase64String(field.Value);
                        Image image;
                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            image = Image.FromStream(ms);
                            if (image.Height > 512)
                            {
                                image = new Bitmap(image, new Size(512, 512));
                            }
                        }
                        MemoryStream stream = new MemoryStream();
                        image.Save(stream, ImageFormat.Jpeg);
                        byte[] bytesFromStream = stream.ToArray();
                        adObject.Properties[field.Key].Add(bytesFromStream);
                    }
                }
                else
                {
                    adObject.Properties[field.Key].Value = field.Value == "" ? null : field.Value;
                }

            }
            adObject.CommitChanges();
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Update", ResolveSearchResult(searcher.FindOne(), _data.typeDict[type]));
        }
        public Dictionary<string, dynamic> AddADObject(string type, Dictionary<string, dynamic> values)
        {
            Types t = _data.typeDict[type];
            if (values.Count == 0)
            {
                return new Dictionary<string, dynamic>();
            }
            DirectoryEntry adObject = new DirectoryEntry(_connections[t.Connection].ConnectionString);
            foreach (KeyValuePair<string, dynamic> field in values)
            {
                if (field.Key == "thumbnailPhoto")
                {
                    adObject.Properties[field.Key].Clear();
                    if (field.Value != "")
                    {
                        byte[] bytes = Convert.FromBase64String(field.Value);
                        Image image;
                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            image = Image.FromStream(ms);
                            if (image.Height > 512)
                            {
                                image = new Bitmap(image, new Size(512, 512));
                            }
                        }
                        MemoryStream stream = new MemoryStream();
                        image.Save(stream, ImageFormat.Jpeg);
                        byte[] bytesFromStream = stream.ToArray();
                        adObject.Properties[field.Key].Add(bytesFromStream);
                    }
                }
                else
                {
                    adObject.Properties[field.Key].Value = field.Value == "" ? null : field.Value;
                }

            }
            adObject.CommitChanges();
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Add", values);
        }
        public Dictionary<string, dynamic> RemoveADObject(string type, Dictionary<string, dynamic> keys)
        {
            Types t = _data.typeDict[type];
            DirectorySearcher searcher = createDirectorySearcher(t);
            int limit = 0;
            int start = 0;
            string sort = "";
            searcher.Filter = BuildFilter(type, ref sort, ref limit, ref start, keys);
            if (searcher.Filter == "(objectClass=*)")
            {
                return new Dictionary<string, dynamic>();
            }
            DirectoryEntry directoryEntry = searcher.FindOne().GetDirectoryEntry();
            Dictionary<string, dynamic> result = ResolveSearchResult(searcher.FindOne(), _data.typeDict[type]);
            directoryEntry.DeleteTree();
            return _data._subscriptionRepos[t.Name].ChangeEntity(t, "Remove", result);
        }
    }
}
