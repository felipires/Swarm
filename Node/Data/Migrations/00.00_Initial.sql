CREATE TABLE IF NOT EXISTS "Configuration" (
    "NodeId" TEXT NOT NULL PRIMARY KEY,
    "NodeName" TEXT NOT NULL,
    "Registered" BOOLEAN NOT NULL DEFAULT 0,
    "Online" BOOLEAN NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS "RemoteParameters" (
    "NodeId" TEXT NOT NULL PRIMARY KEY,
    "QueueHost" TEXT,
    "QueuePort" TEXT,
    "QueueUserName" TEXT,
    "QueuePassword" TEXT
);

CREATE TABLE IF NOT EXISTS "LocalTask" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "ClusterTaskId" TEXT NOT NULL,
    "ConfigJson" TEXT NOT NULL DEFAULT '{}',
    "Status" TEXT NOT NULL DEFAULT 'pending',
    "CreatedAt" TEXT NOT NULL DEFAULT (datetime('now')),
    "CompletedAt" TEXT NULL
);

CREATE INDEX IF NOT EXISTS "IX_LocalTask_Status" ON "LocalTask" ("Status");
CREATE INDEX IF NOT EXISTS "IX_LocalTask_ClusterTaskId" ON "LocalTask" ("ClusterTaskId");