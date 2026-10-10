-- Beat Bash Bonanza leaderboard schema (Cloudflare D1).
-- Apply with: wrangler d1 execute bonanza-leaderboard --file=worker/schema.sql

CREATE TABLE IF NOT EXISTS scores (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  game_id TEXT NOT NULL,
  player_name TEXT NOT NULL,
  score INTEGER NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Every read is "top N for one game_id, ordered by score" — index for that.
CREATE INDEX IF NOT EXISTS idx_scores_game_score ON scores (game_id, score DESC);
