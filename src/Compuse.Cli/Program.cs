namespace Compuse.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        return await CliApplication.RunAsync(
            args,
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            Console.Error,
            cts.Token);
    }
}
