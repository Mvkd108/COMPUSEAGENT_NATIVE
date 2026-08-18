using Compuse.Contracts;
using Compuse.Requests;
using Google.Protobuf.WellKnownTypes;
using DomainDropFilesRequest = Compuse.Requests.DropFilesRequest;
using DomainTransferEffect = Compuse.Requests.TransferEffect;
using ProtoApplicationSurfaceTarget = Compuse.Protocol.V1.ApplicationSurfaceTarget;
using ProtoDropFilesRequest = Compuse.Protocol.V1.DropFilesRequest;
using ProtoFilesystemContainerTarget = Compuse.Protocol.V1.FilesystemContainerTarget;
using ProtoPhysicalFileSource = Compuse.Protocol.V1.PhysicalFileSource;
using ProtoSourceItem = Compuse.Protocol.V1.SourceItem;
using ProtoTargetSelector = Compuse.Protocol.V1.TargetSelector;
using ProtoTransferEffect = Compuse.Protocol.V1.TransferEffect;

namespace Compuse.Protocol;

public static class DropFilesRequestProtoMapper
{
    private const long UnixEpochTicks = 621_355_968_000_000_000;
    private const long MinBclTicks = 0;
    private const long MaxBclTicks = 3_155_378_975_999_999_999;
    private const long TicksPerSecond = 10_000_000;
    private const int NanosecondsPerTick = 100;
    private const int MaxNanos = 999_999_999;

    public static ProtoDropFilesRequest ToProto(DomainDropFilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProtoDropFilesRequest message = new()
        {
            CorrelationId = request.CorrelationId.ToString(),
            Effect = ToProtoEffect(request.Effect),
            Target = ToProtoTarget(request.Target)
        };

        foreach (SourceItem item in request.Sources)
        {
            message.Sources.Add(ToProtoSource(item));
        }

        if (request.DeadlineUtc is DateTimeOffset deadline)
        {
            message.Deadline = Timestamp.FromDateTimeOffset(deadline);
        }

        return message;
    }

    public static DomainDropFilesRequest FromProto(ProtoDropFilesRequest message)
    {
        ArgumentNullException.ThrowIfNull(message);

        CorrelationId correlationId = ParseCorrelationId(message.CorrelationId);
        DomainTransferEffect effect = FromProtoEffect(message.Effect);
        DateTimeOffset? deadlineUtc = FromProtoDeadline(message);
        TargetSelector target = FromProtoTarget(message.Target);

        List<SourceItem> sources = new(message.Sources.Count);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        if (message.Sources.Count is < DropFilesRequestLimits.MinSourceCount
            or > DropFilesRequestLimits.MaxSourceCount)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidSource,
                "sources",
                "A drop_files request requires 1 to 1024 source items.");
        }

        for (int index = 0; index < message.Sources.Count; index++)
        {
            SourceItem item = FromProtoSource(message.Sources[index], index);
            if (!seen.Add(item.PhysicalFile.AbsolutePath))
            {
                throw Create(
                    ProtocolContractErrorCode.InvalidSource,
                    $"sources[{index}].physical_file.absolute_path",
                    "Source items must not contain duplicate physical-file paths.");
            }

            sources.Add(item);
        }

        try
        {
            return DomainDropFilesRequest.Create(correlationId, sources, effect, target, deadlineUtc);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "effect")
        {
            throw Create(
                ProtocolContractErrorCode.UnsupportedTransferEffect,
                "effect",
                "The transfer effect is not a supported semantic value.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "deadlineUtc")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidDeadline,
                "deadline",
                "The request deadline is missing or not exactly representable.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "sources")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidSource,
                "sources",
                "A drop_files request requires 1 to 1024 unique physical-file sources.",
                ex);
        }
        catch (ArgumentNullException ex) when (ex.ParamName == "target")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target",
                "A drop_files request requires exactly one target selector.",
                ex);
        }
    }

    private static ProtoTransferEffect ToProtoEffect(DomainTransferEffect effect)
    {
        return effect switch
        {
            DomainTransferEffect.Copy => ProtoTransferEffect.Copy,
            DomainTransferEffect.Move => ProtoTransferEffect.Move,
            _ => throw new InvalidOperationException("Transfer effect must be a defined managed value.")
        };
    }

    private static DomainTransferEffect FromProtoEffect(ProtoTransferEffect effect)
    {
        if (!System.Enum.IsDefined(effect) || effect == ProtoTransferEffect.Unspecified)
        {
            throw Create(
                ProtocolContractErrorCode.UnsupportedTransferEffect,
                "effect",
                "The transfer effect is not a supported semantic value.");
        }

        return effect switch
        {
            ProtoTransferEffect.Copy => DomainTransferEffect.Copy,
            ProtoTransferEffect.Move => DomainTransferEffect.Move,
            _ => throw Create(
                ProtocolContractErrorCode.UnsupportedTransferEffect,
                "effect",
                "The transfer effect is not a supported semantic value.")
        };
    }

    private static ProtoSourceItem ToProtoSource(SourceItem item) => new()
    {
        PhysicalFile = new ProtoPhysicalFileSource
        {
            AbsolutePath = item.PhysicalFile.AbsolutePath
        }
    };

    private static SourceItem FromProtoSource(ProtoSourceItem item, int index)
    {
        if (item.IdentityCase != ProtoSourceItem.IdentityOneofCase.PhysicalFile)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidSource,
                $"sources[{index}].identity",
                "The source identity is not a supported semantic value.");
        }

        try
        {
            return new SourceItem(new PhysicalFileSource(item.PhysicalFile.AbsolutePath));
        }
        catch (ArgumentException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidSource,
                $"sources[{index}].physical_file.absolute_path",
                "The source path is not a valid absolute Windows path.",
                ex);
        }
    }

    private static ProtoTargetSelector ToProtoTarget(TargetSelector target)
    {
        return target.Kind switch
        {
            TargetSelectorKind.FilesystemContainer => new ProtoTargetSelector
            {
                FilesystemContainer = new ProtoFilesystemContainerTarget
                {
                    AbsolutePath = target.FilesystemContainer!.AbsolutePath
                }
            },
            TargetSelectorKind.ApplicationSurface => new ProtoTargetSelector
            {
                ApplicationSurface = ToProtoApplicationSurface(target.ApplicationSurface!)
            },
            _ => throw new InvalidOperationException("Target selector kind must be a defined managed value.")
        };
    }

    private static ProtoApplicationSurfaceTarget ToProtoApplicationSurface(ApplicationSurfaceTarget target)
    {
        ProtoApplicationSurfaceTarget proto = new()
        {
            ProcessImageName = target.ProcessImageName
        };

        if (target.WindowClass is not null)
        {
            proto.WindowClass = target.WindowClass;
        }

        if (target.WindowTitle is not null)
        {
            proto.WindowTitle = target.WindowTitle;
        }

        if (target.HwndHint is ulong hwndHint)
        {
            proto.HwndHint = hwndHint;
        }

        if (target.ProcessIdHint is uint processIdHint)
        {
            proto.ProcessIdHint = processIdHint;
        }

        return proto;
    }

    private static TargetSelector FromProtoTarget(ProtoTargetSelector? target)
    {
        if (target is null || target.SelectorCase == ProtoTargetSelector.SelectorOneofCase.None)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target",
                "A drop_files request requires exactly one target selector.");
        }

        return target.SelectorCase switch
        {
            ProtoTargetSelector.SelectorOneofCase.FilesystemContainer => FromProtoFilesystemContainer(target.FilesystemContainer),
            ProtoTargetSelector.SelectorOneofCase.ApplicationSurface => FromProtoApplicationSurface(target.ApplicationSurface),
            _ => throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target",
                "A drop_files request requires exactly one target selector.")
        };
    }

    private static TargetSelector FromProtoFilesystemContainer(ProtoFilesystemContainerTarget target)
    {
        try
        {
            return TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(target.AbsolutePath));
        }
        catch (ArgumentException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target.filesystem_container.absolute_path",
                "The filesystem container path is not a valid absolute Windows path.",
                ex);
        }
    }

    private static TargetSelector FromProtoApplicationSurface(ProtoApplicationSurfaceTarget target)
    {
        string? windowClass = target.HasWindowClass ? target.WindowClass : null;
        string? windowTitle = target.HasWindowTitle ? target.WindowTitle : null;
        ulong? hwndHint = target.HasHwndHint ? target.HwndHint : null;
        uint? processIdHint = target.HasProcessIdHint ? target.ProcessIdHint : null;

        try
        {
            return TargetSelector.FromApplicationSurface(
                new ApplicationSurfaceTarget(
                    target.ProcessImageName,
                    windowClass,
                    windowTitle,
                    hwndHint,
                    processIdHint));
        }
        catch (ArgumentException ex) when (ex.ParamName == "processImageName")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target.application_surface.process_image_name",
                "The process image name is not a valid file name.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "windowClass")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target.application_surface.window_class",
                "The window class is not a valid contract value.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "windowTitle")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target.application_surface.window_title",
                "The window title is not a valid contract value.",
                ex);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "hwndHint")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target.application_surface.hwnd_hint",
                "A window handle hint cannot be zero when provided.",
                ex);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "processIdHint")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTarget,
                "target.application_surface.process_id_hint",
                "A process id hint cannot be zero when provided.",
                ex);
        }
    }

    private static CorrelationId ParseCorrelationId(string value)
    {
        CorrelationId parsed;
        try
        {
            parsed = CorrelationId.Parse(value);
        }
        catch (ArgumentNullException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.",
                ex);
        }
        catch (FormatException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.",
                ex);
        }
        catch (ArgumentException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.",
                ex);
        }

        if (!string.Equals(value, parsed.ToString(), StringComparison.Ordinal))
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.");
        }

        return parsed;
    }

    private static DateTimeOffset? FromProtoDeadline(ProtoDropFilesRequest message)
    {
        if (message.Deadline is null)
        {
            return null;
        }

        return FromProtoTimestamp(message.Deadline, "deadline");
    }

    private static DateTimeOffset FromProtoTimestamp(Timestamp? timestamp, string fieldPath)
    {
        if (timestamp is null)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidDeadline,
                fieldPath,
                "The request deadline is missing or not exactly representable.");
        }

        if (timestamp.Nanos is < 0 or > MaxNanos || timestamp.Nanos % NanosecondsPerTick != 0)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidDeadline,
                fieldPath,
                "The request deadline is missing or not exactly representable.");
        }

        long tickOffset;
        try
        {
            checked
            {
                tickOffset = (timestamp.Seconds * TicksPerSecond) + (timestamp.Nanos / NanosecondsPerTick);
            }
        }
        catch (OverflowException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidDeadline,
                fieldPath,
                "The request deadline is missing or not exactly representable.",
                ex);
        }

        long ticks;
        try
        {
            checked
            {
                ticks = UnixEpochTicks + tickOffset;
            }
        }
        catch (OverflowException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidDeadline,
                fieldPath,
                "The request deadline is missing or not exactly representable.",
                ex);
        }

        if (ticks is < MinBclTicks or > MaxBclTicks)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidDeadline,
                fieldPath,
                "The request deadline is missing or not exactly representable.");
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static ProtocolContractException Create(
        ProtocolContractErrorCode code,
        string fieldPath,
        string message,
        Exception? innerException = null) =>
        new(code, fieldPath, message, innerException);
}
