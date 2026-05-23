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