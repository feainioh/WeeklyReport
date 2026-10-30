using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed class GitWorkItemProposal(
    Guid workItemId,
    Guid projectId,
    DateOnly workDate,
    string? currentDescription,
    string suggestedDescription,
    int? confirmedMinutes,
    IReadOnlyList<GitCommit> addedCommits)
{
    public Guid WorkItemId { get; } = workItemId;
    public Guid ProjectId { get; } = projectId;
    public DateOnly WorkDate { get; } = workDate;
    public string? CurrentDescription { get; } = currentDescription;
    public string SuggestedDescription { get; } = suggestedDescription;
    public int? ConfirmedMinutes { get; } = confirmedMinutes;
    public IReadOnlyList<GitCommit> AddedCommits { get; } = addedCommits;
}

public sealed class DeletedGitWorkItemConflict(
    Guid workItemId,
    Guid projectId,
    DateOnly workDate,
    IReadOnlyList<GitCommit> newCommits)
{
    public Guid WorkItemId { get; } = workItemId;
    public Guid ProjectId { get; } = projectId;
    public DateOnly WorkDate { get; } = workDate;
    public IReadOnlyList<GitCommit> NewCommits { get; } = newCommits;
}

public sealed class GitSyncPreview(
    IReadOnlyList<GitWorkItemProposal> proposals,
    IReadOnlyList<DeletedGitWorkItemConflict> deletedConflicts,
    IReadOnlyList<DailyGitHourEstimate> suggestedHours,
    int duplicateCount)
{
    public IReadOnlyList<GitWorkItemProposal> Proposals { get; } = proposals;
    public IReadOnlyList<DeletedGitWorkItemConflict> DeletedConflicts { get; } = deletedConflicts;
    public IReadOnlyList<DailyGitHourEstimate> SuggestedHours { get; } = suggestedHours;
    public int DuplicateCount { get; } = duplicateCount;
}
