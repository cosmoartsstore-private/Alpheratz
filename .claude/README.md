# Project Agent Workspace

Alpheratz 固有の内部資料だけを置く。現行機能の正本はproduction sourceであり、利用者・開発者向けの確定事項はpublic documentsへ記載する。

## Public Documents

| Document | Scope |
| --- | --- |
| `../README.md` | プロジェクト概要、利用・build・dataの入口 |
| `../docs/spec.md` | 現行機能の前提、判断、成功・部分成功・取消し・失敗の結果 |
| `../docs/architecture.md` | process、module責務、状態所有、event、非同期処理とlifecycle |
| `../docs/database.md` | SQLite・JSON・cache・log・registry、識別、transaction、寿命、backup |
| `../docs/tech-stack.md` | version、architecture decision、build・distribution contract |
| `../docs/basic-design.html` | 画面、layer、layout、interaction state。操作manualではない |

## Agent-only Files

| File | Purpose |
| --- | --- |
| `../AGENT.md` | Codex用の簡潔なproject entryとdocument routing |
| `../CLAUDE.md` | Claude用entry bridge |
| `agent-notes.md` | 現行実装のdo-not-rework事項と、実在する保守scope |
| `xaml-packaging-notes.md` | unpackaged self-contained WinUIのXBF/PRI契約と検証手順 |
| `settings.json` | Claude workspace permission settings。documentationや一時成果物ではない |

一時調査、audit log、screenshot、generated reportをこのdirectoryへ残さない。handoffとして残す必要がある内容は、解決済み履歴ではなく現行契約へ書き換えてから既存の2文書へ統合する。
