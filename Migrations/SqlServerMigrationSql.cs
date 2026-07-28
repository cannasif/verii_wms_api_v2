using System;

namespace verii_wms_api_v2.Migrations;

internal static class SqlServerMigrationSql
{
    private const string CreateOrAlterFunctionToken = "CREATE OR ALTER FUNCTION";

    public static string CreateOrAlterFunction(string qualifiedName, string definition)
    {
        var tokenIndex = definition.IndexOf(
            CreateOrAlterFunctionToken,
            StringComparison.OrdinalIgnoreCase);

        if (tokenIndex < 0)
        {
            throw new ArgumentException(
                $"Function definition must contain '{CreateOrAlterFunctionToken}'.",
                nameof(definition));
        }

        var createDefinition = ReplaceToken(
            definition,
            tokenIndex,
            "CREATE FUNCTION");
        var alterDefinition = ReplaceToken(
            definition,
            tokenIndex,
            "ALTER FUNCTION");

        return $"""
IF OBJECT_ID(N'{EscapeSqlLiteral(qualifiedName)}') IS NULL
BEGIN
    EXEC sys.sp_executesql N'{EscapeSqlLiteral(createDefinition)}';
END
ELSE
BEGIN
    EXEC sys.sp_executesql N'{EscapeSqlLiteral(alterDefinition)}';
END
""";
    }

    public static string DropFunction(string qualifiedName)
    {
        return $"""
IF OBJECT_ID(N'{EscapeSqlLiteral(qualifiedName)}') IS NOT NULL
BEGIN
    DROP FUNCTION {qualifiedName};
END
""";
    }

    private static string ReplaceToken(string source, int tokenIndex, string replacement)
    {
        return string.Concat(
            source.AsSpan(0, tokenIndex),
            replacement,
            source.AsSpan(tokenIndex + CreateOrAlterFunctionToken.Length));
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
