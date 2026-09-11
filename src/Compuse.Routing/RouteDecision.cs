using Compuse.Contracts;

namespace Compuse.Routing;

public sealed class RouteDecision
{
    private RouteDecision(ExecutionPlan? plan, ApplicationImportPlan? applicationPlan, RefusalInfo? refusal)
    {
        Plan = plan;
        ApplicationPlan = applicationPlan;
        Refusal = refusal;
    }

    public ExecutionPlan? Plan { get; }

    public ApplicationImportPlan? ApplicationPlan { get; }

    public RefusalInfo? Refusal { get; }

    public bool IsPlan => Plan is not null || ApplicationPlan is not null;

    public static RouteDecision ForPlan(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new RouteDecision(plan, applicationPlan: null, refusal: null);
    }

    public static RouteDecision ForApplicationPlan(ApplicationImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new RouteDecision(plan: null, applicationPlan: plan, refusal: null);
    }

    public static RouteDecision ForRefusal(RefusalInfo refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new RouteDecision(plan: null, applicationPlan: null, refusal);
    }
}
