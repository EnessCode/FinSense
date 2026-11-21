using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinSense.Application.Common.Auditing;

/// <summary>
/// Serializes audit snapshots to valid JSON for <c>audit_logs.old_values / new_values</c> (JSONB).
/// Never build audit JSON via string interpolation: user-supplied text (names, notes) could break JSONB parsing.
/// </summary>
public static class AuditJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(object snapshot) => JsonSerializer.Serialize(snapshot, Options);
}
