using Compuse.ApplicationSurface;
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
    private readonly IWindowDiscovery? _windowDiscovery;
    private readonly IApplicationImportRecipeCatalog? _recipeCatalog;

    public DropFilesHandler(IFilesystemDiscovery discovery, ITransferBackend backend)
        : this(discovery, backend, windowDiscovery: null, recipeCatalog: null)
    {
    }

    public DropFilesHandler(
        IFilesystemDiscovery discovery,
        ITransferBackend backend,
        IWindowDiscovery? windowDiscovery,
        IApplicationImportRecipeCatalog? recipeCatalog)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(backend);
        _discovery = discovery;
        _backend = backend;
        _windowDiscovery = windowDiscovery;
        _recipeCatalog = recipeCatalog;
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

        if (decision.ApplicationPlan is ApplicationImportPlan applicationPlan)
        {
            OperationResult? appStale = RevalidateApplication(applicationPlan, request, context, cancellationToken);
            if (appStale is not null)
            {
                return appStale;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(context);
            }

            IApplicationImportBackend applicationBackend = _recipeCatalog!.CreateBackend(applicationPlan.RecipeName);
            ApplicationImportExecution applicationExecution =
                await applicationBackend.ExecuteAsync(applicationPlan, cancellationToken).ConfigureAwait(false);
            return MapApplication(applicationPlan, applicationExecution, context, cancellationToken);
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
        if (request.Target.Kind == TargetSelectorKind.ApplicationSurface)
        {
            return RouteApplication(request, cancellationToken);
        }

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

    private RouteDecision RouteApplication(DropFilesRequest request, CancellationToken cancellationToken)
    {
        if (_windowDiscovery is null || _recipeCatalog is null)
        {
            return RouteDecision.ForRefusal(
                new RefusalInfo(
                    DropFilesRefusalCode.UnsupportedTargetKind,
                    "Application-surface targets are not supported because no application import recipe catalog is configured."));
        }

        List<string> sourcePaths = new(request.Sources.Count);
        for (int index = 0; index < request.Sources.Count; index++)
        {
            sourcePaths.Add(request.Sources[index].PhysicalFile.AbsolutePath);
        }

        IReadOnlyList<SourceSnapshot> sources = _discovery.DiscoverSources(sourcePaths, cancellationToken);
        WindowDiscoveryResult discovery = _windowDiscovery.Discover(request.Target.ApplicationSurface!, cancellationToken);
        return ApplicationRouter.Route(request.Effect, sources, discovery, _recipeCatalog);
    }

    private OperationResult? RevalidateApplication(
        ApplicationImportPlan plan,
        DropFilesRequest request,
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        WindowDiscoveryResult recheck = _windowDiscovery!.Discover(request.Target.ApplicationSurface!, cancellationToken);
        if (!recheck.Found || recheck.Window is null || !recheck.Window.SameObjectAs(plan.Target))
        {
            return OperationResult.Refused(
                context.CorrelationId,
                new RefusalInfo(DropFilesRefusalCode.TargetIdentityChanged, "The target window identity changed before execution."));
        }

        SourceSnapshot source = _discovery.DiscoverSource(plan.SourcePath, cancellationToken);
        if (source.Status != SourceStatus.PhysicalFile
            || source.Identity is null
            || !source.Identity.SameObjectAs(plan.SourceIdentity)
            || source.ByteLength != plan.ByteLength)
        {
            return OperationResult.Refused(
                context.CorrelationId,
                new RefusalInfo(DropFilesRefusalCode.StaleIdentity, "A source file identity changed before execution."));
        }

        return null;
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

    private static OperationResult MapApplication(
        ApplicationImportPlan plan,
        ApplicationImportExecution execution,
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAt = context.Clock.UtcNow;
        List<VerificationEvidence> evidence =
        [
            new VerificationEvidence(
                VerificationEvidenceKind.OsApiReturn,
                "uia_file_dialog_import",
                $"UI Automation file-dialog import reached phase {execution.LastPhase}.",
                observedAt)
        ];

        ImportObservation? observation = execution.Observation;
        bool textMatched = observation is not null && observation.DocumentTextMatches;
        bool textRead = observation is not null && observation.DocumentText is not null;
        bool sourceRemoved = observation is not null && observation.SourceRemoved;

        if (!execution.TargetAlive)
        {
            evidence.Add(new VerificationEvidence(
                VerificationEvidenceKind.DiagnosticArtifact,
                DropFilesRefusalCode.TargetExited,
                "The target process exited during the import.",
                observedAt));
        }

        if (textMatched)
        {
            evidence.Add(new VerificationEvidence(
                VerificationEvidenceKind.ExternalSideEffectObservation,
                "document_text_observed",
                $"The target document text matches the source file {observation!.SourcePath}.",
                observedAt,
                observation.SourcePath));
        }
        else if (textRead)
        {
            evidence.Add(new VerificationEvidence(
                VerificationEvidenceKind.DiagnosticArtifact,
                DropFilesRefusalCode.VerificationFailed,
                "The target document text did not match the source file content.",
                observedAt));
        }
        else if (observation is not null)
        {
            evidence.Add(new VerificationEvidence(
                VerificationEvidenceKind.DiagnosticArtifact,
                DropFilesRefusalCode.VerificationUnavailable,
                "The target document text could not be read.",
                observedAt));
        }

        if (sourceRemoved)
        {
            evidence.Add(new VerificationEvidence(
                VerificationEvidenceKind.ExternalSideEffectObservation,
                "source_removed_observed",
                $"Source path no longer exists at {observation!.SourcePath}.",
                observedAt,
                observation.SourcePath));
        }

        if (plan.Effect == TransferEffect.Copy && textMatched)
        {
            return OperationResult.Committed(context.CorrelationId, evidence);
        }

        if (plan.Effect == TransferEffect.Move && textMatched && sourceRemoved)
        {
            return OperationResult.Committed(context.CorrelationId, evidence);
        }

        if (plan.Effect == TransferEffect.Move && textMatched)
        {
            evidence.Add(new VerificationEvidence(
                VerificationEvidenceKind.DiagnosticArtifact,
                DropFilesRefusalCode.SourceRemovalFailed,
                "The import completed but the source file was not removed.",
                observedAt));
            return OperationResult.Indeterminate(context.CorrelationId, evidence);
        }

        if (execution.Cancelled || cancellationToken.IsCancellationRequested)
        {
            return Cancelled(context, evidence);
        }

        if (!execution.TargetAlive)
        {
            return OperationResult.Indeterminate(context.CorrelationId, evidence);
        }

        if (execution.LastPhase >= ApplicationImportPhase.DialogShown)
        {
            return OperationResult.Indeterminate(context.CorrelationId, evidence);
        }

        if (execution.LastPhase == ApplicationImportPhase.WindowResolved)
        {
            return OperationResult.Failed(
                context.CorrelationId,
                new FailureInfo(
                    DropFilesRefusalCode.UiAutomationUnsupported,
                    "The target did not expose a File > Open menu action through UI Automation.",
                    isTransient: false),
                evidence);
        }

        if (execution.LastPhase == ApplicationImportPhase.MenuInvoked)
        {
            return OperationResult.Failed(
                context.CorrelationId,
                new FailureInfo(
                    DropFilesRefusalCode.ImportDialogFailed,
                    "The Open dialog did not appear after invoking File > Open.",
                    isTransient: false),
                evidence);
        }

        return OperationResult.Indeterminate(context.CorrelationId, evidence);
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
