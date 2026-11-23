-- Idempotency records of the internal API (mirrors ERP's idempotency_records table).
-- Apply once per service database (idempotent). Records are purged after 7 days (PostgresStore.Purge).
CREATE TABLE IF NOT EXISTS idempotency_records (
    caller        varchar(100) NOT NULL,
    key           varchar(200) NOT NULL,
    tenant_id     uuid         NOT NULL,
    request_hash  varchar(64)  NOT NULL,
    status        integer      NOT NULL,           -- 0 = pending
    response_json text         NOT NULL DEFAULT '',
    created_at    timestamptz  NOT NULL,
    owner_token   uuid         NOT NULL,
    PRIMARY KEY (caller, key)
);

CREATE INDEX IF NOT EXISTS ix_idempotency_records_created_at ON idempotency_records (created_at);
