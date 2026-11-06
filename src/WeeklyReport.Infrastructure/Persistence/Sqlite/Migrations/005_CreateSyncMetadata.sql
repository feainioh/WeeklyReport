-- [新增-离线存储] Phase 1 切片 D：同步元数据表（ADR-03）。
-- local_meta：镜像身份/版本/存储状态（单行）；sync_operation：语义化操作组队列；
-- sync_conflict：冲突双方版本与处理状态（Phase 2 使用，表结构先落）。
CREATE TABLE IF NOT EXISTS local_meta (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    schema_version INTEGER NOT NULL,
    protocol_version INTEGER NOT NULL,
    mirror_identity TEXT NULL,
    last_mirror_at_utc TEXT NULL,
    storage_status TEXT NOT NULL DEFAULT 'mysql-only',
    last_sync_at_utc TEXT NULL
);
CREATE TABLE IF NOT EXISTS sync_operation (
    op_group_id TEXT NOT NULL,
    operation_kind TEXT NOT NULL,
    changes_json TEXT NOT NULL,
    baseline_ref TEXT NULL,
    depends_on TEXT NULL,
    status TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    attempted_at_utc TEXT NULL,
    PRIMARY KEY (op_group_id)
);
CREATE INDEX IF NOT EXISTS ix_sync_operation_status ON sync_operation (status);
CREATE TABLE IF NOT EXISTS sync_conflict (
    op_group_id TEXT NOT NULL,
    object_key TEXT NOT NULL,
    local_version TEXT NOT NULL,
    remote_version TEXT NOT NULL,
    baseline_ref TEXT NULL,
    reason TEXT NOT NULL,
    resolution_status TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    PRIMARY KEY (op_group_id, object_key)
);
