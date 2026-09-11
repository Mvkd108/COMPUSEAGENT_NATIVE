namespace Compuse.ApplicationSurface;

public sealed class ApplicationImportExecution
{
    public ApplicationImportExecution(
        ApplicationImportPhase lastPhase,
        bool targetAlive,
        ImportObservation? observation,
        bool cancelled = false)
    {
        if (!Enum.IsDefined(lastPhase))
        {
            throw new ArgumentOutOfRangeException(nameof(lastPhase), lastPhase, "Import phase must be a defined value.");
        }

        LastPhase = lastPhase;
        TargetAlive = targetAlive;
        Observation = observation;
        Cancelled = cancelled;
    }

    public ApplicationImportPhase LastPhase { get; }

    public bool TargetAlive { get; }

    public ImportObservation? Observation { get; }

    public bool Cancelled { get; }
}
