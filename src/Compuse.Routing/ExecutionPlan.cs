using Compuse.Discovery;
using Compuse.Requests;

namespace Compuse.Routing;

public sealed class ExecutionPlan
{
    public const string FilesystemBackendId = "filesystem";
    public const string SizeAndFileIdVerification = "size_and_file_id";

    public ExecutionPlan(
        TransferEffect effect,
        FilesystemIdentity destinationIdentity,
        IReadOnlyList<PlannedItem> items)
    {
        if (effect == default || !Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect), effect, "Transfer effect must be a defined nonzero value.");
        }

        ArgumentNullException.ThrowIfNull(destinationIdentity);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("An execution plan requires at least one item.", nameof(items));
        }

        for (int index = 0; index < items.Count; index++)
        {
            if (items[index] is null)
            {
                throw new ArgumentException("Plan items cannot contain null elements.", nameof(items));
            }
        }

        Effect = effect;
        DestinationIdentity = destinationIdentity;
        Items = items;
        BackendId = FilesystemBackendId;
        VerificationStrategy = SizeAndFileIdVerification;
    }

    public string BackendId { get; }

    public TransferEffect Effect { get; }

    public string VerificationStrategy { get; }

    public FilesystemIdentity DestinationIdentity { get; }

    public IReadOnlyList<PlannedItem> Items { get; }
}
