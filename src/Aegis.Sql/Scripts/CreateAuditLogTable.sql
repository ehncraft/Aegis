-- Provisions the table SqlAuditLogStore reads/writes, using the default
-- column names from SqlAuditLogStoreOptions (Table = "AegisAuditLog",
-- TenantIdColumn = "TenantId"). Rename the table freely; every other
-- column name is fixed -- this schema is Aegis's own, not an existing
-- system's (unlike the attribute provider tables).
--
-- TenantId defaults to '' for single-tenant deployments -- leave
-- SqlAuditLogStoreOptions.TenantId unset and every row is written/read
-- with TenantId=''. Not part of a unique key: audit rows are append-only,
-- there's nothing to deduplicate on.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AegisAuditLog')
BEGIN
    CREATE TABLE AegisAuditLog
    (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        TenantId        NVARCHAR(100)   NOT NULL DEFAULT '',
        PrincipalId     NVARCHAR(200)   NOT NULL,
        ResourceKind    NVARCHAR(200)   NOT NULL,
        ResourceId      NVARCHAR(200)   NULL,
        Action          NVARCHAR(200)   NOT NULL,
        Allowed         BIT             NOT NULL,
        ExplanationJson NVARCHAR(MAX)   NOT NULL,
        Timestamp       DATETIME2       NOT NULL
    );

    CREATE INDEX IX_AegisAuditLog_Query
        ON AegisAuditLog (TenantId, PrincipalId, ResourceKind, Timestamp DESC);
END
