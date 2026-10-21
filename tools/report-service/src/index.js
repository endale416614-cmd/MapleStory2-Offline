// Problem-report service of MapleStory 2 Offline (docs/engineering/DIAGNOSTIC_REPORTS.md section 5).
// Launchers POST one gzip JSON report; reading needs the ADMIN_TOKEN secret. The sender's IP address is never
// stored: only a keyed hash of it (of its /64 network for IPv6) for the daily limit, cleared by the daily cron.
import { gunzipSync, gzipSync } from "node:zlib";

const MAX_BODY = 512 * 1024; // as sent (gzip)
const MAX_JSON = 4 * 1024 * 1024; // decompressed; node:zlib stops a gzip bomb here (DecompressionStream would not)
const PER_INSTALL = 20; // reports a day
const PER_SENDER = 100;
const DAILY_BYTES = 5 * 1024 * 1024; // stored a day by everyone together: 90 days stay under D1's 500 MB per database
const KEEP_DAYS = 90;
const PAGE = 1000;
const REPORT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const INSTALL_ID = /^[0-9a-z-]{4,64}$/i;
const FIELDS = ["schema", "reportId", "installId", "createdUtc", "kind", "platform", "launcher", "note", "report"];
const ONE = "/v1/reports/";

export default {
  async fetch(request, env) {
    const { pathname, searchParams } = new URL(request.url);
    if (request.method === "POST" && pathname === "/v1/report") {
      return receive(request, env);
    }
    if (request.method === "GET" && (pathname === "/v1/reports" || pathname.startsWith(ONE))) {
      if (!(await authorized(request, env))) {
        return text(401, "unauthorized");
      }
      return pathname === "/v1/reports"
        ? list(searchParams.get("since") ?? "", searchParams.get("after") ?? "", searchParams.get("afterId") ?? "", env)
        : one(pathname.slice(ONE.length), env);
    }
    return text(404, "not found");
  },

  // Daily cron (wrangler.toml): reports are kept 90 days; a sender hash only for the day it limits.
  async scheduled(event, env) {
    await env.DB.prepare("DELETE FROM reports WHERE day < ?1").bind(day(event.scheduledTime - KEEP_DAYS * 86_400_000)).run();
    await env.DB.prepare("UPDATE reports SET sender = '' WHERE day < ?1 AND sender <> ''").bind(day(event.scheduledTime)).run();
  },
};

async function receive(request, env) {
  if (!env.ADMIN_TOKEN) {
    return text(503, "not configured"); // launchers keep the report and try again later
  }
  // A web page cannot send application/json to another site without a preflight, which this service never answers.
  if (!(request.headers.get("content-type") ?? "").toLowerCase().startsWith("application/json")) {
    return text(415, "application/json only");
  }
  const length = request.headers.get("content-length");
  if (length === null) {
    return text(411, "length required"); // never buffer a body of unknown size
  }
  if (!(Number(length) <= MAX_BODY)) {
    return text(413, "too large");
  }
  const body = new Uint8Array(await request.arrayBuffer());
  if (body.length > MAX_BODY) {
    return text(413, "too large");
  }
  const gzipped = body[0] === 0x1f && body[1] === 0x8b;
  let report;
  try {
    report = JSON.parse(new TextDecoder().decode(gzipped ? gunzipSync(body, { maxOutputLength: MAX_JSON }) : body));
  } catch (error) {
    return error instanceof RangeError ? text(413, "too large") : text(400, "not a report");
  }
  const problem = check(report);
  if (problem) {
    return text(400, problem);
  }

  const today = day(Date.now());
  const id = report.reportId.toLowerCase();
  const sender = await keyedHash(env.ADMIN_TOKEN, `${today}|${network(request.headers.get("cf-connecting-ip") ?? "")}`);
  const counts = await env.DB.prepare(
    "SELECT (SELECT COUNT(*) FROM reports WHERE id = ?4) AS stored, " +
    "(SELECT COUNT(*) FROM reports WHERE install_id = ?1 AND day = ?3) AS install, " +
    "(SELECT COUNT(*) FROM reports WHERE sender = ?2 AND day = ?3) AS sender, " +
    "(SELECT COALESCE(SUM(LENGTH(body)), 0) FROM reports WHERE day = ?3) AS bytes")
    .bind(report.installId, sender, today, id).first();
  if (counts.stored > 0) {
    return text(201, "stored"); // a resend after a lost answer
  }
  if (counts.install >= PER_INSTALL || counts.sender >= PER_SENDER) {
    return text(429, "too many reports today");
  }
  const stored = Buffer.from(gzipped ? body : gzipSync(body)).toString("base64");
  if (counts.bytes + stored.length > DAILY_BYTES) {
    return text(503, "full for today"); // launchers keep the report and try again later
  }
  await env.DB.prepare(
    "INSERT INTO reports (id, day, received, install_id, sender, kind, platform, launcher, body) " +
    "VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9) ON CONFLICT(id) DO NOTHING")
    .bind(id, today, new Date().toISOString(), report.installId, sender, report.kind, report.platform, report.launcher, stored)
    .run();
  return text(201, "stored");
}

// The launcher payload, schema 1 (design doc section 1); null when it is valid.
function check(r) {
  if (typeof r !== "object" || r === null || Array.isArray(r)) return "not a report";
  if (Object.keys(r).some(key => !FIELDS.includes(key))) return "unknown field";
  if (r.schema !== 1) return "unknown schema";
  if (typeof r.reportId !== "string" || !REPORT_ID.test(r.reportId)) return "bad reportId";
  if (typeof r.installId !== "string" || !INSTALL_ID.test(r.installId)) return "bad installId";
  if (typeof r.createdUtc !== "string" || r.createdUtc.length > 40) return "bad createdUtc";
  if (!["problem", "crash", "manual"].includes(r.kind)) return "bad kind";
  if (!["windows", "linux"].includes(r.platform)) return "bad platform";
  if (typeof r.launcher !== "string" || r.launcher.length > 64) return "bad launcher";
  if (r.note != null && (typeof r.note !== "string" || r.note.length > 2_000)) return "bad note";
  if (typeof r.report !== "string" || r.report.length > 400_100) return "bad report";
  return null;
}

// Pages of 1,000 in arrival order; the next page starts after the last row's received time and id.
async function list(since, after, afterId, env) {
  const { results } = await env.DB.prepare(
    "SELECT id, day, received, install_id AS installId, kind, platform, launcher FROM reports " +
    "WHERE day >= ?1 AND (received > ?2 OR (received = ?2 AND id > ?3)) ORDER BY received, id LIMIT ?4")
    .bind(since, after, afterId, PAGE)
    .all();
  return Response.json(results);
}

async function one(id, env) {
  const row = await env.DB.prepare("SELECT body FROM reports WHERE id = ?1").bind(id.toLowerCase()).first();
  return row ? new Response(Buffer.from(row.body, "base64"), { headers: { "content-type": "application/gzip" } }) : text(404, "not found");
}

async function authorized(request, env) {
  if (!env.ADMIN_TOKEN) {
    return false;
  }
  // Compared as digests so the time taken does not depend on how much of the token was right.
  const [given, expected] = await Promise.all([
    sha256(request.headers.get("authorization") ?? ""),
    sha256(`Bearer ${env.ADMIN_TOKEN}`),
  ]);
  let difference = 0;
  for (let i = 0; i < given.length; i++) {
    difference |= given[i] ^ expected[i];
  }
  return difference === 0;
}

// The /64 network of an IPv6 address (one subscriber can use all of it); an IPv4 address as it is.
function network(ip) {
  if (!ip.includes(":")) {
    return ip;
  }
  const [head, tail] = ip.split("::");
  const left = head ? head.split(":") : [];
  const right = tail ? tail.split(":") : [];
  const groups = tail === undefined ? left : [...left, ...Array(Math.max(0, 8 - left.length - right.length)).fill("0"), ...right];
  return groups.slice(0, 4).map(group => group.toLowerCase().replace(/^0+(?=.)/, "")).join(":") + "::/64";
}

async function sha256(value) {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
}

async function keyedHash(secret, value) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const mac = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(value)));
  return [...mac.subarray(0, 16)].map(b => b.toString(16).padStart(2, "0")).join("");
}

function day(milliseconds) {
  return new Date(milliseconds).toISOString().slice(0, 10);
}

function text(status, message) {
  return new Response(`${message}\n`, { status, headers: { "content-type": "text/plain; charset=utf-8" } });
}
