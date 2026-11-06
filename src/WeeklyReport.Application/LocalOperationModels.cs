// [新增-离线存储] Phase 1 切片 D：本地操作组模型（ADR-03）。
// 操作组内容为语义化变更集合（表、行主键、自然键、操作类型），不是原始 SQL；
// 内容哈希 = 对变更集合规范化 JSON 的 SHA-256（record 属性顺序固定，序列化确定）。
namespace WeeklyReport.Application;

/// <summary>操作组内单个对象的变更描述。</summary>
/// <param name="Table">业务表名（如 work_entries、weekly_reports）。</param>
/// <param name="RowId">业务主键（GUID 字符串）。</param>
/// <param name="NaturalKey">自然键描述（如周起始日期、项目+日期、提交哈希），用于 MySQL 侧键匹配。</param>
/// <param name="Change">变更类型：新增 / 更新 / 删除意图。</param>
public sealed record LocalOperationChange(string Table, string RowId, string NaturalKey, LocalObjectChange Change)
{
    /// <summary>序列化的本地变更前快照，用于冲突比较；新增操作为空。</summary>
    public string? BeforeJson { get; init; }

    /// <summary>序列化的本地变更后快照；删除操作为空。</summary>
    public string? AfterJson { get; init; }
}

public enum LocalObjectChange
{
    Add,
    Update,
    Delete
}

/// <summary>同步队列中操作组的状态（计划 §4.1/§4.5 状态机）。</summary>
public enum LocalSyncOperationStatus
{
    /// <summary>待同步：本地已保存，等待提交到 MySQL。</summary>
    Pending,

    /// <summary>同步中：正在提交某个操作组。</summary>
    Syncing,

    /// <summary>结果待确认：请求结果不确定，等待同 ID 查询/重试。</summary>
    AwaitingConfirmation,

    /// <summary>冲突待处理：双方版本保留，等待用户。</summary>
    Conflict,

    /// <summary>已同步：MySQL 已确认。</summary>
    Synced,

    /// <summary>失败：多次尝试未成功，需人工介入。</summary>
    Failed
}

/// <summary>本地存储状态（设置页/状态展示用，随镜像切片启用）。</summary>
public enum LocalStorageStatus
{
    MysqlOnly,
    NotInitialized,
    Ready,
    Suspended
}

// [新增-离线存储] Phase 1 切片 E：本地存储状态快照（计划 §4.5 状态模型的最小可展示集合）。
/// <param name="Status">存储状态（未初始化/就绪/暂停/仅 MySQL）。</param>
/// <param name="HasVerifiedBaseline">是否已有已验证基线（首次镜像成功为 true；空工作区为 false）。</param>
/// <param name="PendingCount">待同步操作组数量。</param>
/// <param name="AwaitingConfirmationCount">结果待确认操作组数量。</param>
/// <param name="ConflictCount">冲突待处理数量。</param>
/// <param name="LastMirrorAtUtc">最近完整镜像时间（可空）。</param>
/// <param name="LastSyncAtUtc">最近成功同步时间（可空）。</param>
/// <param name="FailedCount">失败且需要人工介入的操作组数量。</param>
/// <param name="StatusReason">存储暂停原因（可空）。</param>
public sealed record LocalStorageState(
    LocalStorageStatus Status,
    bool HasVerifiedBaseline,
    int PendingCount,
    int AwaitingConfirmationCount,
    int ConflictCount,
    DateTimeOffset? LastMirrorAtUtc,
    DateTimeOffset? LastSyncAtUtc,
    int FailedCount = 0,
    string? StatusReason = null);

public interface ILocalStorageStateProvider
{
    Task<LocalStorageState> GetStateAsync(CancellationToken cancellationToken = default);
}
