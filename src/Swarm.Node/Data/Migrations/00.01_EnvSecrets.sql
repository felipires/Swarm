-- P1-5a Tier 2: local encrypted task-env store. AES-256-GCM ciphertext +
-- per-row nonce. The encryption key is derived from a machine-local secret
-- so values are not portable across Node instances by design.
CREATE TABLE IF NOT EXISTS "EnvSecrets" (
    "Key"        TEXT NOT NULL PRIMARY KEY,
    "Ciphertext" BLOB NOT NULL,
    "Nonce"      BLOB NOT NULL,
    "UpdatedAt"  TEXT NOT NULL DEFAULT (datetime('now'))
);
