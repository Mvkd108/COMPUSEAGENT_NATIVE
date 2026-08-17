using System.Text.Json.Serialization;

namespace Compuse.Contracts;

[JsonConverter(typeof(OperationOutcomeJsonConverter))]
public enum OperationOutcome
{
    Committed = 1,
    Refused = 2,
    Failed = 3,
    Indeterminate = 4
}
