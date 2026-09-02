namespace Neura.Modules.ContextManagement.Domain;

public sealed class ContextThresholdOptions
{
    public double WarningThreshold { get; set; } = 0.70;
    public double CriticalThreshold { get; set; } = 0.85;
    public double ExhaustionThreshold { get; set; } = 0.95;
}

public enum ContextLevel { Normal, Warning, Critical, Exhausted }

public static class ContextEvaluator
{
    public static ContextLevel Evaluate(double usageRatio, ContextThresholdOptions options)
    {
        if (usageRatio >= options.ExhaustionThreshold) return ContextLevel.Exhausted;
        if (usageRatio >= options.CriticalThreshold) return ContextLevel.Critical;
        if (usageRatio >= options.WarningThreshold) return ContextLevel.Warning;
        return ContextLevel.Normal;
    }
}
