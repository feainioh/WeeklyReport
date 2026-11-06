-- [新增-离线存储] Phase 1 切片 A：MySQL 002_AddWorkEntryHours 的 SQLite 等价迁移
ALTER TABLE work_entries ADD COLUMN confirmed_minutes INTEGER NULL;
