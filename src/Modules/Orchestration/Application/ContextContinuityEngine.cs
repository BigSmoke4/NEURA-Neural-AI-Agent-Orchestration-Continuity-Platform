using Neura.Modules.ContextManagement.Domain;

namespace Neura.Modules.Orchestration.Application;

/// <summary>
/// Extracts the minimum sufficient structured context for a handoff instead
/// of copying the full conversation. In this reference implementation the
/// "extraction" summarizes tracked task state; production versions would
/// additionally run an AI summarization pass over agent scratch memory.
/// </summary>
public sealed class ContextContinuityEngine
{
    public ContextHandoffPackage BuildPackage(
        Guid missionId, Guid taskId, Guid fromAgentId,
        string missionTitle, string currentTaskTitle, string status,
        List<string> completedWork, List<string> remainingWork,
        List<string> decisions, List<string> constraints,
        List<string> filesChanged, List<string> errors,
        List<string> tests, List<string> dependencies,
        List<string> openQuestions, List<string> relevantMemorySnippets)
    {
        return ContextHandoffPackage.Build(
            missionId, taskId, fromAgentId,
            missionTitle, currentTaskTitle, status,
            completedWork, remainingWork, decisions, constraints,
            filesChanged, errors, tests, dependencies, openQuestions, relevantMemorySnippets);
    }
}
