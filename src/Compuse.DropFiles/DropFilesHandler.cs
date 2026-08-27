using Compuse.Contracts;
using Compuse.Discovery;
using Compuse.Filesystem;
using Compuse.Requests;
using Compuse.Routing;
using Compuse.Runtime;

namespace Compuse.DropFiles;

public sealed class DropFilesHandler : IOperationHandler<DropFilesRequest>
{
    private readonly IFilesystemDiscovery _discovery;
    private readonly ITransferBackend _backend;

    public DropFilesHandler(IFilesystemDiscovery discovery, ITransferBackend backend)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(backend);
        _discovery = discovery;
        _backend = backend;
    }

    public async ValueTask<OperationResult> ExecuteAsync(
        DropFilesRequest request,
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        RouteDecision decision = Route(request, cancellationToken);
        if (decision.Refusal is not null)
        {
            return OperationResult.Refused(context.CorrelationId, decision.Refusal);
        }

        ExecutionPlan plan = decision.Plan!;
        OperationResult? stale = Revalidate(plan, request, context, cancellationToken);
        if (stale is not null)
        {
            return stale;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(context);
        }

        TransferExecution execution = await _backend.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
        return Map(plan, execution, context, cancellationToken);
    }

    public RouteDecision Plan(DropFilesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Route(request, cancellationToken);
    }

    private RouteDecision Route(DropFilesRequest request, CancellationToken cancellationToken)
    {
        List<string> sourcePaths = new(request.Sources.Count);
        for (int index = 0; index < request.Sources.Count; index++)
        {
            sourcePaths.Add(request.Sources[index].PhysicalFile.AbsolutePath);
        }

        IReadOnlyList<SourceSnapshot> sources = _discovery.DiscoverSources(sourcePaths, cancellationToken);
        DestinationSnapshot destination = _discovery.DiscoverDestination(request.Target, cancellationToken);
        PathInspection[] children = new PathInspection[sources.Count];
        if (destination.Status == DestinationStatus.FilesystemContainer && destination.Identity is not null)
        {
            for (int index = 0; index < sources.Count; index++)
            {
                string destPath = DropFilesRouter.CombineDestination(
                    destination.Identity.NormalizedPath,
                    sources[index].RequestedPath);
                children[index] = _discovery.Inspect(destPath, cancellationToken);
            }
        }

        return DropFilesRouter.Route(request, sources, destination, children);
    }

    private OperationResult? Revalidate(
        ExecutionPlan plan,
        DropFilesRequest request,
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        DestinationSnapshot destination = _discovery.DiscoverDestination(request.Target, cancellationToken);
        if (destination.Status != DestinationStatus.FilesystemContainer
            || destination.Identity is null
            || !destination.Identity.SameObjectAs(plan.DestinationIdentity))
        {
            return OperationResult.Refused(
                context.CorrelationId,
                new RefusalInfo(DropFilesRefusalCode.StaleIdentity, "The destination identity changed before execution."));
        }

        for (int index = 0; index < plan.Items.Count; index++)
        {
            PlannedItem item = plan.Items[index];
            SourceSnapshot source = _discovery.DiscoverSource(item.SourcePath, cancellationToken);
            if (source.Status != SourceStatus.PhysicalFile
                || source.Identity is null
                || !source.Identity.SameObjectAs(item.SourceIdentity)
                || source.ByteLength != item.ByteLength)
            {
                return OperationResult.Refused(
                    context.CorrelationId,
                    new RefusalInfo(DropFilesRefusalCode.StaleIdentity, "A source file identity changed before execution."));
            }

            PathInspection child = _discovery.Inspect(item.DestinationPath, cancellationToken);
            if (child.Presence is PathPresence.File or PathPresence.Directory)
            {
                return OperationResult.Refused(
                    context.CorrelationId,
                    new RefusalInfo(DropFilesRefusalCode.Collision, "A destination file appeared before execution."));
            }
        }

        return null;
    }

    private static OperationResult Map(
        ExecutionPlan plan,
        TransferExecution execution,
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAt = context.Clock.UtcNow;
        List<VerificationEvidence> evidence =
        [
            new VerificationEvidence(
                VerificationEvidenceKind.OsApiReturn,
                "shell_file_operation",
                $"IFileOperation HRESULT 0x{execution.ApiHresult:X8}, aborted={execution.AnyAborted}.",
                observedAt)
        ];

        bool allDestinationsObserved = true;
        bool anyDestinationAppeared = false;
        bool allSourcesRemoved = true;
        bool anySourceRemoved = false;
        bool verificationUnavailable = false;
        for (int index = 0; index < execution.Observations.Count; index++)
        {
            ItemObservation observation = execution.Observations[index];
            if (observation.DestinationMatchesCopy)
            {
                anyDestinationAppeared = true;
                evidence.Add(new VerificationEvidence(
                    VerificationEvidenceKind.ExternalSideEffectObservation,
                    "destination_file_observed",
                    $"Destination file observed at {observation.DestinationPath} with length {observation.Destination.ByteLength}.",
                    observedAt,
                    observation.DestinationPath));
            }
            else
            {
                allDestinationsObserved = false;
                if (observation.Destination.Presence == PathPresence.File)
                {
                    anyDestinationAppeared = true;
                    evidence.Add(new VerificationEvidence(
                        VerificationEvidenceKind.DiagnosticArtifact,
                        DropFilesRefusalCode.IntegrityMismatch,
                        $"Destination file at {observation.DestinationPath} did not match the planned length {observation.ExpectedLength}.",
                        observedAt,
                        observation.DestinationPath));
                }
                else if (observation.Destination.Presence == PathPresence.Inaccessible)
                {
                    verificationUnavailable = true;
                    evidence.Add(new VerificationEvidence(
                        VerificationEvidenceKind.DiagnosticArtifact,
                        DropFilesRefusalCode.VerificationUnavailable,
                        $"Destination path {observation.DestinationPath} could not be inspected after transfer.",
                        observedAt,
                        observation.DestinationPath));
                }
            }

            if (observation.SourceRemoved)
            {
                anySourceRemoved = true;
                evidence.Add(new VerificationEvidence(
                    VerificationEvidenceKind.ExternalSideEffectObservation,
                    "source_removed_observed",
                    $"Source path no longer exists at {observation.SourcePath}.",
                    observedAt,
                    observation.SourcePath));
            }
            else
            {
                allSourcesRemoved = false;
            }
        }

        bool copyCommitted = plan.Effect == TransferEffect.Copy && execution.ApiSucceeded && allDestinationsObserved;
        bool moveCommitted = plan.Effect == TransferEffect.Move
            && execution.ApiSucceeded
            && allDestinationsObserved
            && allSourcesRemoved;
        if (copyCommitted || moveCommitted)
        {
            return OperationResult.Committed(context.CorrelationId, evidence);
        }

        bool sideEffect = anyDestinationAppeared || (plan.Effect == TransferEffect.Move && anySourceRemoved);
        if (sideEffect)
        {
            return OperationResult.Indeterminate(context.CorrelationId, evidence);
        }

        if (verificationUnavailable)
        {
            return OperationResult.Failed(
                context.CorrelationId,
                new FailureInfo(
                    DropFilesRefusalCode.VerificationUnavailable,
                    "The transfer result could not be independently verified.",
                    isTransient: true),
                evidence);
        }

        if (cancellationToken.IsCancellationRequested || execution.AnyAborted)
        {
            return Cancelled(context, evidence);
        }

        return OperationResult.Failed(
            context.CorrelationId,
            new FailureInfo(
                "transfer_failed",
                $"The filesystem transfer did not complete. HRESULT 0x{execution.ApiHresult:X8}.",
                isTransient: false),
            evidence);
    }

    private static OperationResult Cancelled(
        OperationExecutionContext context,
        IReadOnlyList<VerificationEvidence>? evidence = null)
    {
        List<VerificationEvidence> snapshot = [.. evidence ?? []];
        snapshot.Add(new VerificationEvidence(
            VerificationEvidenceKind.DiagnosticArtifact,
            RuntimeOutcomeCode.Cancelled,
            "The operation was cancelled.",
            context.Clock.UtcNow));
        return OperationResult.Indeterminate(context.CorrelationId, snapshot);
    }
}
