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
        string? error)
    {
        ShowHelp = showHelp;
        PlanOnly = planOnly;
        Proto = proto;
        Request = request;
        Error = error;
    }

    public bool ShowHelp { get; }

    public bool PlanOnly { get; }

    public bool Proto { get; }

    public DropFilesRequest? Request { get; }

    public string? Error { get; }
}

internal static class CommandParser
{
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
        string? correlation = null;
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

            if (token == "--correlation")
            {
                if (index + 1 >= tokens.Count)
                {
                    return Error("Missing value for --correlation.");
                }

                correlation = tokens[++index];
                continue;
            }

            if (token.StartsWith('-'))
            {
                return Error($"Unknown argument '{token}'.");
            }

            files.Add(token);
        }

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
            if (copy || move || to is not null || files.Count > 0 || correlation is not null)
            {
                return Error("--proto cannot be combined with --copy, --move, --to, --correlation, or source files.");
            }

            if (protoPayload is null || protoPayload.Length == 0)
            {
                return Error("A --proto invocation requires a drop_files Protobuf payload on stdin.");
            }

            try
            {
                Compuse.Protocol.V1.DropFilesRequest message = Compuse.Protocol.V1.DropFilesRequest.Parser.ParseFrom(protoPayload);
                DropFilesRequest request = Compuse.Protocol.DropFilesRequestProtoMapper.FromProto(message);
                return new ParsedCommand(showHelp: false, plan, proto: true, request, error: null);
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

        if (string.IsNullOrWhiteSpace(to))
        {
            return Error("--to is required.");
        }

        if (files.Count == 0)
        {
            return Error("At least one source file is required.");
        }

        try
        {
            CorrelationId correlationId = correlation is null ? CorrelationId.New() : CorrelationId.Parse(correlation);
            List<SourceItem> sources = new(files.Count);
            for (int index = 0; index < files.Count; index++)
            {
                sources.Add(new SourceItem(new PhysicalFileSource(files[index])));
            }

            DropFilesRequest request = DropFilesRequest.Create(
                correlationId,
                sources,
                copy ? TransferEffect.Copy : TransferEffect.Move,
                TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(to)));
            return new ParsedCommand(showHelp: false, plan, proto: false, request, error: null);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return Error(ex.Message);
        }
    }

    private static ParsedCommand Error(string message) =>
        new(showHelp: false, planOnly: false, proto: false, request: null, message);
}
