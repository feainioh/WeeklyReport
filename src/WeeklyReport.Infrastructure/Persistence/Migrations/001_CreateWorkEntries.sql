CREATE TABLE IF NOT EXISTS work_entries (
    id CHAR(36) NOT NULL,
    work_date DATE NOT NULL,
    content MEDIUMTEXT NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    INDEX ix_work_entries_work_date (work_date)
) ENGINE = InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
