# Agent Entry

This is the Codex entry point for Alpheratz.
Common ethics, engineering philosophy, comment policy, and workflow rules live in the Codex global rules.

## Next Documents

- `.claude/README.md` - project-local index, current implementation contracts, packaging handoff, and Claude workspace settings.
- `README.md` - public project overview and repository layout.
- `docs/spec.md` - public feature specification and data flow.
- `docs/architecture.md` - public process, module responsibilities, state ownership, events, and lifecycle reference.
- `docs/database.md` - public SQLite schema, user-data backup, and current compatibility reference.
- `docs/tech-stack.md` - public stack and design decision reference.
- `docs/basic-design.html` - public screen layout basic design reference.

## Routing

- Need project-local implementation contracts or packaging cautions: read `.claude/README.md`.
- Need GitHub-visible or user-facing explanation: read `README.md`.
- Need public feature conditions: read `docs/spec.md`; internal structure and execution paths: `docs/architecture.md`; storage: `docs/database.md`; dependencies, commands, and distribution: `docs/tech-stack.md`; screen design: `docs/basic-design.html`.
- Need an internal note, audit handoff, or archived working sample: keep it under `.claude/`; do not move it into public docs.
- Duplicate-photo deletion can remove source files after explicit selection and confirmation. Keep its filesystem effects distinct from folder index cleanup and database user-data backup.
