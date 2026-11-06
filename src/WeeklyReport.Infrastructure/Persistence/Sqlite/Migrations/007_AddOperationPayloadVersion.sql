-- Versioned operation payloads prevent legacy queue rows from being mistaken for immutable snapshots.
ALTER TABLE sync_operation
    ADD COLUMN payload_version INTEGER NOT NULL DEFAULT 1;
