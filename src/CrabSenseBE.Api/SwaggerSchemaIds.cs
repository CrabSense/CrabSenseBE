using System.Text.RegularExpressions;

namespace CrabSenseBE.Api;

/// <summary>Stable, unique OpenAPI schema identifiers for DTOs across controllers and namespaces.</summary>
public static class SwaggerSchemaIds
{
    public static string For(Type type)
    {
        if (type.IsArray)
            return For(type.GetElementType()!) + "_Array";

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = definition.FullName ?? definition.Name;
            name = name[..name.IndexOf('`')];
            return Sanitize(name) + "_" + string.Join("_", type.GetGenericArguments().Select(For));
        }

        return Sanitize(type.FullName ?? type.Name);
    }

    private static string Sanitize(string name) =>
        Regex.Replace(name.Replace('+', '.'), @"[^A-Za-z0-9._-]", "_");
}
