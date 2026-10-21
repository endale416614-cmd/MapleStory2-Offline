-- Problem reports (docs/engineering/DIAGNOSTIC_REPORTS.md section 5). body: the gzip JSON as sent, base64.
-- sender: a keyed hash of the sender's IP address that changes every day (the address itself is not stored).
CREATE TABLE IF NOT EXISTS reports (
    id TEXT PRIMARY KEY,
    day TEXT NOT NULL,
    received TEXT NOT NULL,
    install_id TEXT NOT NULL,
    sender TEXT NOT NULL,
    kind TEXT NOT NULL,
    platform TEXT NOT NULL,
    launcher TEXT NOT NULL,
    body TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS reports_by_day ON reports (day, received);
CREATE INDEX IF NOT EXISTS reports_by_install ON reports (install_id, day);
CREATE INDEX IF NOT EXISTS reports_by_sender ON reports (sender, day);
