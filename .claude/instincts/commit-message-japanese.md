---
id: lhamiel-commit-message-japanese
trigger: "when writing a commit message"
confidence: 0.9
domain: git
source: local-repo-analysis
---

# コミットメッセージは日本語で書く

## Action
コミットメッセージは日本語で記述する。以下のスタイルを状況に応じて使い分ける:
- **バージョンリリース**: `v1.0.XXX: 変更内容`
- **バグ修正**: `fix: 具体的な修正内容` または `不具合修正`
- **リファクタリング**: `refactor: 内容` または `リファクタリング`
- **機能追加/変更**: 具体的な日本語記述

## Evidence
- 200コミット中、95%以上が日本語のコミットメッセージ
- Conventional Commits プレフィックスの使用は2.5%と低い
- プロジェクトのUI言語が日本語であり、開発言語も日本語が主
