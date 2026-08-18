using System.Collections.ObjectModel;
using Compuse.Contracts;

namespace Compuse.Requests;

public sealed class DropFilesRequest
{
    private DropFilesRequest(
        CorrelationId correlationId,
        IReadOnlyList<SourceItem> sources,
        TransferEffect effect,
        TargetSelector target,
        DateTimeOffset? deadlineUtc)
    {
        CorrelationId = correlationId;
        Sources = sources;
        Effect = effect;
        Target = target;
        DeadlineUtc = deadlineUtc;
    }

    public CorrelationId CorrelationId { get; }

    public IReadOnlyList<SourceItem> Sources { get; }

    public TransferEffect Effect { get; }

    public TargetSelector Target { get; }

    public DateTimeOffset? DeadlineUtc { get; }

    public static DropFilesRequest Create(
        CorrelationId correlationId,
        IEnumerable<SourceItem> sources,
        TransferEffect effect,
        TargetSelector target,
        DateTimeOffset? deadlineUtc = null)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(target);

        if (effect == default || !Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(
                nameof(effect),
                effect,
                "Transfer effect must be a defined nonzero value.");
        }

        if (deadlineUtc is DateTimeOffset deadline && deadline.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Request deadlines must use a UTC offset of zero and are not converted.",
                nameof(deadlineUtc));
        }

        ReadOnlyCollection<SourceItem> snapshot = SnapshotSources(sources);
        if (snapshot.Count is < DropFilesRequestLimits.MinSourceCount
            or > DropFilesRequestLimits.MaxSourceCount)
        {
            throw new ArgumentException(
                "A drop_files request requires 1 to 1024 source items.",
                nameof(sources));
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (SourceItem item in snapshot)
        {
            if (!seen.Add(item.PhysicalFile.AbsolutePath))
            {
                throw new ArgumentException(
                    "Source items must not contain duplicate physical-file paths.",
                    nameof(sources));
            }
        }

        return new DropFilesRequest(correlationId, snapshot, effect, target, deadlineUtc);
    }

    private static ReadOnlyCollection<SourceItem> SnapshotSources(IEnumerable<SourceItem> sources)
    {
        List<SourceItem> collected = [];
        foreach (SourceItem? item in sources)
        {
            if (item is null)
            {
                throw new ArgumentException("Source items cannot contain null elements.", nameof(sources));
            }

            collected.Add(item);
        }

        return new ReadOnlyCollection<SourceItem>(collected.ToArray());
    }
}
