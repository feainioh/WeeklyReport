-- [新增-离线存储] Phase 1 切片 A：MySQL 003_CreateWeeklyReports 的 SQLite 等价迁移
CREATE TABLE IF NOT EXISTS weekly_reports (
    id TEXT NOT NULL,
    week_start TEXT NOT NULL,
    week_end TEXT NOT NULL,
    work_summary TEXT NOT NULL,
    next_week_plan TEXT NOT NULL,
    risks TEXT NOT NULL,
    confirmed_minutes INTEGER NOT NULL,
    estimated_minutes INTEGER NOT NULL,
    unknown_hours_count INTEGER NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (id),
    UNIQUE (week_start)
);
