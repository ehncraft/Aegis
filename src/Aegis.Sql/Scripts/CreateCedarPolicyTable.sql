-- Provisions the table CedarSqlPolicyProvider reads from, using the default
-- column names from CedarSqlPolicyStoreOptions (Table = "AegisCedarPolicies",
-- PolicyNameColumn = "PolicyName", PolicyCedarColumn = "PolicyCedar",
-- TenantIdColumn = "TenantId"). Rename freely -- every name here is just
-- the default, not a requirement; point CedarSqlPolicyStoreOptions at
-- whatever this ends up being called.
--
-- TenantId defaults to '' for single-tenant deployments -- leave
-- CedarSqlPolicyStoreOptions.TenantId unset and every row loads regardless
-- of this column's value. PolicyName is deliberately not part of the
-- primary key alone -- the same PolicyName may exist per tenant (e.g.
-- "manage-devices" defined independently by two tenants), hence the
-- composite key.
--
-- Version/UpdatedAt aren't read by CedarSqlPolicyProvider today (it's
-- read-only; writing/versioning policies is future work), but are here now
-- so adding that later doesn't require a migration on top of a migration.
-- Same shape as CreatePolicyTable.sql's AegisPolicies, minus ResourceName
-- (a Cedar row isn't scoped to one resource the way a YAML row is -- see
-- CedarSqlPolicyProvider's own doc comment).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AegisCedarPolicies')
BEGIN
    CREATE TABLE AegisCedarPolicies
    (
        TenantId    NVARCHAR(100)   NOT NULL DEFAULT '',
        PolicyName  NVARCHAR(200)   NOT NULL,
        PolicyCedar NVARCHAR(MAX)   NOT NULL,
        Version     INT             NOT NULL DEFAULT 1,
        UpdatedAt   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AegisCedarPolicies PRIMARY KEY (TenantId, PolicyName)
    );
END
