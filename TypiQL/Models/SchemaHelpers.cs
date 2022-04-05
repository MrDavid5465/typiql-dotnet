using DataCrush.TypiQL;
using DataCrush.TypiQL.Models;
using DataCrush.TypiQL.Models.AD;
using DataCrush.TypiQL.Models.Mongo;
using DataCrush.TypiQL.Models.Sql;
using GraphQL;
using GraphQL.DataLoader;
using GraphQL.Types;
using GraphQL.Utilities;
using LinqKit;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DataCrush.TypiQL.Models
{
    public class SchemaHelpers
    {
        private MongoData _mongoData;
        private SqlData _sqlData;
        private ADData _aDData;
        private readonly IDataLoaderContextAccessor _dataLoader;
        private readonly IHttpContextAccessor _httpContext;
        private readonly TypiQLSettings _settings;
        private readonly TypiQLMongoContext _mongoContext;
        public readonly ConfigData _data;
        

        public SchemaHelpers(
            TypiQLSettings settings,
            ConfigData data,
            TypiQLMongoContext mongoContext, 
            IDataLoaderContextAccessor dataLoader, 
            IHttpContextAccessor httpContext)
        {            
            _dataLoader = dataLoader;
            _httpContext = httpContext;
            _settings = settings;
            _mongoContext = mongoContext;
            _data = data;
        }

        public void Configure(MongoData mongoData, SqlData sqlData, ADData aDData)
        {
            _mongoData = mongoData;
            _sqlData = sqlData;
            _aDData = aDData;
        }
            

        public bool IsVariable(string value)
        {
            if (value.StartsWith("@"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public dynamic ResolveVariable(string variable, Dictionary<string, dynamic> arguments = null)
        {
            if (variable.StartsWith("@currentUser") || variable.StartsWith("@user"))
            {
                return ResolveUser(variable, arguments);
            }
            else if (variable.StartsWith("@dName"))
            {
                return ResolveDistinguishedName(variable, arguments);
            }
            else if (variable == "@now")
            {
                return DateTime.UtcNow;
            }
            else if (variable.StartsWith("@today"))
            {
                return DateTime.Today;
            }
            else if (variable.StartsWith("@yesterday"))
            {
                return DateTime.Today.AddDays(-1);
            }
            else if (variable.StartsWith("@tomorrow"))
            {
                return DateTime.Today.AddDays(1);
            }
            else if (variable.StartsWith("@addToArray") || variable.StartsWith("@removeFromArray"))
            {
                return ResolveArray(variable, arguments);
            }
            else return null;
        }
        public dynamic ResolveArray(string variable, Dictionary<string, dynamic> arguments = null)
        {
            List<string> args = Regex.Match(variable.Split(".")[0], "(?<=\\()(.*?)(?=\\))").Value.Split("','").ToList();

            return null;
        }
        public dynamic ResolveDistinguishedName(string variable, Dictionary<string, dynamic> arguments = null)
        {
            List<string> distinguishedNames = new List<string>();
            if (arguments == null)
            {
                distinguishedNames = Regex.Match(variable.Split(".")[0], "(?<=\\(')(.*?)(?='\\))").Value.Split("','").ToList();
            }
            else
            {
                foreach (string arg in Regex.Match(variable.Split(".")[0], "(?<=\\()(.*?)(?=\\))").Value.Split("','").ToList())
                {
                    if (new string[] { "memberOf", "member", "distinguishedName", "manager", "directReports" }.Contains(arg))
                    {
                        if (arguments[arg] is string)
                        {
                            distinguishedNames.Add(((string)arguments[arg]).Trim());
                        }
                        else
                        {
                            foreach (string dn in arguments[arg])
                            {
                                distinguishedNames.Add(dn.Trim());
                            }
                        }

                    }
                }

            }
            string field = variable.Split(".")[1].Contains("(") ? "sAMAccountName" : variable.Split(".")[1];

            Dictionary<string, dynamic> keys = new Dictionary<string, dynamic>();
            dynamic result;
            keys.Add("distinguishedName_in", distinguishedNames);
            if (distinguishedNames.Count > 1)
            {
                result = new List<string>();
                foreach (Dictionary<string, dynamic> obj in GetMany(null, _data.typeDict["Group"], keys))
                {
                    if (obj.ContainsKey(field) && obj[field] != null)
                    {
                        result.Add(obj[field]);
                    }
                }
            }
            else
            {
                Dictionary<string, dynamic> obj = GetOne(null, _data.typeDict["Group"], keys);
                result = obj.ContainsKey(field) && obj[field] != null ? obj[field] : "";
            }
            return result;
        }
        public dynamic ResolveField(Types thisType, Column thisColumn, string fieldName, IDictionary<string, dynamic> obj, ResolvedType resolvedTypeInfo)
        {
            obj = new Dictionary<string, dynamic>(obj);
            if (resolvedTypeInfo.TypeStack.Contains("array")
                && _data.typeDict.ContainsKey(resolvedTypeInfo.Name)
                && thisColumn.Arguments.Count > 0)
            {
                return GetMany(null, _data.typeDict[resolvedTypeInfo.Name], BuildFilter(thisType, fieldName, obj as Dictionary<string, dynamic>));
            }
            else if (!resolvedTypeInfo.TypeStack.Contains("array")
                && _data.typeDict.ContainsKey(resolvedTypeInfo.Name)
                && thisColumn.Arguments.Count > 0)
            {
                return GetOne(null, _data.typeDict[resolvedTypeInfo.Name], BuildFilter(thisType, fieldName, obj as Dictionary<string, dynamic>));
            }
            else if (!obj.ContainsKey(thisColumn.DataName))
            {
                return null;
            }
            else
            {
                return obj[thisColumn.DataName];
            }
        }
        public List<Dictionary<string, dynamic>> GetManyValues(
            Types type,
            List<Argument> arguments,
            FieldType query,
            IResolveFieldContext context,
            Dictionary<string, dynamic> filter
        )
        {
            List<Dictionary<string, dynamic>> manyValues = new List<Dictionary<string, dynamic>>();
            List<Dictionary<string, dynamic>> manyAllowedValues = new List<Dictionary<string, dynamic>>();
            foreach (Argument arg in arguments)
            {
                if (arg.Key == "items" || arg.Key == "updates")
                {
                    if (arg.Value is string && arg.Value == arg.Name)
                    {
                        foreach (Dictionary<string, dynamic> values in context.GetArgument<List<Dictionary<string, dynamic>>>(arg.Name))
                        {
                            Dictionary<string, dynamic> valuesToAdd = new Dictionary<string, dynamic>();
                            foreach (KeyValuePair<string, dynamic> kv in values)
                            {
                                if (!valuesToAdd.ContainsKey(kv.Key))
                                {
                                    valuesToAdd.Add(kv.Key, kv.Value);
                                }
                            }
                            manyValues.Add(valuesToAdd);
                        }
                    }
                    else
                    {
                        foreach (Dictionary<string, dynamic> argVal in arg.Value)
                        {
                            Dictionary<string, dynamic> valuesToAdd = new Dictionary<string, dynamic>();
                            foreach (KeyValuePair<string, dynamic> kv in argVal)
                            {
                                var value = kv.Value;
                                if (context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    if (kv.Value is string && (string)kv.Value == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                    {
                                        value = context.GetArgument<dynamic>(arg.Name);
                                    }
                                    else if (kv.Value is string && ((string)kv.Value).Split(".")[0] == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                    {
                                        value = context.GetArgument<dynamic>(arg.Name)[((string)kv.Value).Split(".")[1]];
                                    }
                                    else if (kv.Value is string && ((string)kv.Value).StartsWith(arg.Name) && Regex.IsMatch(kv.Value, "(?<=\\()(.*?)(?=\\))"))
                                    {
                                        value = context.GetArgument<dynamic>(arg.Name);

                                        Dictionary<string, dynamic> defaultValues = BsonTypeMapper.MapToDotNetValue(
                                            BsonDocument.Parse(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value)
                                        );
                                        foreach (KeyValuePair<string, dynamic> dv in defaultValues)
                                        {
                                            if (!((Dictionary<string, dynamic>)value).ContainsKey(dv.Key))
                                            {
                                                if (IsVariable(dv.Value))
                                                {
                                                    ((Dictionary<string, dynamic>)value).Add(dv.Key, ResolveVariable(dv.Value));
                                                }
                                                else
                                                {
                                                    ((Dictionary<string, dynamic>)value).Add(dv.Key, dv.Value);
                                                }
                                            }
                                        }
                                        valuesToAdd.Add(kv.Key, value);
                                    }
                                    else if (!(kv.Value is string) &&
                                        ((List<dynamic>)kv.Value).Contains(arg.Name) &&
                                        context.GetArgument<dynamic>(arg.Name) != null)
                                    {
                                        value[((List<dynamic>)value).FindIndex(v => v == arg.Name)] = context.GetArgument<dynamic>(arg.Name);
                                    }
                                }
                                if (!valuesToAdd.ContainsKey(kv.Key))
                                {
                                    valuesToAdd.Add(kv.Key, value);
                                }
                            }
                            manyValues.Add(valuesToAdd);
                        }
                    }
                    foreach (QueryArgument queryArgument in query.Arguments)
                    {
                        if (queryArgument.Name == "items" || queryArgument.Name == "updates")
                        {
                            foreach (Dictionary<string, dynamic> valuesToAdd in context.GetArgument<List<dynamic>>(queryArgument.Name))
                            {
                                if (arguments.FindIndex(a => a.Name == queryArgument.Name) == -1)
                                {
                                    manyValues.Add(valuesToAdd);
                                }
                            }
                        }
                    }

                    foreach (Dictionary<string, dynamic> values in manyValues)
                    {
                        Dictionary<string, dynamic> allowedValues = new Dictionary<string, dynamic>();
                        Dictionary<string, dynamic> obj = GetOne(context, type, filter);
                        foreach (KeyValuePair<string, dynamic> kv in values)
                        {
                            bool changeAllowed = true;
                            if (type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups != null && type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups.Count > 0)
                            {
                                changeAllowed = false;
                                foreach (string group in type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups)
                                {
                                    if (_httpContext.HttpContext.User.IsInRole(group))
                                    {
                                        changeAllowed = true;
                                    }
                                }
                            }
                            if (changeAllowed)
                            {
                                if (kv.Value is string && type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(kv.Value))
                                {
                                    Argument a = type.MutationsDict[query.Name].ArgumentsDict[kv.Value];
                                    allowedValues.Add(kv.Key, context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name));
                                }
                                else if (kv.Value is string && type.Model.Fields.ContainsKey(kv.Value))
                                {
                                    Column a = type.Model.Fields[kv.Value];
                                    allowedValues.Add(kv.Key, obj[a.DataName]);
                                }
                                else if (kv.Value is string &&
                                    type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                                {
                                    Argument a = type.MutationsDict[query.Name].ArgumentsDict[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                                    if (IsVariable(kv.Value))
                                    {
                                        allowedValues.Add(
                                            kv.Key,
                                            ResolveVariable(
                                                kv.Value,
                                                Regex.Replace(
                                                    kv.Value,
                                                    "(?<=\\()(.*?)(?=\\))",
                                                    (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                                )
                                            )
                                        );
                                    }
                                    else
                                    {
                                        allowedValues.Add(
                                            kv.Key,
                                            Regex.Replace(
                                                kv.Value,
                                                "(?<=\\()(.*?)(?=\\))",
                                                (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                            )
                                        );
                                    }
                                }
                                else if (kv.Value is string && type.Model.Fields.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                                {
                                    Column a = type.Model.Fields[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                                    if (IsVariable(kv.Value))
                                    {
                                        allowedValues.Add(kv.Key, ResolveVariable(kv.Value, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName])));
                                    }
                                    else
                                    {
                                        allowedValues.Add(kv.Key, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName]));
                                    }
                                }
                                else if (kv.Value is string && IsVariable(kv.Value))
                                {
                                    allowedValues.Add(kv.Key, ResolveVariable(kv.Value));
                                }
                                else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTime")
                                {
                                    allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                                }
                                else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTimeOffset")
                                {
                                    allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                                }
                                else
                                {
                                    allowedValues.Add(kv.Key, kv.Value);
                                }
                            }
                        }
                        manyAllowedValues.Add(allowedValues);
                    }
                }
            }
            return manyAllowedValues;
        }
        public Dictionary<string, dynamic> GetValues(
            Types type,
            List<Argument> arguments,
            FieldType query,
            IResolveFieldContext context,
            Dictionary<string, dynamic> filter
        )
        {
            Dictionary<string, dynamic> values = new Dictionary<string, dynamic>();
            foreach (Argument arg in arguments)
            {
                if (arg.Key == "values" || arg.Key == "update")
                {
                    if (arg.Value is string && arg.Value == arg.Name)
                    {
                        foreach (KeyValuePair<string, dynamic> kv in context.GetArgument<dynamic>(arg.Name))
                        {
                            if (!values.ContainsKey(kv.Key))
                            {
                                values.Add(kv.Key, kv.Value);
                            }
                        }
                    }
                    else
                    {
                        foreach (KeyValuePair<string, dynamic> kv in arg.Value)
                        {
                            var value = kv.Value;
                            if (context.GetArgument<dynamic>(arg.Name) != null)
                            {
                                if (kv.Value is string && (string)kv.Value == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    value = context.GetArgument<dynamic>(arg.Name);
                                }
                                else if (kv.Value is string && ((string)kv.Value).Split(".")[0] == arg.Name && context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    value = context.GetArgument<dynamic>(arg.Name)[((string)kv.Value).Split(".")[1]];
                                }
                                else if (kv.Value is string && ((string)kv.Value).StartsWith(arg.Name) && Regex.IsMatch(kv.Value, "(?<=\\()(.*?)(?=\\))"))
                                {
                                    value = context.GetArgument<dynamic>(arg.Name);

                                    Dictionary<string, dynamic> defaultValues = BsonTypeMapper.MapToDotNetValue(
                                        BsonDocument.Parse(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value)
                                    );
                                    foreach (KeyValuePair<string, dynamic> dv in defaultValues)
                                    {
                                        if (!((Dictionary<string, dynamic>)value).ContainsKey(dv.Key))
                                        {
                                            if (IsVariable(dv.Value))
                                            {
                                                ((Dictionary<string, dynamic>)value).Add(dv.Key, ResolveVariable(dv.Value));
                                            }
                                            else
                                            {
                                                ((Dictionary<string, dynamic>)value).Add(dv.Key, dv.Value);
                                            }
                                        }
                                    }
                                    values.Add(kv.Key, value);
                                }
                                else if (!(kv.Value is string) &&
                                    ((List<dynamic>)kv.Value).Contains(arg.Name) &&
                                    context.GetArgument<dynamic>(arg.Name) != null)
                                {
                                    value[((List<dynamic>)value).FindIndex(v => v == arg.Name)] = context.GetArgument<dynamic>(arg.Name);
                                }
                            }
                            if (!values.ContainsKey(kv.Key))
                            {
                                values.Add(kv.Key, value);
                            }
                        }
                    }
                }
            }
            foreach (QueryArgument queryArgument in query.Arguments)
            {
                if (queryArgument.Name == "values" || queryArgument.Name == "update")
                {
                    var wat = context.Arguments[queryArgument.Name];//GetArgument<Dictionary<string, dynamic>>(queryArgument.Name);
                    var huh = context.GetArgument<dynamic>(queryArgument.Name);
                    foreach (KeyValuePair<string, dynamic> kv in context.GetArgument<dynamic>(queryArgument.Name))
                    {
                        if (!values.ContainsKey(kv.Key))
                        {
                            values.Add(kv.Key, kv.Value);
                        }
                        else
                        {
                            values[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            Dictionary<string, dynamic> allowedValues = new Dictionary<string, dynamic>();
            Dictionary<string, dynamic> obj = GetOne(context, type, filter);
            foreach (KeyValuePair<string, dynamic> kv in values)
            {
                bool changeAllowed = true;
                if (type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups != null && type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups.Count > 0)
                {
                    changeAllowed = false;
                    foreach (string group in type.Model.Fields[kv.Key.Split("_")[0]].AllowedGroups)
                    {
                        if (_httpContext.HttpContext.User.IsInRole(group))
                        {
                            changeAllowed = true;
                        }
                    }
                }
                if (changeAllowed)
                {
                    if (kv.Value is string && type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(kv.Value))
                    {
                        Argument a = type.MutationsDict[query.Name].ArgumentsDict[kv.Value];
                        allowedValues.Add(kv.Key, context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name));
                    }
                    else if (kv.Value is string && type.Model.Fields.ContainsKey(kv.Value))
                    {
                        Column a = type.Model.Fields[kv.Value];
                        allowedValues.Add(kv.Key, obj[a.DataName]);
                    }
                    else if (kv.Value is string &&
                        type.MutationsDict[query.Name].ArgumentsDict.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                    {
                        Argument a = type.MutationsDict[query.Name].ArgumentsDict[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                        if (IsVariable(kv.Value))
                        {
                            allowedValues.Add(
                                kv.Key,
                                ResolveVariable(
                                    kv.Value,
                                    Regex.Replace(
                                        kv.Value,
                                        "(?<=\\()(.*?)(?=\\))",
                                        (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                    )
                                )
                            );
                        }
                        else
                        {
                            allowedValues.Add(
                                kv.Key,
                                Regex.Replace(
                                    kv.Value,
                                    "(?<=\\()(.*?)(?=\\))",
                                    (string)context.GetArgument(Type.GetType($"System.{a.Type}", true, true), a.Name)
                                )
                            );
                        }
                    }
                    else if (kv.Value is string && type.Model.Fields.ContainsKey(Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value))
                    {
                        Column a = type.Model.Fields[Regex.Match(kv.Value, "(?<=\\()(.*?)(?=\\))").Value];
                        if (IsVariable(kv.Value))
                        {
                            allowedValues.Add(kv.Key, ResolveVariable(kv.Value, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName])));
                        }
                        else
                        {
                            allowedValues.Add(kv.Key, Regex.Replace(kv.Value, "(?<=\\()(.*?)(?=\\))", obj[a.DataName]));
                        }
                    }
                    else if (kv.Value is string && IsVariable(kv.Value))
                    {
                        allowedValues.Add(kv.Key, ResolveVariable(kv.Value));
                    }
                    else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTime")
                    {
                        allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                    }
                    else if (type.Model.Fields[kv.Key.Split("_")[0]].ColumnType == "DateTimeOffset")
                    {
                        allowedValues.Add(kv.Key, new DateTimeGraphType().ParseValue(kv.Value));
                    }
                    else
                    {
                        allowedValues.Add(kv.Key, kv.Value);
                    }
                }
            }
            return allowedValues;
        }
        public Dictionary<string, dynamic> BuildQueryFilter(Types type, List<Argument> arguments, FieldType query, IResolveFieldContext context)
        {
            Dictionary<string, Argument> args = new Dictionary<string, Argument>();
            Dictionary<string, dynamic> filter = new Dictionary<string, dynamic>();
            foreach (Argument arg in arguments)
            {
                args.Add(arg.Name, arg);
                if (!filter.ContainsKey(arg.Name) && arg.Key != "values" && arg.Key != "update" && arg.Key != "manyValues" && arg.Key != "updates" && !filter.ContainsKey(arg.Key))
                {
                    if (arg.Value is string)
                    {
                        if (arg.Value == arg.Name)
                        {
                            if (context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name) != null)
                            {
                                filter.Add(arg.Key, context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name));
                            }
                        }
                        else if (Regex.IsMatch(arg.Value, $"(?<=\\(){arg.Name}(?=\\))"))
                        {
                            if (context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name) != null)
                            {
                                if (IsVariable(arg.Value))
                                {
                                    filter.Add(arg.Key, ResolveVariable(Regex.Replace(arg.Value, $"(?<=\\(){arg.Name}(?=\\))", (string)context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name))));
                                }
                                else
                                {
                                    filter.Add(arg.Key, Regex.Replace(arg.Value, $"(?<=\\(){arg.Name}(?=\\))", (string)context.GetArgument(Type.GetType($"System.{arg.Type}", true, true), arg.Name)));
                                }
                            }
                        }
                        else if (IsVariable(arg.Value))
                        {
                            filter.Add(arg.Key, ResolveVariable(arg.Value));
                        }
                        else if (arg.Type == "DateTime")
                        {
                            filter.Add(arg.Key, new DateTimeGraphType().ParseValue(arg.Value));
                        }
                        else if (arg.Type == "DateTimeOffset")
                        {
                            filter.Add(arg.Key, new DateTimeOffsetGraphType().ParseValue(arg.Value));
                        }
                        else
                        {
                            filter.Add(arg.Key, arg.Value);
                        }
                    }
                    else
                    {
                        filter.Add(arg.Key, arg.Value);
                    }
                }
            }
            foreach (QueryArgument queryArgument in query.Arguments)
            {
                if (!filter.ContainsKey(queryArgument.Name))
                {
                    if (queryArgument.Name != "values" && queryArgument.Name != "update")
                    {
                        if (!args.ContainsKey(queryArgument.Name))
                        {
                            if (context.GetArgument<dynamic>(
                                    queryArgument.Name
                                ) != null)
                            {
                                filter.Add(
                                    queryArgument.Name,
                                    context.GetArgument<dynamic>(
                                        queryArgument.Name
                                    )
                                );
                            }
                        }
                    }
                }
            }
            return filter;
        }
        public Dictionary<string, dynamic> BuildFilter(Types thisType, string field, Dictionary<string, dynamic> obj)
        {
            Dictionary<string, dynamic> filters = new Dictionary<string, dynamic>();
            foreach (Argument arg in thisType.Model.Fields[field].Arguments)
            {
                if (arg.Value is string && thisType.Model.Fields.ContainsKey(arg.Value) && obj.ContainsKey(thisType.Model.Fields[arg.Value].DataName))
                {
                    if (obj[thisType.Model.Fields[arg.Value].DataName] != null)
                    {
                        filters.Add(arg.Key, obj[thisType.Model.Fields[arg.Value].DataName]);
                    }
                }
                else if (arg.Value is string && thisType.Model.Fields.ContainsKey(arg.Value) && !obj.ContainsKey(thisType.Model.Fields[arg.Value].DataName))
                {
                    switch (arg.Type)
                    {
                        case "List":
                            {
                                filters.Add(arg.Key, new List<dynamic>());
                                break;
                            }
                        case "Object":
                            {
                                filters.Add(arg.Key, new Dictionary<string, dynamic>());
                                break;
                            }
                        default:
                            {
                                filters.Add(arg.Key, "");
                                break;
                            }
                    }
                }
                else
                {
                    if (arg.Value is string)
                    {
                        if (arg.Value == arg.Name)
                        {
                            filters.Add(arg.Key, arg.Value);
                        }
                        else if (Regex.IsMatch(arg.Value, "(?<=\\()(.*?)(?=\\))"))
                        {
                            Dictionary<string, dynamic> arguments = new Dictionary<string, dynamic>();

                            foreach (string c in Regex.Match(arg.Value, "(?<=\\()(.*?)(?=\\))").Value.Split(","))
                            {
                                if (thisType.Model.Fields.ContainsKey(c) && obj[c] != null)
                                {
                                    arguments.Add(c, obj[thisType.Model.Fields[c].DataName]);
                                }
                                else if (thisType.Model.Fields.ContainsKey(c) && obj.Count == 0)
                                {
                                    arguments.Add(c, null);
                                }
                            }
                            filters.Add(arg.Key, ResolveVariable(arg.Value, arguments));
                        }
                        else if (IsVariable(arg.Value))
                        {
                            filters.Add(arg.Key, ResolveVariable(arg.Value, new Dictionary<string, dynamic>()));
                        }
                        else if (arg.Type == "DateTime")
                        {
                            filters.Add(arg.Key, new DateTimeGraphType().ParseValue(arg.Value));
                        }
                        else if (arg.Type == "DateTimeOffset")
                        {
                            filters.Add(arg.Key, new DateTimeOffsetGraphType().ParseValue(arg.Value));
                        }
                        else
                        {
                            filters.Add(arg.Key, arg.Value);
                        }
                    }
                    else
                    {
                        filters.Add(arg.Key, arg.Value);
                    }
                }
            }
            return filters;
        }
        public string BuildFilterFromResult(Types thisType, Types parentType, string parentField, Dictionary<string, dynamic> obj)
        {
            Column pf = parentType.Model.Fields[parentField];
            Dictionary<string, dynamic> filters = new Dictionary<string, dynamic>();
            foreach (Argument arg in parentType.Model.Fields[parentField].Arguments)
            {
                if (arg.Value is string && parentType.Model.Fields.ContainsKey(arg.Value) && thisType.Model.Fields.ContainsKey(arg.Key) && obj.ContainsKey(thisType.Model.Fields[arg.Key].DataName))
                {
                    if (obj[thisType.Model.Fields[arg.Value].DataName] != null)
                    {
                        filters.Add(arg.Key, obj[thisType.Model.Fields[arg.Value].DataName]);
                    }
                }
                else if (arg.Value is string && parentType.Model.Fields.ContainsKey(arg.Value) && thisType.Model.Fields.ContainsKey(arg.Key) && !obj.ContainsKey(thisType.Model.Fields[arg.Key].DataName))
                {
                    return "invalid";
                }
                else
                {
                    if (arg.Value is string)
                    {
                        if (arg.Value == arg.Name)
                        {
                            filters.Add(arg.Key, arg.Value);
                        }
                        else if (Regex.IsMatch(arg.Value, "(?<=\\()(.*?)(?=\\))"))
                        {
                            return "invalid";
                            //Dictionary<string, dynamic> arguments = new Dictionary<string, dynamic>();

                            //foreach (string c in Regex.Match(arg.Value, "(?<=\\()(.*?)(?=\\))").Value.Split(","))
                            //{
                            //    if (thisType.Model.Fields.ContainsKey(c) && obj[c] != null)
                            //    {
                            //        arguments.Add(c, obj[thisType.Model.Fields[c].DataName]);
                            //    }
                            //    else if (thisType.Model.Fields.ContainsKey(c) && obj.Count == 0)
                            //    {
                            //        arguments.Add(c, null);
                            //    }
                            //}
                            //filters.Add(arg.Key, ResolveVariable(arg.Value, arguments));
                        }
                        else if (IsVariable(arg.Value))
                        {
                            filters.Add(arg.Key, ResolveVariable(arg.Value, new Dictionary<string, dynamic>()));
                        }
                        else if (arg.Type == "DateTime")
                        {
                            filters.Add(arg.Key, new DateTimeGraphType().ParseValue(arg.Value));
                        }
                        else if (arg.Type == "DateTimeOffset")
                        {
                            filters.Add(arg.Key, new DateTimeOffsetGraphType().ParseValue(arg.Value));
                        }
                        else if (arg.Key.Split("_").Count() > 1)
                        {
                            return "invalid";
                        }
                        else
                        {
                            filters.Add(arg.Key, arg.Value);
                        }
                    }
                    else
                    {
                        filters.Add(arg.Key, arg.Value);
                    }
                }
            }
            //return filters;
            //Dictionary<string, dynamic> thisFilter = new Dictionary<string, dynamic>();
            //foreach (Argument kv in pf.Arguments)
            //{
            //    dynamic value = null;
            //    if (kv.Value is string && ((string)kv.Value).StartsWith("@"))
            //    {
            //        //value = ResolveVariable
            //    }

            //    else if (kv.Value is string && parentType.Model.Fields.ContainsKey(kv.Value))
            //    {
            //        value = obj[thisType.Model.Fields[kv.Key].DataName];
            //    }
            //    else
            //    {
            //        value = kv.Value;
            //    }

            //    if (value == null)
            //    {
            //        return null;
            //    }
            //    thisFilter.Add(kv.Key, value);
            //}
            return JsonConvert.SerializeObject(filters);
        }
        
        public ExpressionStarter<Dictionary<string, object>> BuildLinqFilter(Types t, Dictionary<string, dynamic> keys)
        {
            var predicate = PredicateBuilder.New<Dictionary<string, object>>();

            List<string> ops = new List<string> {
                "gt",
                "gte",
                "lt",
                "lte",
                "not",
                "startsWith",
                "notStartsWith",
                "endsWith",
                "notEndsWith",
                "notContains",
                "contains",
                "anyEq",
                "anyNe",
                "in",
                "notIn",
                "last",
                "lastNot",
                "first",
                "firstNot",
                "atIndex",
                "atIndexNot"
            };


            if (keys.Count == 0)
            {
            }
            else
            {
                foreach (KeyValuePair<string, object> key in keys)
                {
                    var value = key.Value;
                    if (key.Value == null || key.Value is string && (string)key.Value == "")
                    {

                    }
                    //else if (t.Model.Fields.ContainsKey(key.Key.Split("_")[0]) && t.Model.Fields[key.Key.Split("_")[0]].DataName == "_id")
                    //{
                    //    if (key.Value is string && ((string)key.Value).Length == 24)
                    //    {
                    //        value = new ObjectId((string)key.Value);
                    //    }
                    //    else if (key.Value is ObjectId)
                    //    {
                    //        value = key.Value;
                    //    }
                    //    else
                    //    {
                    //        value = new List<ObjectId>();
                    //        foreach (string v in (List<string>)key.Value)
                    //        {
                    //            if (v.Length == 24)
                    //            {
                    //                ((List<ObjectId>)value).Add(new ObjectId(v));
                    //            }
                    //        }
                    //    }
                    //}
                    else if (key.Value is ObjectId)
                    {
                        value = ((ObjectId)key.Value).ToString();
                    }
                    if (value == null)
                    {

                    }
                    else if (key.Key == "_orderBy" && value != null)
                    {
                        //foreach (string field in ((string)value).Split(","))
                        //{
                        //    sort.Add(Builders<BsonDocument>.Sort.Ascending(t.Model.Fields[field.Trim()].DataName));
                            
                        //}
                    }
                    else if (key.Key == "_orderBy_desc" && value != null)
                    {
                        //foreach (string field in ((string)value).Split(","))
                        //{
                        //    sort.Add(Builders<BsonDocument>.Sort.Descending(t.Model.Fields[field.Trim()].DataName));
                        //}
                    }
                    else if (key.Key == "_upsert" && value != null)
                    {
                        //upsert = (bool)value;
                    }
                    else if (key.Key == "_start" && value != null)
                    {
                        //skip = (int)value;
                    }
                    else if (key.Key == "_limit" && value != null)
                    {
                        //limit = (int)value;
                    }
                    else if (key.Key.Split("_").Length > 1 && ops.Contains(key.Key.Split("_")[1]))
                    {
                        Column field = t.Model.Fields[key.Key.Split("_")[0]];
                        string operation = key.Key.Split("_")[1];
                        switch (operation)
                        {
                            case "gt":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => (int)p[field.DataName] > (int)value);
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => (float)p[field.DataName] > (float)value);
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => (double)p[field.DataName] > (double)value);
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => (decimal)p[field.DataName] > (decimal)(double)value);
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => (long)p[field.DataName] > (long)value);
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => (DateTime)p[field.DataName] > (DateTime)value);
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => (DateTimeOffset)p[field.DataName] > (DateTimeOffset)value);
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "gte":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => (int)p[field.DataName] >= (int)value);
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => (float)p[field.DataName] >= (float)value);
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => (double)p[field.DataName] >= (double)value);
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => (decimal)p[field.DataName] >= (decimal)(double)value);
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => (long)p[field.DataName] >= (long)value);
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => (DateTime)p[field.DataName] >= (DateTime)value);
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => (DateTimeOffset)p[field.DataName] >= (DateTimeOffset)value);
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "lt":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => (int)p[field.DataName] < (int)value);
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => (float)p[field.DataName] < (float)value);
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => (double)p[field.DataName] < (double)value);
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => (decimal)p[field.DataName] < (decimal)(double)value);
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => (long)p[field.DataName] < (long)value);
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => (DateTime)p[field.DataName] < (DateTime)value);
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => (DateTimeOffset)p[field.DataName] < (DateTimeOffset)value);
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "lte":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => (int)p[field.DataName] <= (int)value);
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => (float)p[field.DataName] <= (float)value);
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => (double)p[field.DataName] <= (double)value);
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => (decimal)p[field.DataName] <= (decimal)(double)value);
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => (long)p[field.DataName] <= (long)value);
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => (DateTime)p[field.DataName] <= (DateTime)value);
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => (DateTimeOffset)p[field.DataName] <= (DateTimeOffset)value);
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "not":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => (string)p[field.DataName] != (string)value);
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => (string)p[field.DataName] != (string)value);
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => (bool)p[field.DataName] != (bool)value);
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => (int)p[field.DataName] != (int)value);
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => (float)p[field.DataName] != (float)value);
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => (double)p[field.DataName] != (double)value);
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => (decimal)p[field.DataName] != (decimal)(double)value);
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => (long)p[field.DataName] != (long)value);
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => (DateTime)p[field.DataName] != (DateTime)value);
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => (DateTimeOffset)p[field.DataName] != (DateTimeOffset)value);
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "startsWith":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((string)p[field.DataName]).StartsWith((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((string)p[field.DataName]).StartsWith((string)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "notStartsWith":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => !((string)p[field.DataName]).StartsWith((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => !((string)p[field.DataName]).StartsWith((string)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "endsWith":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((string)p[field.DataName]).EndsWith((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((string)p[field.DataName]).EndsWith((string)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "notEndsWith":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => !((string)p[field.DataName]).EndsWith((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => !((string)p[field.DataName]).EndsWith((string)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "contains":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((string)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((string)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "notContains":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => !((string)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => !((string)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "anyEq":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => ((List<bool>)p[field.DataName]).Contains((bool)value));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => ((List<int>)p[field.DataName]).Contains((int)value));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => ((List<float>)p[field.DataName]).Contains((float)value));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => ((List<double>)p[field.DataName]).Contains((double)value));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => ((List<decimal>)p[field.DataName]).Contains((decimal)value));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => ((List<long>)p[field.DataName]).Contains((long)value));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => ((List<DateTime>)p[field.DataName]).Contains((DateTime)value));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => ((List<DateTimeOffset>)p[field.DataName]).Contains((DateTimeOffset)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "anyNe":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => !((List<string>)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => !((List<string>)p[field.DataName]).Contains((string)value));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => !((List<bool>)p[field.DataName]).Contains((bool)value));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => !((List<int>)p[field.DataName]).Contains((int)value));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => !((List<float>)p[field.DataName]).Contains((float)value));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => !((List<double>)p[field.DataName]).Contains((double)value));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => !((List<decimal>)p[field.DataName]).Contains((decimal)value));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => !((List<long>)p[field.DataName]).Contains((long)value));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => !((List<DateTime>)p[field.DataName]).Contains((DateTime)value));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => !((List<DateTimeOffset>)p[field.DataName]).Contains((DateTimeOffset)value));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }

                            case "in":
                                {
                                    var ct = "";
                                    if ((ct = field.ColumnType) == "List")
                                    {
                                        ct = ((List<object>)value)[0].GetType().Name;
                                    }
                                    switch (ct)
                                    {
                                        case "String":
                                            {
                                                List<string> values = new List<string>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((string)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((string)p[field.DataName]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                List<string> values = new List<string>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((string)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((string)p[field.DataName]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                List<bool> values = new List<bool>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((bool)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((bool)p[field.DataName]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                List<int> values = new List<int>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((int)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((int)p[field.DataName]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                List<float> values = new List<float>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((float)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((float)p[field.DataName]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                List<double> values = new List<double>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((double)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((double)p[field.DataName]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                List<decimal> values = new List<decimal>();
                                                foreach (double val in (List<double>)value)
                                                {
                                                    values.Add((decimal)val);
                                                }
                                                predicate = predicate.And(p => values.Contains((decimal)p[field.DataName]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                List<long> values = new List<long>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((long)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((long)p[field.DataName]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                List<DateTime> values = new List<DateTime>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((DateTime)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((DateTime)p[field.DataName]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                List<DateTimeOffset> values = new List<DateTimeOffset>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((DateTimeOffset)val);
                                                }
                                                predicate = predicate.And(p => (values).Contains((DateTimeOffset)p[field.DataName]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "notIn":
                                {
                                    var ct = "";
                                    if ((ct = field.ColumnType) == "List")
                                    {
                                        ct = ((List<object>)value)[0].GetType().Name;
                                    }
                                    switch (ct)
                                    {
                                        case "String":
                                            {
                                                List<string> values = new List<string>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((string)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((string)p[field.DataName]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                List<string> values = new List<string>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((string)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((string)p[field.DataName]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                List<bool> values = new List<bool>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((bool)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((bool)p[field.DataName]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                List<int> values = new List<int>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((int)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((int)p[field.DataName]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                List<float> values = new List<float>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((float)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((float)p[field.DataName]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                List<double> values = new List<double>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((double)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((double)p[field.DataName]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                List<decimal> values = new List<decimal>();
                                                foreach (double val in (List<double>)value)
                                                {
                                                    values.Add((decimal)val);
                                                }
                                                predicate = predicate.And(p => !values.Contains((decimal)p[field.DataName]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                List<long> values = new List<long>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((long)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((long)p[field.DataName]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                List<DateTime> values = new List<DateTime>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((DateTime)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((DateTime)p[field.DataName]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                List<DateTimeOffset> values = new List<DateTimeOffset>();
                                                foreach (object val in (List<object>)value)
                                                {
                                                    values.Add((DateTimeOffset)val);
                                                }
                                                predicate = predicate.And(p => !(values).Contains((DateTimeOffset)p[field.DataName]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "last":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).LastOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).LastOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => ((List<bool>)p[field.DataName]).LastOrDefault() == ((bool)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => ((List<int>)p[field.DataName]).LastOrDefault() == ((int)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => ((List<float>)p[field.DataName]).LastOrDefault() == ((float)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => ((List<double>)p[field.DataName]).LastOrDefault() == ((double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => ((List<decimal>)p[field.DataName]).LastOrDefault() == ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => ((List<long>)p[field.DataName]).LastOrDefault() == ((long)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => ((List<DateTime>)p[field.DataName]).LastOrDefault() == ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => ((List<DateTimeOffset>)p[field.DataName]).LastOrDefault() == ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "lastNot":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).LastOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).LastOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => ((List<bool>)p[field.DataName]).LastOrDefault() != ((bool)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => ((List<int>)p[field.DataName]).LastOrDefault() != ((int)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => ((List<float>)p[field.DataName]).LastOrDefault() != ((float)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => ((List<double>)p[field.DataName]).LastOrDefault() != ((double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => ((List<decimal>)p[field.DataName]).LastOrDefault() != ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => ((List<long>)p[field.DataName]).LastOrDefault() != ((long)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => ((List<DateTime>)p[field.DataName]).LastOrDefault() != ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => ((List<DateTimeOffset>)p[field.DataName]).LastOrDefault() != ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "first":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).FirstOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).FirstOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => ((List<bool>)p[field.DataName]).FirstOrDefault() == ((bool)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => ((List<int>)p[field.DataName]).FirstOrDefault() == ((int)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => ((List<float>)p[field.DataName]).FirstOrDefault() == ((float)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => ((List<double>)p[field.DataName]).FirstOrDefault() == ((double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => ((List<decimal>)p[field.DataName]).FirstOrDefault() == ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => ((List<long>)p[field.DataName]).FirstOrDefault() == ((long)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => ((List<DateTime>)p[field.DataName]).FirstOrDefault() == ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => ((List<DateTimeOffset>)p[field.DataName]).FirstOrDefault() == ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "firstNot":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).FirstOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).FirstOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => ((List<bool>)p[field.DataName]).FirstOrDefault() != ((bool)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => ((List<int>)p[field.DataName]).FirstOrDefault() != ((int)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => ((List<float>)p[field.DataName]).FirstOrDefault() != ((float)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => ((List<double>)p[field.DataName]).FirstOrDefault() != ((double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => ((List<decimal>)p[field.DataName]).FirstOrDefault() != ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => ((List<long>)p[field.DataName]).FirstOrDefault() != ((long)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => ((List<DateTime>)p[field.DataName]).FirstOrDefault() != ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => ((List<DateTimeOffset>)p[field.DataName]).FirstOrDefault() != ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "atIndex":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => ((List<bool>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((bool)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => ((List<int>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((int)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => ((List<float>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((float)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => ((List<double>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => ((List<decimal>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => ((List<long>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((long)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => ((List<DateTime>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => ((List<DateTimeOffset>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case "atIndexNot":
                                {
                                    switch (field.ColumnType)
                                    {
                                        case "String":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Json":
                                            {
                                                predicate = predicate.And(p => ((List<string>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((string)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Boolean":
                                            {
                                                predicate = predicate.And(p => ((List<bool>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((bool)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Int32":
                                            {
                                                predicate = predicate.And(p => ((List<int>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((int)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Float":
                                            {
                                                predicate = predicate.And(p => ((List<float>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((float)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Double":
                                            {
                                                predicate = predicate.And(p => ((List<double>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Decimal":
                                            {
                                                predicate = predicate.And(p => ((List<decimal>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "Long":
                                            {
                                                predicate = predicate.And(p => ((List<long>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((long)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTime":
                                            {
                                                predicate = predicate.And(p => ((List<DateTime>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        case "DateTimeOffset":
                                            {
                                                predicate = predicate.And(p => ((List<DateTimeOffset>)p[field.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                break;
                                            }
                                        default:
                                            {
                                                break;
                                            }
                                    }
                                    break;
                                }
                        }
                    }
                    else if (key.Key.Split("_").Length > 1 && !ops.Contains(key.Key.Split("_")[1]))
                    {
                        Types st = _data.typeDict[_data.ResolveType(t.Model.Fields[key.Key.Split("_")[0]].ColumnGraphType).Name];
                        Column field = t.Model.Fields[key.Key.Split("_")[0]];
                        Column subField = st.Model.Fields[key.Key.Split("_")[1]];
                        if (key.Key.Split("_").Length == 3)
                        {
                            string operation = key.Key.Split("_")[2];
                            switch (operation)
                            {
                                case "gt":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => (int)((Dictionary<string, object>)p[field.DataName])[subField.DataName] > (int)value);
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => (float)((Dictionary<string, object>)p[field.DataName])[subField.DataName] > (float)value);
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => (double)((Dictionary<string, object>)p[field.DataName])[subField.DataName] > (double)value);
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => (decimal)((Dictionary<string, object>)p[field.DataName])[subField.DataName] > (decimal)(double)value);
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => (long)((Dictionary<string, object>)p[field.DataName])[subField.DataName] > (long)value);
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => (DateTime)((Dictionary<string, object>)p[field.DataName])[subField.DataName] > (DateTime)value);
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => (DateTimeOffset)((Dictionary<string, object>)p[field.DataName])[subField.DataName] > (DateTimeOffset)value);
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "gte":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => (int)((Dictionary<string, object>)p[field.DataName])[subField.DataName] >= (int)value);
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => (float)((Dictionary<string, object>)p[field.DataName])[subField.DataName] >= (float)value);
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => (double)((Dictionary<string, object>)p[field.DataName])[subField.DataName] >= (double)value);
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => (decimal)((Dictionary<string, object>)p[field.DataName])[subField.DataName] >= (decimal)(double)value);
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => (long)((Dictionary<string, object>)p[field.DataName])[subField.DataName] >= (long)value);
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => (DateTime)((Dictionary<string, object>)p[field.DataName])[subField.DataName] >= (DateTime)value);
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => (DateTimeOffset)((Dictionary<string, object>)p[field.DataName])[subField.DataName] >= (DateTimeOffset)value);
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "lt":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => (int)((Dictionary<string, object>)p[field.DataName])[subField.DataName] < (int)value);
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => (float)((Dictionary<string, object>)p[field.DataName])[subField.DataName] < (float)value);
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => (double)((Dictionary<string, object>)p[field.DataName])[subField.DataName] < (double)value);
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => (decimal)((Dictionary<string, object>)p[field.DataName])[subField.DataName] < (decimal)(double)value);
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => (long)((Dictionary<string, object>)p[field.DataName])[subField.DataName] < (long)value);
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => (DateTime)((Dictionary<string, object>)p[field.DataName])[subField.DataName] < (DateTime)value);
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => (DateTimeOffset)((Dictionary<string, object>)p[field.DataName])[subField.DataName] < (DateTimeOffset)value);
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "lte":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => (int)((Dictionary<string, object>)p[field.DataName])[subField.DataName] <= (int)value);
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => (float)((Dictionary<string, object>)p[field.DataName])[subField.DataName] <= (float)value);
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => (double)((Dictionary<string, object>)p[field.DataName])[subField.DataName] <= (double)value);
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => (decimal)((Dictionary<string, object>)p[field.DataName])[subField.DataName] <= (decimal)(double)value);
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => (long)((Dictionary<string, object>)p[field.DataName])[subField.DataName] <= (long)value);
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => (DateTime)((Dictionary<string, object>)p[field.DataName])[subField.DataName] <= (DateTime)value);
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => (DateTimeOffset)((Dictionary<string, object>)p[field.DataName])[subField.DataName] <= (DateTimeOffset)value);
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "not":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => (string)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (string)value);
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => (string)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (string)value);
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => (bool)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (bool)value);
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => (int)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (int)value);
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => (float)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (float)value);
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => (double)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (double)value);
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => (decimal)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (decimal)(double)value);
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => (long)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (long)value);
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => (DateTime)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (DateTime)value);
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => (DateTimeOffset)((Dictionary<string, object>)p[field.DataName])[subField.DataName] != (DateTimeOffset)value);
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "startsWith":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).StartsWith((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).StartsWith((string)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "notStartsWith":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => !((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).StartsWith((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => !((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).StartsWith((string)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "endsWith":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).EndsWith((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).EndsWith((string)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "notEndsWith":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => !((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).EndsWith((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => !((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).EndsWith((string)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "contains":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "notContains":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => !((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => !((string)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "anyEq":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => ((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((bool)value));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => ((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((int)value));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => ((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((float)value));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => ((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((double)value));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => ((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((decimal)value));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => ((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((long)value));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((DateTime)value));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((DateTimeOffset)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "anyNe":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => !((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => !((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((string)value));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => !((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((bool)value));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => !((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((int)value));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => !((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((float)value));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => !((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((double)value));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => !((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((decimal)value));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => !((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((long)value));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => !((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((DateTime)value));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => !((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).Contains((DateTimeOffset)value));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }

                                case "in":
                                    {
                                        var ct = "";
                                        if ((ct = field.ColumnType) == "List")
                                        {
                                            ct = ((List<object>)value)[0].GetType().Name;
                                        }
                                        switch (ct)
                                        {
                                            case "String":
                                                {
                                                    List<string> values = new List<string>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((string)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<string>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<string> spVals = new List<string>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((string)spVal);
                                                                }                                                                
                                                                return val.Except(spVals).Count() > 0;
                                                            } else
                                                            {
                                                                return val.Contains((string)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;                                                                
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    List<string> values = new List<string>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((string)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<string>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<string> spVals = new List<string>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((string)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((string)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    List<bool> values = new List<bool>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((bool)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<bool>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<bool> spVals = new List<bool>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((bool)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((bool)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    List<int> values = new List<int>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((int)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<int>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<int> spVals = new List<int>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((int)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((int)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    List<float> values = new List<float>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((float)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<float>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<float> spVals = new List<float>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((float)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((float)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    List<double> values = new List<double>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((double)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<double>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<double> spVals = new List<double>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((double)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((double)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    List<decimal> values = new List<decimal>();
                                                    foreach (double val in (List<double>)value)
                                                    {
                                                        values.Add((decimal)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<decimal>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<decimal> spVals = new List<decimal>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((decimal)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((decimal)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    List<long> values = new List<long>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((long)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<long>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<long> spVals = new List<long>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((long)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((long)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    List<DateTime> values = new List<DateTime>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((DateTime)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<DateTime>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<DateTime> spVals = new List<DateTime>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((DateTime)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((DateTime)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    List<DateTimeOffset> values = new List<DateTimeOffset>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((DateTimeOffset)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<DateTimeOffset>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<DateTimeOffset> spVals = new List<DateTimeOffset>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((DateTimeOffset)spVal);
                                                                }
                                                                return val.Except(spVals).Count() > 0;
                                                            }
                                                            else
                                                            {
                                                                return val.Contains((DateTimeOffset)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "notIn":
                                    {
                                        var ct = "";
                                        if ((ct = field.ColumnType) == "List")
                                        {
                                            ct = ((List<object>)value)[0].GetType().Name;
                                        }
                                        switch (ct)
                                        {
                                            case "String":
                                                {
                                                    List<string> values = new List<string>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((string)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<string>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<string> spVals = new List<string>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((string)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((string)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    List<string> values = new List<string>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((string)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<string>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<string> spVals = new List<string>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((string)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((string)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    List<bool> values = new List<bool>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((bool)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<bool>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<bool> spVals = new List<bool>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((bool)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((bool)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    List<int> values = new List<int>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((int)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<int>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<int> spVals = new List<int>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((int)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((int)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    List<float> values = new List<float>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((float)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<float>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<float> spVals = new List<float>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((float)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((float)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    List<double> values = new List<double>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((double)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<double>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<double> spVals = new List<double>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((double)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((double)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    List<decimal> values = new List<decimal>();
                                                    foreach (double val in (List<double>)value)
                                                    {
                                                        values.Add((decimal)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<decimal>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<decimal> spVals = new List<decimal>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((decimal)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((decimal)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    List<long> values = new List<long>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((long)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<long>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<long> spVals = new List<long>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((long)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((long)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    List<DateTime> values = new List<DateTime>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((DateTime)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<DateTime>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<DateTime> spVals = new List<DateTime>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((DateTime)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((DateTime)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    List<DateTimeOffset> values = new List<DateTimeOffset>();
                                                    foreach (object val in (List<object>)value)
                                                    {
                                                        values.Add((DateTimeOffset)val);
                                                    }
                                                    Func<Dictionary<string, object>, List<DateTimeOffset>, bool> func = (v, val) =>
                                                    {
                                                        return ((List<object>)v[field.DataName]).FindAll(sp => {
                                                            if (((Dictionary<string, object>)sp)[subField.DataName] is List<object>)
                                                            {
                                                                List<DateTimeOffset> spVals = new List<DateTimeOffset>();
                                                                foreach (object spVal in (List<object>)((Dictionary<string, object>)sp)[subField.DataName])
                                                                {
                                                                    spVals.Add((DateTimeOffset)spVal);
                                                                }
                                                                return !(val.Except(spVals).Count() > 0);
                                                            }
                                                            else
                                                            {
                                                                return !val.Contains((DateTimeOffset)((Dictionary<string, object>)sp)[subField.DataName]);
                                                            }
                                                        }).Count() > 0;
                                                    };
                                                    predicate = predicate.And(p => func.Invoke(p, values));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "last":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => ((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((bool)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => ((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((int)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => ((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((float)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => ((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => ((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => ((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((long)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() == ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "lastNot":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => ((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((bool)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => ((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((int)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => ((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((float)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => ((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => ((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => ((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((long)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).LastOrDefault() != ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "first":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => ((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((bool)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => ((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((int)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => ((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((float)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => ((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => ((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => ((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((long)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() == ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "firstNot":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => ((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((bool)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => ((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((int)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => ((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((float)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => ((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => ((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => ((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((long)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).FirstOrDefault() != ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "atIndex":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => ((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((bool)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => ((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((int)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => ((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((float)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => ((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => ((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => ((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((long)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) == ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case "atIndexNot":
                                    {
                                        switch (field.ColumnType)
                                        {
                                            case "String":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Json":
                                                {
                                                    predicate = predicate.And(p => ((List<string>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((string)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Boolean":
                                                {
                                                    predicate = predicate.And(p => ((List<bool>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((bool)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Int32":
                                                {
                                                    predicate = predicate.And(p => ((List<int>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((int)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Float":
                                                {
                                                    predicate = predicate.And(p => ((List<float>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((float)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Double":
                                                {
                                                    predicate = predicate.And(p => ((List<double>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Decimal":
                                                {
                                                    predicate = predicate.And(p => ((List<decimal>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((decimal)(double)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "Long":
                                                {
                                                    predicate = predicate.And(p => ((List<long>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((long)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTime":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTime>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((DateTime)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            case "DateTimeOffset":
                                                {
                                                    predicate = predicate.And(p => ((List<DateTimeOffset>)((Dictionary<string, object>)p[field.DataName])[subField.DataName]).ElementAtOrDefault((int)((Dictionary<string, object>)value)["index"]) != ((DateTimeOffset)((Dictionary<string, object>)value)["value"]));
                                                    break;
                                                }
                                            default:
                                                {
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                            }
                        }
                        else
                        {
                            switch (field.ColumnType)
                            {
                                case "String":
                                    {
                                        Func<Dictionary<string, object>, object, bool> func = (v, val) =>
                                        {
                                            if (((Dictionary<string, object>)v[field.DataName])[subField.DataName] is ObjectId)
                                            {
                                                return ((ObjectId)((Dictionary<string, object>)v[field.DataName])[subField.DataName]).ToString() == (string)val;
                                            }
                                            else
                                            {
                                                return (string)((Dictionary<string, object>)v[field.DataName])[subField.DataName] == (string)val;
                                            }
                                        };
                                        predicate = predicate.And(p => func.Invoke(p, value));
                                        break;
                                    }
                                case "Json":
                                    {
                                        predicate = predicate.And(p => (string)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (string)value);
                                        break;
                                    }
                                case "Boolean":
                                    {
                                        predicate = predicate.And(p => (bool)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (bool)value);
                                        break;
                                    }
                                case "Int32":
                                    {
                                        predicate = predicate.And(p => (int)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (int)value);
                                        break;
                                    }
                                case "Float":
                                    {
                                        predicate = predicate.And(p => (float)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (float)value);
                                        break;
                                    }
                                case "Double":
                                    {
                                        predicate = predicate.And(p => (double)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (double)value);
                                        break;
                                    }
                                case "Decimal":
                                    {
                                        predicate = predicate.And(p => (decimal)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (decimal)(double)value);
                                        break;
                                    }
                                case "Long":
                                    {
                                        predicate = predicate.And(p => (long)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (long)value);
                                        break;
                                    }
                                case "DateTime":
                                    {
                                        predicate = predicate.And(p => (DateTime)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (DateTime)value);
                                        break;
                                    }
                                case "DateTimeOffset":
                                    {
                                        predicate = predicate.And(p => (DateTimeOffset)((Dictionary<string, object>)p[field.DataName])[subField.DataName] == (DateTimeOffset)value);
                                        break;
                                    }
                                default:
                                    {
                                        break;
                                    }
                            }
                        }
                    }
                    else if (value != null)
                    {
                        Column field = t.Model.Fields[key.Key.Split("_")[0]];
                        switch (field.ColumnType)
                        {
                            case "String":
                                {
                                    Func<Dictionary<string, object>, object, bool> func = (v, val) =>
                                    {
                                        if (v[field.DataName] is ObjectId)
                                        {
                                            return ((ObjectId)v[field.DataName]).ToString() == (string)val;
                                        }
                                        else
                                        {
                                            return (string)v[field.DataName] == (string)val;
                                        }
                                    };
                                    predicate = predicate.And(p => func.Invoke(p, value));
                                    break;
                                }
                            case "Json":
                                {
                                    predicate = predicate.And(p => (string)p[field.DataName] == (string)value);
                                    break;
                                }
                            case "Boolean":
                                {
                                    predicate = predicate.And(p => (bool)p[field.DataName] == (bool)value);
                                    break;
                                }
                            case "Int32":
                                {
                                    predicate = predicate.And(p => (int)p[field.DataName] == (int)value);
                                    break;
                                }
                            case "Float":
                                {
                                    predicate = predicate.And(p => (float)p[field.DataName] == (float)value);
                                    break;
                                }
                            case "Double":
                                {
                                    predicate = predicate.And(p => (double)p[field.DataName] == (double)value);
                                    break;
                                }
                            case "Decimal":
                                {
                                    predicate = predicate.And(p => (decimal)p[field.DataName] == (decimal)(double)value);
                                    break;
                                }
                            case "Long":
                                {
                                    predicate = predicate.And(p => (long)p[field.DataName] == (long)value);
                                    break;
                                }
                            case "DateTime":
                                {
                                    predicate = predicate.And(p => (DateTime)p[field.DataName] == (DateTime)value);
                                    break;
                                }
                            case "DateTimeOffset":
                                {
                                    predicate = predicate.And(p => (DateTimeOffset)p[field.DataName] == (DateTimeOffset)value);
                                    break;
                                }
                            default:
                                {
                                    break;
                                }
                        }
                    }
                }
            }
            return predicate;
        }
        
        public dynamic ResolveUser(string arg, Dictionary<string, dynamic> arguments = null)
        {
            Dictionary<string, dynamic> user = new Dictionary<string, dynamic>();
            var userType = _data.GetTypesType("User").Result;
            var userName = "";
            if (arg.StartsWith("@currentUser"))
            {
                userName = _httpContext.HttpContext.User.Identity.Name;
            }
            else if (arg.StartsWith("@user") && Regex.IsMatch(arg.Split(".")[0], "([\\(\\)])"))
            {
                userName = Regex.Match(arg.Split(".")[0], "(?<=\\()(.*?)(?=\\))").Value;
            }
            if (_settings.UserCRUD.GetUser != null)
            {
                foreach (var kv in _settings.UserCRUD.GetUser.Invoke(userName))
                {
                    if (userType == null)
                    {
                        user.Add(kv.Key, kv.Value);
                    }
                    else
                    {
                        Column column = userType.Model.Columns.Find(c => c.DataName == kv.Key);
                        string key = kv.Key;
                        if (column != null)
                        {
                            key = column.Name;
                        }
                        user.Add(key, kv.Value);
                    }
                }
            }
            if (userType != null)
            {
                foreach (var kv in GetOne(null, _data.typeDict["User"], new Dictionary<string, dynamic> { { _settings.UserCRUD.UserNameProperty, userName } }))
                {

                    if (userType == null)
                    {
                        if (!user.ContainsKey(kv.Key))
                            user.Add(kv.Key, kv.Value);
                    }
                    else
                    {
                        Column column = userType.Model.Columns.Find(c => c.DataName == kv.Key);
                        string key = kv.Key;
                        if (column != null)
                        {
                            key = column.Name;
                        }
                        if (!user.ContainsKey(key))
                            user.Add(key, kv.Value);
                    }
                }
            }
            else
            {
                foreach (var value in _httpContext.HttpContext.User.Claims.Where(c => c.Type != ClaimTypes.Role).ToList())
                {
                    user.Add(value.Type, value.Value);
                }
            }
            if (arg == "@currentUser")
            {
                return userName;
            }
            else if (user.ContainsKey(arg.Split(".")[1]))
            {
                return user[arg.Split(".")[1]];
            }
            else if (arg.Split(".")[1] == "groups")
            {
                return _httpContext.HttpContext.User.Claims.Where(c => c.Type == ClaimTypes.Role).ToList();
            }
            else
            {
                Dictionary<string, dynamic> userData = new Dictionary<string, dynamic>();
                var userDataType = _data.GetTypesType("UserData").Result;
                var UserData = GetOne(null, _data.typeDict["UserData"], new Dictionary<string, dynamic> { { "sid", user["objectSid"] } });
                if (UserData != null)
                {
                    foreach (var kv in UserData)
                    {
                        userData.Add(userDataType.Model.Columns.Find(c => c.DataName == kv.Key).Name, kv.Value);
                    }
                }
                if (userData.ContainsKey(arg.Split(".")[1]))
                {
                    return userData[arg.Split(".")[1]];
                }
                else
                {
                    return null;
                }
            }
        }
        public string GetFile(Types type, string value)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.ReadFileAsBase64(type.Name, value).Result; }
                case "sql": { return ""; }
                case "ad": { return ""; }
                default: return null;
            }
        }
        public IDataLoader<string, dynamic> BatchOne(Types type, string name, Types parentType, string fieldName)
        {
            return _dataLoader.Context.GetOrAddBatchLoader<string, dynamic>(name, async (keySets) =>
            {
                switch (type.Type)
                {
                    case "mongo":
                        {
                            return await _mongoData.BatchDocument(type.Name, keySets, parentType, fieldName);
                        }
                    case "sql":
                        {
                            return await _sqlData.BatchRecord(type.Name, keySets, parentType, fieldName);
                        }
                    //case "ad":
                    //    {
                    //        result = _adData.BatchADObject(type.Name, filter);
                    //        break;
                    //    }
                    default:
                        {
                            return null;
                        };
                }
            });
        }
        public IDataLoader<string, dynamic> BatchMany(Types type, string name, Types parentType, string fieldName)
        {
            return _dataLoader.Context.GetOrAddBatchLoader<string, dynamic>(name, async (keySets) =>
            {
                switch (type.Type)
                {
                    case "mongo":
                        {
                            return await _mongoData.BatchDocuments(type.Name, keySets, parentType, fieldName);
                        }
                    case "sql":
                        {
                            return await _sqlData.BatchRecords(type.Name, keySets, parentType, fieldName);
                        }
                    //case "ad":
                    //    {
                    //        result = _adData.GetADObjects(type.Name, filter);
                    //        break;
                    //    }
                    default:
                        {
                            return null;
                        };
                }
            });
        }
        public List<dynamic> GetMany(IResolveFieldContext context, Types type, Dictionary<string, dynamic> filter)
        {
            List<dynamic> result = new List<dynamic>();
            switch (type.Type)
            {
                case "mongo":
                    {
                        result = _mongoData.GetDocuments(type.Name, filter).Result;
                        break;
                    }
                case "sql":
                    {
                        result = _sqlData.GetRecords(context, type.Name, filter).Result;
                        break;
                    }
                case "ad":
                    {
                        result = _aDData.GetADObjects(type.Name, filter);
                        break;
                    }
                default:
                    {
                        break;
                    };
            }
            return result;
        }
        public Dictionary<string, dynamic> GetOne(IResolveFieldContext context, Types type, Dictionary<string, dynamic> filter)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            switch (type.Type)
            {
                case "mongo":
                    {
                        result = _mongoData.GetDocument(type.Name, filter).Result;
                        break;
                    }
                case "sql":
                    {
                        result = _sqlData.GetRecord(context, type.Name, filter).Result;
                        break;
                    }
                case "ad":
                    {
                        result = _aDData.GetADObject(type.Name, filter);
                        break;
                    }
                default:
                    {
                        break;
                    };
            }
            return result;
        }
        public Dictionary<string, dynamic> AddOne(IResolveFieldContext context, Types type, Dictionary<string, dynamic> values)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            switch (type.Type)
            {
                case "mongo":
                    {
                        result = _mongoData.AddDocument(type.Name, values).Result;
                        break;
                    }
                case "sql":
                    {
                        result = _sqlData.AddRecord(context, type.Name, values).Result;
                        break;
                    }
                case "ad":
                    {
                        result = _aDData.AddADObject(type.Name, values);
                        break;
                    }
                default:
                    {
                        break;
                    };
            }
            return result;
        }
        public List<dynamic> AddMany(IResolveFieldContext context, Types type, List<Dictionary<string, dynamic>> manyValues)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.AddDocuments(type.Name, manyValues).Result; }
                //case "sql": { return _sqlData.AddRecords(type.Name, manyValues).Result; }
                //case "ad": { return _adData.AddADObjects(type.Name, manyValues); }
                default: return null;
            }
        }
        public Dictionary<string, dynamic> UpdateOne(IResolveFieldContext context, Types type, Dictionary<string, dynamic> filter, Dictionary<string, dynamic> update)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            switch (type.Type)
            {
                case "mongo":
                    {
                        result = _mongoData.UpdateDocument(type.Name, filter, update).Result;
                        break;
                    }
                case "sql":
                    {
                        result = _sqlData.UpdateRecord(context, type.Name, filter, update).Result;
                        break;
                    }
                case "ad":
                    {
                        result = _aDData.UpdateADObject(type.Name, filter, update);
                        break;
                    }
                default:
                    {
                        break;
                    };
            }
            return result;
        }
        public List<dynamic> UpdateMany(IResolveFieldContext context, Types type, Dictionary<string, dynamic> filter, Dictionary<string, dynamic> update)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.UpdateDocuments(type.Name, filter, update).Result; }
                //case "sql": { return _sqlData.UpdateRecords(type.Name, filter, manyUpdate).Result; }
                //case "ad": { return _adData.UpdateADObjects(type.Name, filter, manyUpdate); }
                default: return null;
            }
        }
        public Dictionary<string, dynamic> RemoveOne(IResolveFieldContext context, Types type, Dictionary<string, dynamic> filter)
        {
            Dictionary<string, dynamic> result = new Dictionary<string, dynamic>();
            switch (type.Type)
            {
                case "mongo":
                    {
                        result = _mongoData.RemoveDocument(type.Name, filter).Result;
                        break;
                    }
                case "sql":
                    {
                        result = _sqlData.RemoveRecord(context, type.Name, filter).Result;
                        break;
                    }
                case "ad":
                    {
                        result = _aDData.RemoveADObject(type.Name, filter);
                        break;
                    }
                default:
                    {
                        break;
                    };
            }
            return result;
        }
        public List<dynamic> RemoveMany(IResolveFieldContext context, Types type, Dictionary<string, dynamic> filter)
        {
            switch (type.Type)
            {
                case "mongo": { return _mongoData.RemoveDocuments(type.Name, filter).Result; }
                //case "sql": { return _sqlData.RemoveRecords(type.Name, filter).Result; }
                //case "ad": { return _adData.RemoveADObjects(type.Name, filter); }
                default: return null;
            }
        }

    }
}
