# Diagnostic reports (1.2.0)

Maintainer 2026-10-21: players can send problem reports to the maintainer automatically, opt-in. The player-facing
text (the promise this design keeps) is the "Problem reports" page; its claims are marked [P] below.

## 1. What is sent
One JSON document per report, gzip-compressed, schema 1:

| Field | Content |
|---|---|
| schema | 1 |
| reportId | a new GUID (also the queue file name) |
| installId | the install id the launcher already has (a random GUID; [P] "a random install number") |
| createdUtc | ISO 8601 |
| kind | `problem` (the session ended with a problem), `crash` (the launcher crashed), `manual` (Report a problem) |
| platform, launcher | `windows` or `linux`; the launcher version |
| note | the player's own words (manual reports only), at most 2,000 characters, names replaced |
| report | the session report text after section 2, at most 400,000 characters (cut at the end with a marker) |

The report text already holds the versions, game mode, content pack, OS and hardware, so they are not separate
fields; the digest tool reads them from the text and groups reports by their known-problem id or first exception.

Never sent [P]: the diagnostics zip, the save database, game files. launcher.ini lines appear as the session report
already prints them (Username and Password hidden).

## 2. What is removed (both launchers, before anything is queued)
The upload text is made from the session report BEFORE its usual redaction, in this order (ProblemReport.ForUpload;
the Linux launcher ports it):
1. Everything the game client wrote goes, while its markers are intact (a Windows user named "Game" would otherwise
   change "-- Game client log"): the client's own log section (MS2Log; ends only at a real section header), every
   server log line with `Client Log:` (client messages), `[SendOp:` / `[LastPacket:` (a socket error with a hex dump of
   the last packet, which can hold names and chat), `[...exception...] ` (the client's raw error text) or `proposed='`
   (a save conflict printing free text such as a motto), each with its continuation lines, and any run of 16 hex bytes
   [P]. The servers themselves log no chat (checked 2026-10-21: UserChatHandler and the World chat service log only ids
   and errors); the packet dumps were found by the privacy review.
2. Redact: the user profile folder becomes `%USERPROFILE%` (Linux: the home folder becomes `~`); the user name, PC name
   and machine ids become `<redacted>`.
3. Names become `<name>` as whole words, any case, 2 or more characters: every character and account name in every save
   profile, the profile folder names, the user name, PC name and user folder name [P]; the default account `offline`
   stays. When a save cannot be read (damaged, locked), nothing is queued: its character names would stay in the server
   log lines.
4. The cut at 400,000 characters. The player's note gets steps 2 and 3 and is cut at 2,000.
- Edges handled (second review): the client section ends only at "-- Offline guard log" or "-- end of report --" (a
  client line can look like a header); long lines are shortened by the report, so a client error matches without its
  closing bracket; a log section's lines before its first timestamped entry count as a continuation (a short dump whose
  "[LastPacket:" line fell outside the tail); hex runs from 8 bytes.
- Accepted limit: a name that is in no save (typed by the player, e.g. a group-chat invite target, or a character
  renamed during the session) stays in the server lines that print it.
- No IP rule: an offline install's logs hold only local addresses, and a 4-part version such as 1.2.0.0 looks like one.
- The hardware summary stays (processor, graphics card and driver, memory, screen) [P]. The full list of what a report
  contains is ReportDialogs.WhatIsSent, shown before every consent.

## 3. Consent
- launcher.ini `SendReports=ask` (default), `always` or `never`, read only from the player's launcher.ini (a shipped
  defaults.ini cannot turn sending on). Nothing is asked or sent while the build has no service address.
- "Choose how to play" has the unticked checkbox "Send problem reports to the developer automatically"; ticking it
  (also by accessibility tools) first shows what a report contains, with Turn on or Cancel (the default). Unticking
  goes back to asking; "never" stays until the player ticks the box.
- `ask`: when a session ends with a problem, after the game has closed, a dialog asks "Send a problem report to the
  developer?" with the same list, "See exactly what would be sent" (the upload text in Notepad), Send, Don't send (the
  default) and "Do this every time (no more questions)" (writes `always` or `never`; `never` also takes back queued
  automatic reports, and so does every start while it is `never`).
- A launcher crash: with `ask` the crash message is followed by a Yes/No question with the same list (no preview); the
  report is queued and goes out at the next start. With `always` it is queued at once.
- Report a problem ("Choose how to play" link, the launcher's `--report-problem`, a Start-menu shortcut made by Setup):
  the player writes a note, can open "What is sent?" and the exact text, and presses Send; that press is the consent
  for that report.

## 4. Queue and upload
- Queue: `<data folder>\report-queue\<reportId>.<kind>.json.gz`, at most 20 (the oldest go first), 30 days.
- Upload: when the launcher starts (in the background, before the update check) and right after a Send.
  `POST <ReportUrl>/v1/report`, `Content-Type: application/json`, `Content-Encoding: gzip`, 10 s timeout.
  2xx: delete. 400 or 413 (rejected): delete. Anything else (429, 5xx, no network): keep it and stop until next time.
- ReportUrl: the constant `ProblemReport.ServiceUrl`, HTTPS only, set at release time (empty until the service is
  deployed: reports then stay queued). The environment variable `MS2OFFLINE_REPORT_URL` overrides it for tests, like
  the update feed's `MS2OFFLINE_UPDATE_FEED`.

## 5. Service (maintainer's Cloudflare account)
A Worker with a D1 database (R2 would need a payment method even on the free tier), code and deploy steps in
`tools/report-service/`:
- `POST /v1/report`: no authentication; `Content-Type: application/json` (else 415: a web page cannot send that to
  another site without a preflight this service never answers) and a Content-Length (else 411); JSON of schema 1 with
  exactly the payload fields (gzip, or plain), at most 512 KB as sent and 4 MB decompressed (node:zlib's output limit;
  workerd's DecompressionStream would inflate a gzip bomb fully); at most 20 reports a day per installId, 100 per
  sender (a keyed hash of the IP address, of its /64 network for IPv6; the address is not stored) and 5 MB a day for
  everyone (90 days stay under D1's 500 MB per database); a resent reportId answers 201 again, even at a limit;
  answers 201, 400, 411, 413, 415, 429, or 503 (full for today, or the ADMIN_TOKEN secret not set). The launchers
  delete a report on 2xx, 400 and 413 and keep it on anything else.
- `GET /v1/reports?since=<yyyy-mm-dd>&after=<received>&afterId=<id>` (pages of 1,000 metadata rows) and
  `GET /v1/reports/<id>` (one gzip file): only with `Authorization: Bearer <ADMIN_TOKEN>` (a Worker secret). No other
  routes; nothing can be read without the token.
- A daily cron deletes reports older than 90 days and clears the sender hashes of past days (D1 Time Travel keeps
  earlier states 7 days).
- Reviewed 2026-10-21 (REQUEST CHANGES, all applied: gzip bomb, IPv6 networks, database size, sender hashes kept,
  paging, the digest tool's robustness and patterns, Content-Type and Content-Length, resend at a limit).
- `tools/report-service/fetch_reports.py` (the token in `MS2_REPORTS_TOKEN`) downloads new reports to a folder outside
  the repository and writes a digest grouped by known-problem id (`Known problem: ... [Tn]`), else first exception
  type, else exit code; it is triaged into BUG_TRACKER.md.

## 6. Tasks
- DR-02: Windows core without WinForms, linked into Launcher.Tests: upload text, payload, queue, sender, tests.
- DR-03: Windows wiring: setting, consent dialog, crash pending, Report a problem, send on start,
  texts (README, SECURITY.md, the code comments that say nothing is sent).
- DR-04: Linux launcher: the same text rules, payload, queue, sender, consent prompt and report command.
- DR-05: the Worker service and fetch_reports.py, with deploy steps for the maintainer.
- DR-06: release defaults (SendReports=ask, ServiceUrl), README and the player page.
