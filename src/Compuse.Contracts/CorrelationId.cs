using System.Diagnostics.CodeAnalysis;

namespace Compuse.Contracts;

public sealed record CorrelationId
{
    private CorrelationId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CorrelationId New()
    {
        Guid value = Guid.NewGuid();
        while (value == Guid.Empty)
        {
            value = Guid.NewGuid();
        }

        return new CorrelationId(value);
    }

    public static CorrelationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A correlation identifier cannot be an empty GUID.", nameof(value));
        }

        return new CorrelationId(value);
    }

    public static CorrelationId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParseExactD(value, out Guid parsed))
        {
            throw new FormatException("A correlation identifier must use canonical GUID D form.");
        }

        if (parsed == Guid.Empty)
        {
            throw new ArgumentException("A correlation identifier cannot be an empty GUID.", nameof(value));
        }

        return new CorrelationId(parsed);
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out CorrelationId? correlationId)
    {
        correlationId = null;
        if (value is null || !TryParseExactD(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        correlationId = new CorrelationId(parsed);
        return true;
    }

    public override string ToString() => Value.ToString("D").ToLowerInvariant();

    private static bool TryParseExactD(string value, out Guid parsed)
    {
        if (value.Length != 36)
        {
            parsed = Guid.Empty;
            return false;
        }

        return Guid.TryParseExact(value, "D", out parsed);
    }
}
