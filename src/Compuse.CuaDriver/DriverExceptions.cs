namespace Compuse.CuaDriver;

public class DriverStartException : Exception
{
    public DriverStartException(string message)
        : base(message)
    {
    }

    public DriverStartException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public class DriverProtocolException : Exception
{
    public DriverProtocolException(string message)
        : base(message)
    {
    }

    public DriverProtocolException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public class DriverDisconnectedException : Exception
{
    public DriverDisconnectedException(string message)
        : base(message)
    {
    }

    public DriverDisconnectedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public class DriverIncompatibleException : Exception
{
    public DriverIncompatibleException(string message)
        : base(message)
    {
    }
}

public class DriverUnavailableException : Exception
{
    public DriverUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
