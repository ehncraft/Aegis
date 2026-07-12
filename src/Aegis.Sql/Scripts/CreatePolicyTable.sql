-- Provisions the table SqlPolicyProvider reads from, using the default
-- column names from SqlPolicyStoreOptions (Table = "AegisPolicies",
-- ResourceNameColumn = "ResourceName", PolicyYamlColumn = "PolicyYaml").
-- Rename freely -- every name here is just the default, not a requirement;
-- point SqlPolicyStoreOptions at whatever this ends up being called.
--
-- Version/UpdatedAt aren't read by SqlPolicyProvider today (it's read-only;
-- writing/versioning policies is future work), but are here now so adding
-- that later doesn't require a migration on top of a migration.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AegisPolicies')
BEGIN
    CREATE TABLE AegisPolicies
    (
        ResourceName NVARCHAR(200)   NOT NULL PRIMARY KEY,
        PolicyYaml   NVARCHAR(MAX)   NOT NULL,
        Version      INT             NOT NULL DEFAULT 1,
        UpdatedAt    DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
