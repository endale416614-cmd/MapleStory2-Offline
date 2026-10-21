# Problem-report service

The launchers send problem reports (only when the player agrees) to this small web service. It runs on Cloudflare,
on the maintainer's own account. Design: [docs/engineering/DIAGNOSTIC_REPORTS.md](../../docs/engineering/DIAGNOSTIC_REPORTS.md) section 5.

- **Worker**: the program in `src/index.js`. Cloudflare runs it at `https://ms2-reports.<your-subdomain>.workers.dev`.
- **D1 database** `ms2-reports`: holds the reports (gzip JSON, at most 512 KB each). A daily cleanup deletes reports
  older than 90 days.
- Anyone can send a report (the launchers carry no password); only someone with the ADMIN_TOKEN can read them.
- Limits: 20 reports a day per install, 100 per sender (an IPv6 /64 network counts as one sender), 5 MB a day for
  everyone together (about 1,000 normal reports). Above a limit the launchers keep their reports and try again later.
- The sender's IP address is not stored. A keyed hash of it counts the daily limit and is cleared by the next daily
  cleanup. D1 keeps earlier states of the database for 7 days (Time Travel), so a cleared value is gone after a week.
- Cost: the Workers Free plan (100,000 requests a day; one D1 database holds up to 500 MB). D1 is part of the free
  plan; R2 storage would need a paid-plan checkout, so it is not used.

## Deploy (once, about 10 minutes)
Needs Node.js 22 or newer (on the development PC). Open PowerShell in this folder (`tools\report-service`) and run,
one at a time. The first `npx wrangler` asks "Ok to proceed?" before it downloads Wrangler: answer `y`.

1. Sign in: `npx wrangler login`. A browser window opens: sign in to Cloudflare (with GitHub, as you made the account)
   and press Allow.
2. Create the database: `npx wrangler d1 create ms2-reports`. It prints a short configuration snippet. Copy only the
   `database_id` value into `wrangler.toml`, in place of `PASTE-THE-ID-FROM-wrangler-d1-create` (keep `binding = "DB"`).
3. Create the table: `npx wrangler d1 execute ms2-reports --remote --file schema.sql` (answer `y` if it asks).
4. Publish: `npx wrangler deploy`. The first time it asks you to choose a workers.dev subdomain (any name). At the end
   it prints the service address. A new subdomain can take a few minutes before it answers.
5. Make the read password: `python -c "import secrets; print(secrets.token_urlsafe(32))"`. Keep it in your password
   manager. Anyone with it can read every report; never post it or put it in a file of the repository.
6. Give it to the service: `npx wrangler secret put ADMIN_TOKEN` and paste it when asked. Until this step the service
   answers "not configured" and the launchers keep their reports.
7. Check:
   `curl.exe -s -X POST -H "content-type: application/json" --data "{}" https://ms2-reports.<your-subdomain>.workers.dev/v1/report`
   prints `unknown schema` (ready). `not configured` means step 6 is missing.
8. Put the address (not the password) into the launchers (`ProblemReport.ServiceUrl` and
   the Linux `REPORT_SERVICE_URL`).

## Read reports
Store the password once for your Windows account. This asks for it, so it stays out of the PowerShell history:

```powershell
[Environment]::SetEnvironmentVariable("MS2_REPORTS_TOKEN", (Read-Host "Report service password"), "User")
```

Then, in a new PowerShell window, from the repository folder:

```powershell
python tools\report-service\fetch_reports.py --url https://ms2-reports.<your-subdomain>.workers.dev --out reports
```

New reports land in `<out>\<day>\<id>.json`; `<out>\digest.md` lists each problem once, most frequent first. Player
text in the digest is quoted as untrusted data.

## Change the password
Repeat steps 5 and 6, then store the new one in MS2_REPORTS_TOKEN. The launchers are not affected.

## Stop or delete everything
- `npx wrangler delete` removes the Worker; launchers then keep their reports queued (30 days, at most 20).
- `npx wrangler d1 delete ms2-reports` deletes the database with every report in it.

## Test
`node --test test/worker.test.mjs` (Node 22.13 or newer) runs the Worker against the schema in a local SQLite database;
`python fetch_reports.py --self-test` checks the digest tool. Neither needs a Cloudflare account.
