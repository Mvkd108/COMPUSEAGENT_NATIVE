namespace Compuse.ApplicationSurface;

public sealed class FlaUiSessionFactory : IUiaSessionFactory
{
    public static FlaUiSessionFactory Instance { get; } = new();

    public IUiaSession Create() => new FlaUiSession();
}
