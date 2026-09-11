using System.Reflection;

namespace Compuse.Runtime.Tests;

[TestClass]
public sealed class OperationRuntimeOptionsTests
{
    [TestMethod]
    public void DefaultsAreResolvedWhenArgumentsAreOmitted()
    {
        OperationRuntimeOptions options = new();
        Assert.AreEqual(32, options.MaxConcurrentOperations);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.MaxExecution);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.ShutdownDrain);
        Assert.AreEqual(TimeSpan.FromSeconds(1), options.CleanupTimeout);
    }

    [TestMethod]
    public void ExplicitValuesArePreserved()
    {
        OperationRuntimeOptions options = new(
            maxConcurrentOperations: 4,
            maxExecution: TimeSpan.FromSeconds(12),
            shutdownDrain: TimeSpan.FromSeconds(3),
            cleanupTimeout: TimeSpan.FromMilliseconds(500));
        Assert.AreEqual(4, options.MaxConcurrentOperations);
        Assert.AreEqual(TimeSpan.FromSeconds(12), options.MaxExecution);
        Assert.AreEqual(TimeSpan.FromSeconds(3), options.ShutdownDrain);
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), options.CleanupTimeout);
    }

    [TestMethod]
    public void ConcurrentBoundsAreRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OperationRuntimeOptions(0));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OperationRuntimeOptions(1025));
    }

    [TestMethod]
    public void NonPositiveAndOverlongDurationsAreRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OperationRuntimeOptions(maxExecution: TimeSpan.Zero));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OperationRuntimeOptions(shutdownDrain: TimeSpan.FromHours(1).Add(TimeSpan.FromTicks(1))));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OperationRuntimeOptions(cleanupTimeout: TimeSpan.FromSeconds(-1)));
    }
}

[TestClass]
public sealed class RuntimeOutcomeCodeTests
{
    [TestMethod]
    public void PublicCodesAreExact()
    {
        Dictionary<string, string> expected = new()
        {
            ["Cancelled"] = "cancelled",
            ["DeadlineExpired"] = "deadline_expired",
            ["DuplicateCorrelation"] = "duplicate_correlation",
            ["HandlerCorrelationMismatch"] = "handler_correlation_mismatch",
            ["HandlerFault"] = "handler_fault",
            ["HandlerNullResult"] = "handler_null_result",
            ["RuntimeBusy"] = "runtime_busy",
            ["RuntimeStopping"] = "runtime_stopping",
            ["ShutdownInterrupted"] = "shutdown_interrupted",
            ["UnsupportedRequest"] = "unsupported_request"
        };

        FieldInfo[] fields = typeof(RuntimeOutcomeCode).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.AreEqual(expected.Count, fields.Length);
        foreach (KeyValuePair<string, string> pair in expected)
        {
            FieldInfo field = typeof(RuntimeOutcomeCode).GetField(pair.Key)!;
            Assert.AreEqual(pair.Value, (string)field.GetValue(null)!);
        }
    }
}

[TestClass]
public sealed class SystemOperationClockTests
{
    [TestMethod]
    public void UtcNowHasZeroOffset()
    {
        SystemOperationClock clock = new();
        Assert.AreEqual(TimeSpan.Zero, clock.UtcNow.Offset);
    }

    [TestMethod]
    public async Task ZeroOrNegativeDelayCompletesWithoutThrowing()
    {
        SystemOperationClock clock = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        await clock.Delay(TimeSpan.Zero, cts.Token);
        await clock.Delay(TimeSpan.FromMilliseconds(-1), CancellationToken.None);
    }
}

[TestClass]
public sealed class PublicSurfaceTests
{
    [TestMethod]
    public void RuntimeAssemblyExportsOnlySpecifiedTypes()
    {
        string[] names = [.. typeof(OperationRuntime).Assembly.GetExportedTypes()
            .Select(static type => type.IsGenericType ? type.GetGenericTypeDefinition().FullName! : type.FullName!)
            .OrderBy(static name => name, StringComparer.Ordinal)];
        string[] expected =
        [
            "Compuse.Runtime.IOperationClock",
            "Compuse.Runtime.IOperationHandler`1",
            "Compuse.Runtime.OperationExecutionContext",
            "Compuse.Runtime.OperationRuntime",
            "Compuse.Runtime.OperationRuntimeOptions",
            "Compuse.Runtime.RuntimeOutcomeCode",
            "Compuse.Runtime.SystemOperationClock"
        ];
        CollectionAssert.AreEqual(expected, names);
    }

    [TestMethod]
    public void RuntimeProjectDoesNotReferenceRequests()
    {
        Assert.IsNull(typeof(OperationRuntime).Assembly.GetType("Compuse.Requests.DropFilesRequest"));
        Assert.IsNotNull(typeof(Compuse.Requests.DropFilesRequest));
    }
}
