-- [新增-离线存储] Phase 2：保存同步暂停原因，以便应用重启后仍能解释暂停状态。
ALTER TABLE local_meta ADD COLUMN status_reason TEXT NULL;
