-- [新增-离线存储] Phase 2：为已安装同步协议的 MySQL 数据库生成稳定身份。
INSERT INTO sync_identity (id, database_id, protocol_version, created_at_utc)
SELECT 1, UUID(), 1, UTC_TIMESTAMP(6)
WHERE NOT EXISTS (SELECT 1 FROM sync_identity WHERE id = 1);
