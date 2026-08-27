using System.Text;
using Compuse.Contracts;
using Compuse.Discovery;
using Compuse.DropFiles;
using Compuse.Filesystem;
using Compuse.Protocol;
using Compuse.Requests;
using Compuse.Routing;
using Compuse.Runtime;
using Google.Protobuf;

namespace Compuse.Cli;

public static class CliApplication
{
    public const int ExitOk = 0;
    public const int ExitInvalid = 1;
    public const int ExitRefused = 2;
    public const int ExitFailed = 3;
    public const int ExitIndeterminate = 4;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        Stream stdin,
        Stream stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        byte[]? protoPayload = null;
        bool mayNeedProto = false;
        for (int index = 0; index < args.Count; index++)
        {
            if (args[index] == "--proto")
            {
                mayNeedProto = true;
                break;
            }
        }

        if (mayNeedProto)
        {
            protoPayload = ReadAll(stdin);
        }

        ParsedCommand parsed = CommandParser.Parse(args, protoPayload);
        if (parsed.ShowHelp)
        {
            stderr.Write(HelpText());
            return ExitOk;
        }

        if (parsed.Error is not null)
        {
            stderr.WriteLine(parsed.Error);
            stderr.Write(HelpText());
            return ExitInvalid;
        }

        DropFilesRequest request = parsed.Request!;
        WindowsFilesystemDiscovery discovery = new();
        DropFilesHandler handler = new(discovery, new FilesystemTransferBackend(discovery));
        if (parsed.PlanOnly)
        {
            RouteDecision decision = handler.Plan(request, cancellationToken);
            WritePlan(stdout, stderr, decision);
            return decision.IsPlan ? ExitOk : ExitRefused;
        }

        await using OperationRuntime runtime = new(new SystemOperationClock());
        runtime.Register(handler);
        OperationResult result = await runtime.RunAsync(
            request,
            request.CorrelationId,
            parsed.DeadlineUtc ?? request.DeadlineUtc,
            cancellationToken);
        WriteResult(stdout, stderr, result, parsed.Proto);
        return result.Outcome switch
        {
            OperationOutcome.Committed => ExitOk,
            OperationOutcome.Refused => ExitRefused,
            OperationOutcome.Failed => ExitFailed,
            OperationOutcome.Indeterminate => ExitIndeterminate,
            _ => ExitFailed
        };
    }

    public static string HelpText() =>
        """
        Usage:
          compuse drop-files --copy|--move --to <dir> [--plan] [--timeout <seconds>] [--correlation <guid>] <file>...
          compuse drop-files --proto [--plan] [--timeout <seconds>]

        Source and destination paths may be relative; they are resolved against the current directory.
        Outcomes are written to stdout as key=value lines, including evidence after execute.
        Diagnostics are written to stderr.
        """ + Environment.NewLine;

    private static void WritePlan(Stream stdout, TextWriter stderr, RouteDecision decision)
    {
        StreamWriter writer = new(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        if (decision.Plan is ExecutionPlan plan)
        {
            writer.WriteLine("outcome=plan");
            writer.WriteLine($"backend={plan.BackendId}");
            writer.WriteLine($"effect={plan.Effect.ToString().ToLowerInvariant()}");
            writer.WriteLine($"verification={plan.VerificationStrategy}");
            writer.WriteLine($"destination={plan.DestinationIdentity.NormalizedPath}");
            for (int index = 0; index < plan.Items.Count; index++)
            {
                PlannedItem item = plan.Items[index];
                writer.WriteLine($"source={item.SourcePath}");
                writer.WriteLine($"destination_file={item.DestinationPath}");
            }
        }
        else
        {
            writer.WriteLine("outcome=refused");
            writer.WriteLine($"code={decision.Refusal!.Code}");
            writer.WriteLine($"message={decision.Refusal.Message}");
            stderr.WriteLine(decision.Refusal.Message);
        }

        writer.Flush();
    }

    private static void WriteResult(Stream stdout, TextWriter stderr, OperationResult result, bool proto)
    {
        if (proto)
        {
            byte[] payload = OperationResultProtoMapper.ToProto(result).ToByteArray();
            stdout.Write(payload);
            stdout.Flush();
            return;
        }

        StreamWriter writer = new(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        writer.WriteLine($"outcome={result.Outcome.ToString().ToLowerInvariant()}");
        writer.WriteLine($"correlation={result.CorrelationId}");
        if (result.Refusal is not null)
        {
            writer.WriteLine($"code={result.Refusal.Code}");
            stderr.WriteLine(result.Refusal.Message);
        }
        else if (result.Failure is not null)
        {
            writer.WriteLine($"code={result.Failure.Code}");
            stderr.WriteLine(result.Failure.Message);
        }

        for (int index = 0; index < result.Evidence.Count; index++)
        {
            VerificationEvidence item = result.Evidence[index];
            writer.WriteLine($"evidence={KindToken(item.Kind)}:{item.Code}");
            if (item.ArtifactReference is not null)
            {
                writer.WriteLine($"artifact={item.ArtifactReference}");
            }
        }

        writer.Flush();
    }

    private static string KindToken(VerificationEvidenceKind kind) =>
        kind switch
        {
            VerificationEvidenceKind.OsApiReturn => "os_api_return",
            VerificationEvidenceKind.ExternalSideEffectObservation => "external_side_effect_observation",
            VerificationEvidenceKind.DiagnosticArtifact => "diagnostic_artifact",
            _ => "unknown"
        };

    private static byte[] ReadAll(Stream stdin)
    {
        using MemoryStream buffer = new();
        stdin.CopyTo(buffer);
        return buffer.ToArray();
    }
}
