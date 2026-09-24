using GraphQL.Types;

namespace DataCrush.TypiQL.Models
{
    public class SqlColumn
    {
        public string TABLE_CATALOG { get; set; }
        public string TABLE_SCHEMA { get; set; }
        public string TABLE_NAME { get; set; }
        public string COLUMN_NAME { get; set; }
        public int ORDINAL_POSITION { get; set; }
        public string IS_NULLABLE { get; set; }
        public string DATA_TYPE { get; set; }
        public int CHARACTER_MAXIMUM_LENGTH { get; set; }
        public int CHARACTER_OCTET_LENGTH { get; set; }
        public int NUMERIC_PRECISION { get; set; }
        public int NUMERIC_PRECISION_RADIX { get; set; }
        public int NUMERIC_SCALE { get; set; }
        public int DATETIME_PRECISION { get; set; }
        public string CHARACTER_SET_CATALOG { get; set; }
        public string CHARACTER_SET_NAME { get; set; }
        public string COLLATION_NAME { get; set; }
        public string DOMAIN_CATALOG { get; set; }
        public string DOMAIN_SCHEMA { get; set; }
        public string DOMAIN_NAME { get; set; }
    }
    public class SqlColumnType : ObjectGraphType<SqlColumn>
    {
        public SqlColumnType(ConfigData data)
        {
            Name = "SqlColumn";
            Field<StringGraphType>("tableCatalog", resolve: context => context.Source.TABLE_CATALOG);
            Field<StringGraphType>("tableSchema", resolve: context => context.Source.TABLE_SCHEMA);
            Field<StringGraphType>("tableName", resolve: context => context.Source.TABLE_NAME);
            Field<StringGraphType>("columnName", resolve: context => context.Source.COLUMN_NAME);
            Field<IntGraphType>("ordinalPosition", resolve: context => context.Source.ORDINAL_POSITION);
            Field<StringGraphType>("isNullable", resolve: context => context.Source.IS_NULLABLE);
            Field<StringGraphType>("dataType", resolve: context => context.Source.DATA_TYPE);
            Field<IntGraphType>("characterMaximumLength", resolve: context => context.Source.CHARACTER_MAXIMUM_LENGTH);
            Field<IntGraphType>("characterOctetLength", resolve: context => context.Source.CHARACTER_OCTET_LENGTH);
            Field<IntGraphType>("numericPrecision", resolve: context => context.Source.NUMERIC_PRECISION);
            Field<IntGraphType>("numericPrecisionRadix", resolve: context => context.Source.NUMERIC_PRECISION_RADIX);
            Field<IntGraphType>("numericScale", resolve: context => context.Source.NUMERIC_SCALE);
            Field<IntGraphType>("datetimePrecision", resolve: context => context.Source.DATETIME_PRECISION);
            Field<StringGraphType>("characterSetCatalog", resolve: context => context.Source.CHARACTER_SET_CATALOG);
            Field<StringGraphType>("characterSetName", resolve: context => context.Source.CHARACTER_SET_NAME);
            Field<StringGraphType>("collationName", resolve: context => context.Source.COLLATION_NAME);
            Field<StringGraphType>("domainCatalog", resolve: context => context.Source.DOMAIN_CATALOG);
            Field<StringGraphType>("domainSchema", resolve: context => context.Source.DOMAIN_SCHEMA);
            Field<StringGraphType>("domainName", resolve: context => context.Source.DOMAIN_NAME);
        }

    }
}
