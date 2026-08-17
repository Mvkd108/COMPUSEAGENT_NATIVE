using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compuse.Contracts;

public sealed class OperationOutcomeJsonConverter : JsonConverter<OperationOutcome>
{
    public override OperationOutcome Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("OperationOutcome JSON must be an exact lowercase string token.");
        }

        string? token = reader.GetString();
        return token switch
        {
            "committed" => OperationOutcome.Committed,
            "refused" => OperationOutcome.Refused,
            "failed" => OperationOutcome.Failed,
            "indeterminate" => OperationOutcome.Indeterminate,
            _ => throw new JsonException("OperationOutcome JSON must be an exact lowercase vocabulary token.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        OperationOutcome value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        string token = value switch
        {
            OperationOutcome.Committed => "committed",
            OperationOutcome.Refused => "refused",
            OperationOutcome.Failed => "failed",
            OperationOutcome.Indeterminate => "indeterminate",
            _ => throw new JsonException("OperationOutcome values must be one of the defined vocabulary members.")
        };

        writer.WriteStringValue(token);
    }
}
