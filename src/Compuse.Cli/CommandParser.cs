using System.Globalization;
using Compuse.Contracts;
using Compuse.Requests;

namespace Compuse.Cli;

internal sealed class ParsedCommand
{
    public ParsedCommand(
        bool showHelp,
        bool planOnly,
        bool proto,
        DropFilesRequest? request,
        string? error,
        DateTimeOffset? deadlineUtc = null)
    {
        ShowHelp = showHelp;
        PlanOnly = planOnly;
        Proto = proto;
        Request = request;
        Error = error;
        DeadlineUtc = deadlineUtc;
    }

    public bool ShowHelp { get; }

    public bool PlanOnly { get; }

    public bool Proto { get; }

    public DropFilesRequest? Request { get; }

    public string? Error { get; }

    public DateTimeOffset? DeadlineUtc { get; }
}

internal static class CommandParser
{
    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 86400;

    internal static ParsedCommand Parse(IReadOnlyList<string> args, byte[]? protoPayload)
    {
        ArgumentNullException.ThrowIfNull(args);
        List<string> tokens = [.. args];
        if (tokens.Count > 0 && string.Equals(tokens[0], "drop-files", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }

        bool help = false;
        bool plan = false;
        bool proto = false;
        bool copy = false;
        bool move = false;
        string? to = null;
        string? app = null;
        string? windowClass = null;
        string? windowTitle = null;
        ulong? hwndHint = null;
        uint? processIdHint = null;
        string? correlation = null;
        int? timeoutSeconds = null;
        List<string> files = [];
        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (token is "-h" or "--help")
            {
                help = true;
                continue;
            }

            if (token == "--plan")
            {
                plan = true;
                continue;
            }

            if (token == "--proto")
            {
                proto = true;
                continue;
            }

            if (token == "--copy")
            {
                copy = true;
                continue;
            }

            if (token == "--move")
            {
                move = true;
                continue;
            }

            if (token == "--to")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --to.");
                }

                to = tokens[++index];
                continue;
            }

            if (token == "--app")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --app.");
                }

                app = tokens[++index];
                continue;
            }

            if (token == "--window-class")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --window-class.");
                }

                windowClass = tokens[++index];
                continue;
            }

            if (token == "--window-title")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --window-title.");
                }

                windowTitle = tokens[++index];
                continue;
            }

            if (token == "--hwnd")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --hwnd.");
                }

                string raw = tokens[++index];
                string digits = raw;
                NumberStyles styles = NumberStyles.Integer;
                if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    digits = raw[2..];
                    styles = NumberStyles.HexNumber;
                }

                if (!ulong.TryParse(digits, styles, CultureInfo.InvariantCulture, out ulong parsed)
                    || parsed == 0)
                {
                    return Error("--hwnd must be a nonzero unsigned integer window handle.");
                }

                hwndHint = parsed;
                continue;
            }

            if (token == "--pid")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --pid.");
                }

                string raw = tokens[++index];
                if (!uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed)
                    || parsed == 0)
                {
                    return Error("--pid must be a nonzero unsigned integer process id.");
                }

                processIdHint = parsed;
                continue;
            }

            if (token == "--correlation")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --correlation.");
                }

                correlation = tokens[++index];
                continue;
            }

            if (token == "--timeout")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --timeout.");
                }

                string raw = tokens[++index];
                if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
                    || seconds < MinTimeoutSeconds
                    || seconds > MaxTimeoutSeconds)
                {
                    return Error("--timeout must be an integer number of seconds between 1 and 86400.");
                }

                timeoutSeconds = seconds;
                continue;
            }

            if (token.StartsWith('-'))
            {
                return Error($"Unknown argument '{token}'.");
            }

            files.Add(token);
        }

        DateTimeOffset? deadlineUtc = timeoutSeconds is int timeout
            ? DateTimeOffset.UtcNow.AddSeconds(timeout)
            : null;

        if (help)
        {
            return new ParsedCommand(showHelp: true, planOnly: false, proto: false, request: null, error: null);
        }

        if (tokens.Count == 0)
        {
            return new ParsedCommand(showHelp: true, planOnly: false, proto: false, request: null, error: null);
        }

        if (proto)
        {
            if (copy || move || to is not null || app is not null || windowClass is not null || windowTitle is not null
                || hwndHint is not null || processIdHint is not null || files.Count > 0 || correlation is not null)
            {
                return Error("--proto cannot be combined with --copy, --move, --to, --app, window hints, --correlation, or source files.");
            }

            if (protoPayload is null || protoPayload.Length == 0)
            {
                return Error("A --proto invocation requires a drop_files Protobuf payload on stdin.");
            }

            try
            {
                Compuse.Protocol.V1.DropFilesRequest message =
                    Compuse.Protocol.V1.DropFilesRequest.Parser.ParseFrom(protoPayload);
                DropFilesRequest request = Compuse.Protocol.DropFilesRequestProtoMapper.FromProto(message);
                return new ParsedCommand(
                    showHelp: false,
                    plan,
                    proto: true,
                    request,
                    error: null,
                    deadlineUtc);
            }
            catch (Exception ex) when (ex is Google.Protobuf.InvalidProtocolBufferException
                or Compuse.Protocol.ProtocolContractException
                or ArgumentException
                or FormatException)
            {
                return Error($"Invalid drop_files Protobuf payload: {ex.Message}");
            }
        }

        if (copy == move)
        {
            return Error("Specify exactly one of --copy or --move.");
        }

        if ((to is null) == (app is null))
        {
            return Error("--to is required when --app is not used. Specify exactly one of --to <directory> or --app <process-image-name>.");
        }

        if (app is null && (windowClass is not null || windowTitle is not null || hwndHint is not null || processIdHint is not null))
        {
            return Error("Window hints require --app.");
        }

        if (files.Count == 0)
        {
            return Error("At least one source file is required.");
        }

        try
        {
            CorrelationId correlationId = correlation is null ? CorrelationId.New() : CorrelationId.Parse(correlation);
            TargetSelector target;
            if (app is not null)
            {
                target = TargetSelector.FromApplicationSurface(
                    new ApplicationSurfaceTarget(app, windowClass, windowTitle, hwndHint, processIdHint));
            }
            else
            {
                string destination = CliPaths.ResolveAbsolute(to!);
                target = TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destination));
            }

            List<SourceItem> sources = new(files.Count);
            for (int index = 0; index < files.Count; index++)
            {
                string source = CliPaths.ResolveAbsolute(files[index]);
                sources.Add(new SourceItem(new PhysicalFileSource(source)));
            }

            DropFilesRequest request = DropFilesRequest.Create(
                correlationId,
                sources,
                copy ? TransferEffect.Copy : TransferEffect.Move,
                target,
                deadlineUtc);
            return new ParsedCommand(showHelp: false, plan, proto: false, request, error: null, deadlineUtc);
        }
        catch (Exception ex) when (ex is ArgumentException
            or FormatException
            or NotSupportedException
            or PathTooLongException)
        {
            return Error(ex.Message);
        }
    }

    private static ParsedCommand Error(string message) =>
        new(showHelp: false, planOnly: false, proto: false, request: null, message);
}
