using System.Text;
using Compuse.Contracts;
using Compuse.Protocol;
using Compuse.Requests;
using Google.Protobuf;

namespace Compuse.Cli.Tests;

[TestClass]
public sealed class CliApplicationTests
{
    [TestMethod]
    public async Task HelpExitsZero()
    {
        (int exit, string stdout, string stderr) = await Run(["drop-files", "--help"]);
        Assert.AreEqual(CliApplication.ExitOk, exit);
        StringAssert.Contains(stderr, "Usage:");
        Assert.AreEqual("", stdout);
    }

    [TestMethod]
    public async Task InvalidArgsExitOne()
    {
        (int exit, _, string stderr) = await Run(["drop-files", "--copy"]);
        Assert.AreEqual(CliApplication.ExitInvalid, exit);
        StringAssert.Contains(stderr, "--to is required");
    }

    [TestMethod]
    public async Task PlanCopyWritesRouteWithoutMutating()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        (int exit, string stdout, _) = await Run(
        [
            "drop-files",
            "--plan",
            "--copy",
            "--to",
            destDir,
            source
        ]);
        Assert.AreEqual(CliApplication.ExitOk, exit);
        StringAssert.Contains(stdout, "outcome=plan");
        StringAssert.Contains(stdout, "backend=filesystem");
        StringAssert.Contains(stdout, "effect=copy");
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
        Assert.IsTrue(System.IO.File.Exists(source));
    }

    [TestMethod]
    public async Task ExecuteCopyWritesCommittedOutcome()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        (int exit, string stdout, _) = await Run(
        [
            "drop-files",
            "--copy",
            "--to",
            destDir,
            source
        ]);
        Assert.AreEqual(CliApplication.ExitOk, exit);
        StringAssert.Contains(stdout, "outcome=committed");
        Assert.AreEqual("payload", System.IO.File.ReadAllText(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task ProtoExecuteRoundTripsCanonicalResult()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        DropFilesRequest request = DropFilesRequest.Create(
            CorrelationId.Parse("abcdef01-2345-6789-abcd-ef0123456789"),
            [new SourceItem(new PhysicalFileSource(source))],
            TransferEffect.Copy,
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir)));
        byte[] payload = DropFilesRequestProtoMapper.ToProto(request).ToByteArray();
        (int exit, byte[] stdout, _) = await RunBytes(["drop-files", "--proto"], payload);
        Assert.AreEqual(CliApplication.ExitOk, exit);
        OperationResult result = OperationResultProtoMapper.FromProto(
            Compuse.Protocol.V1.OperationResultEnvelope.Parser.ParseFrom(stdout));
        Assert.AreEqual(OperationOutcome.Committed, result.Outcome);
        Assert.AreEqual(request.CorrelationId, result.CorrelationId);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> Run(string[] args, byte[]? stdin = null)
    {
        (int exit, byte[] stdout, string stderr) = await RunBytes(args, stdin);
        return (exit, Encoding.UTF8.GetString(stdout), stderr);
    }

    private static async Task<(int Exit, byte[] Stdout, string Stderr)> RunBytes(string[] args, byte[]? stdin)
    {
        using MemoryStream input = new(stdin ?? []);
        using MemoryStream output = new();
        using StringWriter stderr = new();
        int exit = await CliApplication.RunAsync(args, input, output, stderr, CancellationToken.None);
        return (exit, output.ToArray(), stderr.ToString());
    }
}

internal sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "compuse-tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name, string contents = "hello")
    {
        string path = Path.Combine(Root, name);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, contents);
        return path;
    }

    public string Dir(string name)
    {
        string path = Path.Combine(Root, name);
        _ = Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
