// node --test tools/report-service/test/worker.test.mjs (Node 22.13 or newer)
// Runs the Worker against the real schema in node:sqlite through a minimal D1 shim (prepare, bind, first, all, run).
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { gzipSync, gunzipSync } from "node:zlib";
import worker from "../src/index.js";

const TOKEN = crypto.randomUUID(); // a fresh read password per run

function d1() {
  const db = new DatabaseSync(":memory:");
  db.exec(readFileSync(new URL("../schema.sql", import.meta.url), "utf8"));
  // D1 numbers its parameters (?1, ?2, ?1 again); node:sqlite binds plain ? in order.
  const positional = (sql, values) => [sql.replace(/\?\d+/g, "?"), [...sql.matchAll(/\?(\d+)/g)].map(m => values[Number(m[1]) - 1])];
  const statement = (sql, values = []) => {
    const [plain, args] = positional(sql, values);
    return {
      bind: (...next) => statement(sql, next),
      first: async () => db.prepare(plain).get(...args) ?? null,
      all: async () => ({ results: db.prepare(plain).all(...args) }),
      run: async () => db.prepare(plain).run(...args),
    };
  };
  return { prepare: sql => statement(sql), raw: db };
}

const env = () => ({ DB: d1(), ADMIN_TOKEN: TOKEN });
const today = () => new Date().toISOString().slice(0, 10);
const report = (overrides = {}) => ({
  schema: 1, reportId: crypto.randomUUID(), installId: "0123456789abcdef0123456789abcdef",
  createdUtc: "2026-10-21T10:00:00.0000000Z", kind: "problem", platform: "windows", launcher: "1.2.0",
  note: null, report: "MapleStory 2 (Offline) - play session report\n-- end of report --\n", ...overrides,
});
const gz = value => gzipSync(Buffer.from(JSON.stringify(value)));

// The Worker reads only url, method, headers and arrayBuffer(); a plain object lets the tests set Content-Length.
function request(method, path, body, headers) {
  const bytes = Buffer.from(body ?? "");
  return {
    method, url: `https://reports.test${path}`, headers: new Headers(headers),
    arrayBuffer: async () => bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength),
  };
}
const post = (e, body, { ip = "203.0.113.5", type = "application/json", length } = {}) => {
  const headers = { "cf-connecting-ip": ip };
  if (type) headers["content-type"] = type;
  if (length !== null) headers["content-length"] = String(length ?? Buffer.from(body).length);
  return worker.fetch(request("POST", "/v1/report", body, headers), e);
};
const get = (e, path, token = TOKEN) => worker.fetch(request("GET", path, null, token ? { authorization: `Bearer ${token}` } : {}), e);
const count = e => e.DB.raw.prepare("SELECT COUNT(*) AS n FROM reports").get().n;
const insert = (e, id, day, received, body = "", sender = "s") =>
  e.DB.raw.prepare("INSERT INTO reports VALUES (?, ?, ?, 'i', ?, 'problem', 'windows', '1.1.9', ?)").run(id, day, received, sender, body);

test("a report is stored once and read back only with the token", async () => {
  const e = env();
  const r = report({ note: "the quest did not finish" });
  assert.equal((await post(e, gz(r))).status, 201);
  assert.equal((await post(e, gz(r))).status, 201, "a resend is answered like the first");
  assert.equal(count(e), 1);
  assert.equal((await get(e, "/v1/reports", null)).status, 401);
  assert.equal((await get(e, "/v1/reports", "wrong")).status, 401);
  assert.equal((await get(e, `/v1/reports/${r.reportId}`, "wrong")).status, 401);

  const listing = await (await get(e, "/v1/reports?since=2000-01-01")).json();
  assert.equal(listing.length, 1);
  assert.equal(listing[0].id, r.reportId);
  assert.equal(listing[0].installId, r.installId);
  const file = await get(e, `/v1/reports/${r.reportId}`);
  assert.equal(file.status, 200);
  assert.deepEqual(JSON.parse(gunzipSync(Buffer.from(await file.arrayBuffer()))), r);
  assert.equal((await get(e, `/v1/reports/${crypto.randomUUID()}`)).status, 404);
  assert.ok(!JSON.stringify(e.DB.raw.prepare("SELECT * FROM reports").get()).includes("203.0.113.5"), "the IP address is not stored");
});

test("a plain JSON body is accepted and stored gzipped", async () => {
  const e = env();
  const r = report();
  assert.equal((await post(e, JSON.stringify(r))).status, 201);
  const file = await get(e, `/v1/reports/${r.reportId}`);
  assert.deepEqual(JSON.parse(gunzipSync(Buffer.from(await file.arrayBuffer()))), r);
});

test("invalid, oversized and browser-made requests are rejected", async () => {
  const e = env();
  assert.equal((await post(e, "not json")).status, 400);
  assert.equal((await post(e, Buffer.from([0x1f, 0x8b, 1, 2, 3]))).status, 400, "damaged gzip");
  assert.equal((await post(e, Buffer.concat([gz(report()), Buffer.from("junk")]))).status, 400, "bytes after the gzip data");
  assert.equal((await post(e, gz(report({ schema: 2 })))).status, 400);
  assert.equal((await post(e, gz(report({ kind: "other" })))).status, 400);
  assert.equal((await post(e, gz(report({ installId: "../x" })))).status, 400);
  assert.equal((await post(e, gz(report({ note: "x".repeat(2_001) })))).status, 400);
  assert.equal((await post(e, gz(report({ report: 5 })))).status, 400);
  assert.equal((await post(e, gz(report({ extra: { deep: [[[[1]]]] } })))).status, 400, "unknown field");
  assert.equal((await post(e, gz([1, 2]))).status, 400);
  assert.equal((await post(e, Buffer.alloc(512 * 1024 + 1))).status, 413);
  assert.equal((await post(e, gz(report()), { length: 600_000 })).status, 413, "declared too large");
  assert.equal((await post(e, gzipSync(Buffer.alloc(5 * 1024 * 1024, 0x20)))).status, 413, "gzip bomb");
  assert.equal((await post(e, gz(report()), { length: null })).status, 411, "no Content-Length");
  assert.equal((await post(e, gz(report()), { type: "text/plain" })).status, 415, "what a web page could send");
  assert.equal(count(e), 0);
  assert.equal((await post(e, gz(report({ installId: "unknown" })))).status, 201, "the launcher's fallback install id");
});

test("daily limits per install and per sender; an IPv6 /64 is one sender", async () => {
  const e = env();
  const first = report();
  assert.equal((await post(e, gz(first))).status, 201);
  for (let i = 1; i < 20; i++) {
    assert.equal((await post(e, gz(report()))).status, 201);
  }
  assert.equal((await post(e, gz(report()))).status, 429, "21st report of one install");
  assert.equal((await post(e, gz(first))).status, 201, "a resend at the limit is still answered as stored");

  const v6 = env();
  for (let install = 0; install < 5; install++) {
    for (let i = 0; i < 20; i++) {
      const ip = `2001:db8:1:2:${install.toString(16)}::${(i + 1).toString(16)}`;
      assert.equal((await post(v6, gz(report({ installId: `install-${install}-0000` })), { ip })).status, 201);
    }
  }
  assert.equal((await post(v6, gz(report({ installId: "another-install" })), { ip: "2001:0db8:0001:0002:ffff::9" })).status, 429,
    "101st report of one /64");
  assert.equal((await post(v6, gz(report({ installId: "another-install" })), { ip: "2001:db8:1:3::1" })).status, 201, "another network");
});

test("the daily byte budget keeps the database under its size limit", async () => {
  const e = env();
  insert(e, "big", today(), new Date().toISOString(), "x".repeat(5 * 1024 * 1024 - 100));
  assert.equal((await post(e, gz(report()))).status, 503);
  assert.equal(count(e), 1);
});

test("the listing comes in pages of 1,000 after a cursor", async () => {
  const e = env();
  insert(e, "old", "2000-01-01", "2000-01-01T00:00:00.000Z");
  for (let i = 0; i < 1_200; i++) {
    insert(e, `r${String(i).padStart(4, "0")}`, "2026-10-21", "2026-10-21T10:00:00.000Z"); // one received time: the id decides
  }
  const page1 = await (await get(e, "/v1/reports?since=2026-10-01")).json();
  assert.equal(page1.length, 1_000);
  const last = page1.at(-1);
  const page2 = await (await get(e, `/v1/reports?since=2026-10-01&after=${encodeURIComponent(last.received)}&afterId=${last.id}`)).json();
  assert.equal(page2.length, 200);
  assert.equal(new Set([...page1, ...page2].map(row => row.id)).size, 1_200);
});

test("other routes, and a service without its token", async () => {
  const e = env();
  assert.equal((await worker.fetch(request("GET", "/"), e)).status, 404);
  assert.equal((await worker.fetch(request("POST", "/v1/reports", "{}", { "content-type": "application/json", "content-length": "2" }), e)).status, 404);
  assert.equal((await worker.fetch(request("GET", "/v1/report"), e)).status, 404);
  const bare = { DB: d1() };
  assert.equal((await post(bare, gz(report()))).status, 503);
  assert.equal((await worker.fetch(request("GET", "/v1/reports", null, { authorization: "Bearer " }), bare)).status, 401);
});

test("the daily cron deletes reports older than 90 days and the sender hashes of past days", async () => {
  const e = env();
  const r = report();
  assert.equal((await post(e, gz(r))).status, 201);
  insert(e, "old", "2000-01-01", "2000-01-01T00:00:00.000Z");
  insert(e, "yesterday", new Date(Date.now() - 86_400_000).toISOString().slice(0, 10), "2026-10-20T10:00:00.000Z", "", "hash");
  await worker.scheduled({ scheduledTime: Date.now() }, e);
  const rows = Object.fromEntries(e.DB.raw.prepare("SELECT id, sender FROM reports").all().map(row => [row.id, row.sender]));
  assert.deepEqual(Object.keys(rows).sort(), [r.reportId, "yesterday"].sort());
  assert.equal(rows.yesterday, "");
  assert.notEqual(rows[r.reportId], "", "today's hash stays for today's limit");
});
