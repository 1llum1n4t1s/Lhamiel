---
id: lhamiel-version-bump
trigger: "when releasing a new version or bumping the version number"
confidence: 0.9
domain: release
source: local-repo-analysis
---

# バージョンは Directory.Build.props で一元管理

## Action
バージョン更新時は `Directory.Build.props` の `<Version>` タグのみを変更する。偶数番号で2〜4ずつインクリメントする。コミットメッセージは `v1.0.XXX: 変更内容の説明` 形式を使用する。

## Evidence
- 200コミット中14回のバージョンバンプが全て `Directory.Build.props` で実施
- バージョンは偶数 (v1.0.90, v1.0.94, v1.0.98, ..., v1.0.122)
- コミットメッセージのフォーマットが一貫している
