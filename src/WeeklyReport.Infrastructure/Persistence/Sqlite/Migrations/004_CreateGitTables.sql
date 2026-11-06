-- [新增-离线存储] Phase 1 切片 A：MySQL 004_CreateGitTables 的 SQLite 等价迁移（含复合唯一与外键，ADR-03）
CREATE TABLE IF NOT EXISTS git_projects (
    id TEXT NOT NULL,
    name TEXT NOT NULL,
    repository_path TEXT NULL,
    author_field TEXT NULL,
    author_value TEXT NULL,
    PRIMARY KEY (id)
);
CREATE TABLE IF NOT EXISTS git_work_items (
    id TEXT NOT NULL,
    project_id TEXT NOT NULL,
    work_date TEXT NOT NULL,
    description TEXT NOT NULL,
    estimated_minutes INTEGER NULL,
    confirmed_minutes INTEGER NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    revision INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    UNIQUE (project_id, work_date),
    UNIQUE (id, project_id),
    FOREIGN KEY (project_id) REFERENCES git_projects (id)
);
CREATE INDEX IF NOT EXISTS ix_git_work_items_date ON git_work_items (work_date);
CREATE TABLE IF NOT EXISTS git_commit_links (
    project_id TEXT NOT NULL,
    commit_hash TEXT NOT NULL,
    work_item_id TEXT NOT NULL,
    author_name TEXT NOT NULL,
    author_email TEXT NOT NULL,
    authored_at TEXT NOT NULL,
    subject TEXT NOT NULL,
    PRIMARY KEY (project_id, commit_hash),
    FOREIGN KEY (project_id) REFERENCES git_projects (id),
    FOREIGN KEY (work_item_id, project_id) REFERENCES git_work_items (id, project_id)
);
CREATE INDEX IF NOT EXISTS ix_git_commit_links_work_item ON git_commit_links (work_item_id);
