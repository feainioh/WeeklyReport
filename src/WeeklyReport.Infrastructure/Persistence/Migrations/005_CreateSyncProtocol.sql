-- [新增-离线存储] Phase 2 G1：MySQL 同步协议表（ADR-03 §4.1 MySQL 侧）。
-- sync_receipt：幂等回执——操作组 ID + 内容哈希，业务写入与回执同一 InnoDB 事务提交；
-- sync_identity：数据库身份与协议版本（首次同步时初始化，Phase 2 G3 用于身份校验）。
CREATE TABLE IF NOT EXISTS sync_receipt (
    op_group_id CHAR(36) NOT NULL,
    content_hash CHAR(64) NOT NULL,
    applied_at_utc DATETIME(6) NOT NULL,
    result VARCHAR(16) NOT NULL DEFAULT 'applied',
    PRIMARY KEY (op_group_id)
) ENGINE = InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sync_identity (
    id INT NOT NULL DEFAULT 1,
    database_id CHAR(36) NOT NULL,
    protocol_version INT NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT chk_sync_identity_single CHECK (id = 1)
) ENGINE = InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
