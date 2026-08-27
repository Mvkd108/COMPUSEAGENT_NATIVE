using System.Reflection;
using Compuse.Contracts;
using Compuse.Runtime;

namespace Compuse.DropFiles.Tests;

internal static class HandlerContext
{
    internal static OperationExecutionContext Create(CorrelationId correlation, IOperationClock clock)
    {
        ConstructorInfo constructor = typeof(OperationExecutionContext)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        DateTimeOffset now = clock.UtcNow;
        return (OperationExecutionContext)constructor.Invoke(
            [correlation, now, now.AddHours(1), clock]);
    }
}
