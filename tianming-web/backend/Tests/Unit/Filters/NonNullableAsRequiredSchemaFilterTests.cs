using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Filters;

/// <summary>
/// Locks the contract the generated TypeScript depends on. Without this filter every
/// property except those carrying an explicit <c>[Required]</c> exports as optional,
/// so <c>src/api/types.ts</c> cannot re-export the generated schema without becoming
/// looser than the hand-written types it replaces.
/// </summary>
public class NonNullableAsRequiredSchemaFilterTests
{
    [Fact]
    public void NonNullableReference_BecomesRequired()
    {
        var schema = Apply<ResponseSample>();

        Assert.Contains(nameof(ResponseSample.Token).ToLowerInvariant(), Lowered(schema.Required));
    }

    [Fact]
    public void NullableReference_StaysOptional()
    {
        var schema = Apply<ResponseSample>();

        Assert.DoesNotContain(nameof(ResponseSample.Note).ToLowerInvariant(), Lowered(schema.Required));
    }

    [Fact]
    public void NonNullableValueType_BecomesRequired()
    {
        var schema = Apply<ResponseSample>();

        Assert.Contains(nameof(ResponseSample.Count).ToLowerInvariant(), Lowered(schema.Required));
    }

    [Fact]
    public void NullableValueType_StaysOptional()
    {
        var schema = Apply<ResponseSample>();

        Assert.DoesNotContain(nameof(ResponseSample.Ratio).ToLowerInvariant(), Lowered(schema.Required));
    }

    [Fact]
    public void RequiredModifier_BecomesRequired()
    {
        var schema = Apply<RequiredModifierSample>();

        Assert.Contains(nameof(RequiredModifierSample.Id).ToLowerInvariant(), Lowered(schema.Required));
    }

    /// <summary>
    /// Regression: a positional record parameter with a default is omittable on the wire
    /// even though the property type is non-nullable. Marking it required rejected
    /// payloads the endpoint accepts — the real <c>AgentSessionUpdateRequest</c> is
    /// patched with only <c>isArchived</c>, and doing so broke the frontend typecheck.
    /// </summary>
    [Fact]
    public void ConstructorParameterWithDefault_StaysOptional()
    {
        var schema = Apply<DefaultedRecordSample>();

        Assert.DoesNotContain(nameof(DefaultedRecordSample.Title).ToLowerInvariant(), Lowered(schema.Required));
    }

    /// <summary>
    /// A property Swashbuckle already flagged <c>nullable: true</c> must stay optional
    /// even when the CLR member reads as non-nullable; the generator had a reason.
    /// </summary>
    [Fact]
    public void SchemaMarkedNullable_StaysOptional()
    {
        var schema = SchemaFor<ResponseSample>();
        schema.Properties["token"].Nullable = true;

        new NonNullableAsRequiredSchemaFilter().Apply(schema, ContextFor<ResponseSample>());

        Assert.DoesNotContain("token", Lowered(schema.Required));
    }

    [Fact]
    public void SchemaWithoutProperties_IsLeftAlone()
    {
        var schema = new OpenApiSchema { Type = "string" };

        new NonNullableAsRequiredSchemaFilter().Apply(schema, ContextFor<ResponseSample>());

        Assert.Empty(schema.Required);
    }

    private static OpenApiSchema Apply<T>()
    {
        var schema = SchemaFor<T>();
        new NonNullableAsRequiredSchemaFilter().Apply(schema, ContextFor<T>());
        return schema;
    }

    /// <summary>
    /// Mirrors what Swashbuckle hands the filter: camelCased property names and no
    /// <c>required</c> set, which is precisely the gap this filter closes.
    /// </summary>
    private static OpenApiSchema SchemaFor<T>()
    {
        var schema = new OpenApiSchema { Type = "object" };

        foreach (var property in typeof(T).GetProperties())
        {
            var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            schema.Properties[name] = new OpenApiSchema();
        }

        return schema;
    }

    private static SchemaFilterContext ContextFor<T>() => new(typeof(T), null, null);

    private static HashSet<string> Lowered(ISet<string> values) =>
        values.Select(value => value.ToLowerInvariant()).ToHashSet();

    private sealed class ResponseSample
    {
        public string Token { get; set; } = null!;
        public string? Note { get; set; }
        public int Count { get; set; }
        public decimal? Ratio { get; set; }
    }

    private sealed class RequiredModifierSample
    {
        public required string Id { get; set; }
    }

    private sealed record DefaultedRecordSample(string Title = "", bool? IsArchived = null);
}
