# Agent Entry

This is the Codex entry point for Alpheratz.
Common ethics, engineering philosophy, comment policy, and workflow rules live in the Codex global rules.

## Next Documents

- `.claude/README.md` - project-local index, moved agent notes, follow-up notes, and Claude workspace files.
- `README.md` - public project overview and repository layout.
- `docs/spec.md` - public feature specification and data flow.
- `docs/database.md` - public SQLite schema and migration reference.
- `docs/tech-stack.md` - public stack and design decision reference.

## Routing

- Need project-local decisions or prior agent notes: read `.claude/README.md`.
- Need GitHub-visible or user-facing explanation: read `README.md`.
- Need public feature, database, or stack details: read `docs/spec.md`, `docs/database.md`, and `docs/tech-stack.md`.
- Need an internal note, audit handoff, or archived working sample: keep it under `.claude/`; do not move it into public docs.
