CREATE TABLE ReceivedNotification(
    Id INTEGER PRIMARY KEY NOT NULL,
    ReceivedAt TEXT NOT NULL,
    ReceivedOnRun INTEGER NOT NULL REFERENCES ProgramRun(Id) ON DELETE RESTRICT,
    Title TEXT NOT NULL,
    Body TEXT NOT NULL,
    Substring TEXT NOT NULL CHECK (json_valid(Substring)),
    Formatted TEXT NOT NULL
) STRICT;
