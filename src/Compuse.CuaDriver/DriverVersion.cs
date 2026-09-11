namespace Compuse.CuaDriver;

public sealed class DriverVersion : IEquatable<DriverVersion>
{
    public DriverVersion(int major, int minor, int patch)
    {
        if (major < 0 || minor < 0 || patch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "Version components cannot be negative.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public static DriverVersion Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("A driver version string is required.");
        }

        string trimmed = value.Trim();
        int dash = trimmed.IndexOf('-');
        string basePart = dash >= 0 ? trimmed[..dash] : trimmed;
        string[] parts = basePart.Split('.');
        if (parts.Length is < 2 or > 3)
        {
            throw new FormatException($"Driver version '{value}' is not a valid semver.");
        }

        if (!int.TryParse(parts[0], out int major)
            || !int.TryParse(parts[1], out int minor)
            || !int.TryParse(parts.Length == 3 ? parts[2] : "0", out int patch))
        {
            throw new FormatException($"Driver version '{value}' is not a valid semver.");
        }

        return new DriverVersion(major, minor, patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public bool Equals(DriverVersion? other) =>
        other is not null && Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) => Equals(obj as DriverVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    public static bool operator ==(DriverVersion? left, DriverVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(DriverVersion? left, DriverVersion? right) => !(left == right);

    public static bool operator <(DriverVersion left, DriverVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Compare(left, right) < 0;
    }

    public static bool operator >(DriverVersion left, DriverVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Compare(left, right) > 0;
    }

    public static bool operator <=(DriverVersion left, DriverVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Compare(left, right) <= 0;
    }

    public static bool operator >=(DriverVersion left, DriverVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Compare(left, right) >= 0;
    }

    private static int Compare(DriverVersion left, DriverVersion right)
    {
        int result = left.Major.CompareTo(right.Major);
        if (result != 0)
        {
            return result;
        }

        result = left.Minor.CompareTo(right.Minor);
        return result != 0 ? result : left.Patch.CompareTo(right.Patch);
    }
}
