---
id: lhamiel-archive-extractor-hotspot
trigger: "when modifying ArchiveExtractor.cs or fixing archive-related bugs"
confidence: 0.85
domain: architecture
source: local-repo-analysis
---

# ArchiveExtractor.cs はホットスポット — 慎重に変更する

## Action
`ArchiveExtractor.cs` を変更する際は、必ず `ArchiveProcessor.cs` への影響も確認する。変更後はユニットテスト (`ArchiveExtractorTests.cs`) を実行し、回帰がないことを確認する。

## Evidence
- 200コミット中53回変更 — リポジトリで最も変更頻度が高いファイル
- バグ修正の大半がこのファイルに集中
- `ArchiveProcessor.cs` (45回変更) と頻繁に同時変更される
