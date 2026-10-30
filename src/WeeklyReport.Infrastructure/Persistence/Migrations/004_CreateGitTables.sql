CREATE TABLE IF NOT EXISTS git_projects (
    id CHAR(36) NOT NULL,
    name VARCHAR(200) NOT NULL,
    repository_path MEDIUMTEXT NULL,
    author_field VARCHAR(16) NULL,
    author_value MEDIUMTEXT NULL,
    PRIMARY KEY (id)
) ENGINE = InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS git_work_items (
    id CHAR(36) NOT NULL,
    project_id CHAR(36) NOT NULL,
    work_date DATE NOT NULL,
    description MEDIUMTEXT NOT NULL,
    estimated_minutes INT NULL,
    confirmed_minutes INT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    revision BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    UNIQUE KEY ux_git_work_items_project_date (project_id, work_date),
    UNIQUE KEY ux_git_work_items_id_project (id, project_id),
    INDEX ix_git_work_items_date (work_date),
    CONSTRAINT fk_git_work_items_project FOREIGN KEY (project_id) REFERENCES git_projects (id)
) ENGINE = InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS git_commit_links (
    project_id CHAR(36) NOT NULL,
    commit_hash VARCHAR(64) NOT NULL,
    work_item_id CHAR(36) NOT NULL,
    author_name MEDIUMTEXT NOT NULL,
    author_email MEDIUMTEXT NOT NULL,
    authored_at VARCHAR(40) NOT NULL,
    subject MEDIUMTEXT NOT NULL,
    PRIMARY KEY (project_id, commit_hash),
    INDEX ix_git_commit_links_work_item (work_item_id),
    CONSTRAINT fk_git_commit_links_project FOREIGN KEY (project_id) REFERENCES git_projects (id),
    CONSTRAINT fk_git_commit_links_work_item FOREIGN KEY (work_item_id, project_id) REFERENCES git_work_items (id, project_id)
) ENGINE = InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
