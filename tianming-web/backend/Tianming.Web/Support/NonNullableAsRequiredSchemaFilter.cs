using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Promotes properties the C# type system already guarantees to be present into
/// the OpenAPI <c>required</c> set.
/// </summary>
/// <remarks>
/// <para>
/// <c>SupportNonNullableReferenceTypes()</c> only emits <c>nullable: false</c>; it does
/// not touch schema-level <c>required</c>, and Swashbuckle 6.5.0 has no built-in option
/// that does. Without this filter every property except the 13 carrying an explicit
/// <c>[Required]</c> is exported as optional, so generated TypeScript comes out as
/// <c>token?: string</c> for a <c>string Token { get; set; } = null!</c> that is in fact
/// always populated — looser than the hand-written types it is meant to replace, and
/// forcing null checks at every consumer.
/// </para>
/// <para>
/// A property is treated as required when any of these hold:
/// </para>
/// <list type="bullet">
///   <item>it is declared with the C# <c>required</c> modifier;</item>
///   <item>it is a non-nullable reference type (nullable annotation context enabled);</item>
///   <item>it is a non-nullable value type without a default in the schema.</item>
/// </list>
/// <para>
/// Nullable reference types (<c>string?</c>), <see cref="Nullable{T}"/>, and anything
/// Swashbuckle already marked <c>nullable: true</c> are deliberately left optional —
/// those genuinely may be absent.
/// </para>
/// </remarks>
public sealed class NonNullableAsRequiredSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null || schema.Properties.Count == 0)
            return;

        // Only object schemas carry a meaningful `required` set.
        if (context.Type is null || context.Type.IsPrimitive || context.Type.IsEnum)
            return;

        var members = context.Type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(member => member is PropertyInfo or FieldInfo)
            .ToArray();

        foreach (var (name, property) in schema.Properties)
        {
            // Respect what Swashbuckle already determined: an explicitly nullable
            // schema must stay optional even if the CLR member looks non-nullable.
            if (property.Nullable)
                continue;

            // A positional parameter with a default (e.g. `string Title = ""`) is
            // omittable on the wire even though the type is non-nullable, so it must
            // stay optional. Checked before the type-based rules, which cannot see this.
            if (HasConstructorDefault(context.Type, name))
                continue;

            var member = FindMember(members, name);
            if (member is null)
                continue;

            if (IsRequiredByContract(member))
                schema.Required.Add(name);
        }
    }

    /// <summary>
    /// Matches an OpenAPI property name back to its CLR member. Swashbuckle applies the
    /// serializer naming policy (camelCase here) and honours
    /// <c>[JsonPropertyName]</c>, so a case-insensitive comparison against both the CLR
    /// name and any explicit JSON name is what reliably lines the two up.
    /// </summary>
    private static MemberInfo? FindMember(MemberInfo[] members, string schemaPropertyName)
    {
        foreach (var member in members)
        {
            var jsonName = member
                .GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                ?.Name;

            if (jsonName is not null &&
                string.Equals(jsonName, schemaPropertyName, StringComparison.OrdinalIgnoreCase))
                return member;

            if (string.Equals(member.Name, schemaPropertyName, StringComparison.OrdinalIgnoreCase))
                return member;
        }

        return null;
    }

    /// <summary>
    /// True when the property is backed by a constructor parameter carrying a default
    /// value. Positional records are the common case: <c>record R(string Title = "")</c>
    /// compiles to a non-nullable property, so the nullability rules below would mark it
    /// required, but System.Text.Json falls back to the default when the client omits it.
    /// Marking it required would reject payloads the endpoint actually accepts.
    /// </summary>
    private static bool HasConstructorDefault(Type type, string schemaPropertyName)
    {
        foreach (var constructor in type.GetConstructors())
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (!parameter.HasDefaultValue || parameter.Name is null)
                    continue;

                if (string.Equals(parameter.Name, schemaPropertyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool IsRequiredByContract(MemberInfo member)
    {
        // The C# `required` modifier is a compile-time guarantee the object cannot be
        // constructed without the member, so it is the strongest signal available.
        if (member.GetCustomAttribute<RequiredMemberAttribute>() is not null)
            return true;

        var memberType = member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => null,
        };

        if (memberType is null)
            return false;

        // Nullable<T> is explicitly optional.
        if (Nullable.GetUnderlyingType(memberType) is not null)
            return false;

        // Non-nullable value types always carry a value.
        if (memberType.IsValueType)
            return true;

        return IsNonNullableReference(member);
    }

    /// <summary>
    /// Reads the compiler-emitted nullability metadata for a reference-type member.
    /// <see cref="NullabilityInfoContext"/> is the supported way to do this; hand-parsing
    /// <c>[Nullable]</c>/<c>[NullableContext]</c> attributes gets the generic and
    /// containing-context rules wrong.
    /// </summary>
    private static bool IsNonNullableReference(MemberInfo member)
    {
        var context = new NullabilityInfoContext();

        var info = member switch
        {
            PropertyInfo property => context.Create(property),
            FieldInfo field => context.Create(field),
            _ => null,
        };

        // ReadState is what a client observes on a response payload.
        return info?.ReadState == NullabilityState.NotNull;
    }
}
