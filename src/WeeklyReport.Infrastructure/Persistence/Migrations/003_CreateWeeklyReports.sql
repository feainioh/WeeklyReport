CREATE TABLE IF NOT EXISTS weekly_reports (
    id CHAR(36) NOT NULL,
    week_start DATE NOT NULL,
    week_end DATE NOT NULL,
    work_summary MEDIUMTEXT NOT NULL,
    next_week_plan MEDIUMTEXT NOT NULL,
    risks MEDIUMTEXT NOT NULL,
    confirmed_minutes INT NOT NULL,
    estimated_minutes INT NOT NULL,
    unknown_hours_count INT NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_weekly_reports_week_start (week_start)
) ENGINE = InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
