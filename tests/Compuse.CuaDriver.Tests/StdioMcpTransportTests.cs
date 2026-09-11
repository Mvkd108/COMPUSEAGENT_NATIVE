using System.Text;

namespace Compuse.CuaDriver.Tests;

[TestClass]
public sealed class StdioMcpTransportTests
{
    private const string PowerShell = "powershell.exe";

    [TestMethod]
    public async Task StderrFloodIsBounded()
    {
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments =
            [
                "-NoProfile",
                "-Command",
                "for($i=0;$i -lt 20000;$i++){[Console]::Error.WriteLine('err '+$i)}"
            ],
            MaxDiagnosticBytes = 4096
        };
        await using StdioMcpTransport transport = new(options);

        // Deterministic: the flooder finishes writing when the child exits. A fixed
        // delay raced the drain under parallel test load (PowerShell startup alone
        // can exceed it), so wait for exit (with a fail-fast timeout), then poll the
        // bounded ring until the drain has consumed the stream and the diagnostics
        // stop changing.
        using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(30));
        await transport.WaitForExitAsync(exitTimeout.Token);
        string diagnostics = string.Empty;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            diagnostics = transport.GetDiagnostics();
            await Task.Delay(50);
            if (diagnostics.Length > 0 && diagnostics.Length == transport.GetDiagnostics().Length)
            {
                break;
            }
        }

        Assert.IsTrue(
            diagnostics.Length <= options.MaxDiagnosticBytes + 200,
            $"Diagnostics must be bounded; got {diagnostics.Length} bytes for a limit of {options.MaxDiagnosticBytes}.");
        Assert.IsTrue(diagnostics.Length > 0, "Flooded stderr should still be captured up to the bound.");
    }

    [TestMethod]
    public async Task OversizedStdoutLineIsRejected()
    {
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments = ["-NoProfile", "-Command", "[Console]::Out.WriteLine(('a' * 10000))"],
            MaxMessageBytes = 1024
        };
        await using StdioMcpTransport transport = new(options);

        _ = await Assert.ThrowsAsync<DriverProtocolException>(
            () => transport.ReadLineAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingExecutableThrowsStartFailure()
    {
        DriverConnectionOptions options = new(@"C:\definitely\missing\driver.exe");
        _ = await Assert.ThrowsAsync<DriverStartException>(() =>
        {
            StdioMcpTransport transport = new(options);
            transport.Dispose();
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task EofReturnsNull()
    {
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments = ["-NoProfile", "-Command", "$null"],
        };
        await using StdioMcpTransport transport = new(options);
        transport.CloseInput();

        byte[]? line = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNull(line);
    }

    [TestMethod]
    public async Task MultipleFramesInOneWriteAreDeliveredWithoutLossOrMerge()
    {
        // The child writes twenty newline-delimited frames in a single Write call.
        // The adapter's 8 KiB read chunk forces the write to arrive across several
        // reads, exercising multiple-frames-per-read and split-frame reassembly.
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments =
            [
                "-NoProfile",
                "-Command",
                "$s = [string]::Join(\"`n\", 0..19) + \"`n\"; [Console]::Out.Write($s)"
            ]
        };
        await using StdioMcpTransport transport = new(options);

        List<string> lines = [];
        for (int expected = 0; expected < 20; expected++)
        {
            byte[]? line = await transport.ReadLineAsync(CancellationToken.None);
            Assert.IsNotNull(line, $"Expected frame {expected} to be delivered.");
            lines.Add(Encoding.UTF8.GetString(line));
        }

        byte[]? extra = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNull(extra, "No additional frame should remain after the twenty frames.");

        Assert.AreEqual(20, lines.Count);
        for (int index = 0; index < 20; index++)
        {
            Assert.AreEqual(index.ToString(System.Globalization.CultureInfo.InvariantCulture), lines[index], $"Frame {index} must not be lost, merged, or reordered.");
        }
    }

    [TestMethod]
    public async Task SingleFrameSplitAcrossManyReadsIsReassembled()
    {
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments =
            [
                "-NoProfile",
                "-Command",
                "[Console]::Out.WriteLine(('x' * 20000))"
            ]
        };
        await using StdioMcpTransport transport = new(options);

        byte[]? line = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNotNull(line);
        Assert.AreEqual(20000, line!.Length);
        Assert.IsTrue(line.All(value => value == (byte)'x'));
    }

    [TestMethod]
    public async Task CompleteFrameFollowedByPartialFrameRetainsThePartial()
    {
        // One write delivers "alpha\nbeta-partial" then EOF.
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments = ["-NoProfile", "-Command", "[Console]::Out.Write('alpha'+[char]10+'beta-partial')"]
        };
        await using StdioMcpTransport transport = new(options);
        transport.CloseInput();

        byte[]? first = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNotNull(first);
        Assert.AreEqual("alpha", Encoding.UTF8.GetString(first!));

        byte[]? second = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNotNull(second, "The unterminated trailing frame must be returned as the final line.");
        Assert.AreEqual("beta-partial", Encoding.UTF8.GetString(second!));

        byte[]? third = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNull(third);
    }

    [TestMethod]
    public async Task CrlfFramingIsNormalized()
    {
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments = ["-NoProfile", "-Command", "[Console]::Out.Write('one'+[char]13+[char]10+'two'+[char]13+[char]10+'three')"]
        };
        await using StdioMcpTransport transport = new(options);
        transport.CloseInput();

        Assert.AreEqual("one", Encoding.UTF8.GetString((await transport.ReadLineAsync(CancellationToken.None))!));
        Assert.AreEqual("two", Encoding.UTF8.GetString((await transport.ReadLineAsync(CancellationToken.None))!));
        Assert.AreEqual("three", Encoding.UTF8.GetString((await transport.ReadLineAsync(CancellationToken.None))!));
        Assert.IsNull(await transport.ReadLineAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task EofAfterFinalUnterminatedFrameReturnsTheFrame()
    {
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments = ["-NoProfile", "-Command", "[Console]::Out.Write('unterminated')"]
        };
        await using StdioMcpTransport transport = new(options);
        transport.CloseInput();

        byte[]? line = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNotNull(line);
        Assert.AreEqual("unterminated", Encoding.UTF8.GetString(line!));
    }

    [TestMethod]
    public async Task CancellationWhilePartialFrameIsBufferedDoesNotLoseData()
    {
        // The child writes a partial frame, sleeps, then completes it. Cancelling the
        // in-flight read must keep the partial frame buffered for a later read.
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments =
            [
                "-NoProfile",
                "-Command",
                "[Console]::Out.Write('partial'); Start-Sleep -Milliseconds 2500; [Console]::Out.Write('-frame'+[char]10)"
            ]
        };
        await using StdioMcpTransport transport = new(options);
        using CancellationTokenSource cts = new();
        cts.CancelAfter(500);

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => transport.ReadLineAsync(cts.Token));

        byte[]? line = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNotNull(line, "The partial frame must survive cancellation and complete on the next read.");
        Assert.AreEqual("partial-frame", Encoding.UTF8.GetString(line!));
    }

    [TestMethod]
    public async Task ManyConcurrentFramesInOneWriteAreNotCombined()
    {
        // A large single write carrying many frames must not combine adjacent frames.
        DriverConnectionOptions options = new(PowerShell)
        {
            ServerArguments =
            [
                "-NoProfile",
                "-Command",
                "$lines = @(); for($i=0;$i -lt 500;$i++){ $lines += '{\"id\":' + $i + '}' }; [Console]::Out.Write(([string]::Join(\"`n\", $lines) + \"`n\"))"
            ]
        };
        await using StdioMcpTransport transport = new(options);

        for (int index = 0; index < 500; index++)
        {
            byte[]? line = await transport.ReadLineAsync(CancellationToken.None);
            Assert.IsNotNull(line, $"Frame {index} must be delivered.");
            string text = Encoding.UTF8.GetString(line!);
            Assert.AreEqual($"{{\"id\":{index}}}", text, "Frames must not be combined or lost.");
        }

        byte[]? extra = await transport.ReadLineAsync(CancellationToken.None);
        Assert.IsNull(extra, "No additional frame should remain after the five hundred frames.");
    }
}
