-- D1 schema for the Bootsharp Cloudflare data sample.
--
-- Applied by `npx wrangler d1 migrations apply bootsharp-cf-data --local`, once per checkout.
-- The ORM is built with UseAutoSyncStructure(false) on purpose: this file is the only definition
-- of the table, so the two providers cannot disagree about what they are reading.

CREATE TABLE IF NOT EXISTS notes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  body TEXT NOT NULL,
  -- Written only by these defaults and by UPDATE. created_at never moves; updated_at is what makes
  -- a successful PUT observable in the response body rather than only in a status code.
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- The list endpoints order by (created_at DESC, id DESC) and window with LIMIT/OFFSET. Without a
-- matching index that ordering is a full scan and a sort, so the ORDER BY in the SQL would be
-- honest about intent and dishonest about cost.
CREATE INDEX IF NOT EXISTS notes_created_at_id ON notes (created_at DESC, id DESC);
