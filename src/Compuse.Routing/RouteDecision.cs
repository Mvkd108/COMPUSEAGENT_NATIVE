using Compuse.Contracts;

namespace Compuse.Routing;

public sealed class RouteDecision
{
    private RouteDecision(ExecutionPlan? plan, RefusalInfo? refusal)
    {
        Plan = plan;
        Refusal = refusal;
    }

    public ExecutionPlan? Plan { get; }

    public RefusalInfo? Refusal { get; }

    public bool IsPlan => Plan is not null;

    public static RouteDecision ForPlan(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new RouteDecision(plan, refusal: null);
    }

    public static RouteDecision ForRefusal(RefusalInfo refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new RouteDecision(plan: null, refusal);
    }
}
