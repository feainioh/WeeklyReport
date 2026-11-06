-- [新增-离线存储] Phase 1 切片 A：MySQL 001_CreateWorkEntries 的 SQLite 等价迁移（ADR-02 类型映射）
CREATE TABLE IF NOT EXISTS work_entries (
    id TEXT NOT NULL,
    work_date TEXT NOT NULL,
    content TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_work_entries_work_date ON work_entries (work_date);
