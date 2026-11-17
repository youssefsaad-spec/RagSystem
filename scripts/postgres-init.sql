-- Runs once, when the pgdata volume is first created. The EF Core migration also creates the extension (safe to repeat).
CREATE EXTENSION IF NOT EXISTS vector;
