namespace Compuse.Filesystem;

public sealed class TransferExecution
{
    public TransferExecution(int apiHresult, bool anyAborted, IReadOnlyList<ItemObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ApiHresult = apiHresult;
        AnyAborted = anyAborted;
        Observations = observations;
        ApiSucceeded = apiHresult >= 0 && !anyAborted;
    }

    public int ApiHresult { get; }

    public bool ApiSucceeded { get; }

    public bool AnyAborted { get; }

    public IReadOnlyList<ItemObservation> Observations { get; }
}
