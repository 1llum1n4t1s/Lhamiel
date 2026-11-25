# Lhamiel Design

現在のシステム構成、責務境界、データフロー、不変条件、採用した設計判断の正本。詳細は `docs/design/` に分け、運用コマンドと実装規約は [AGENTS.md](AGENTS.md) に置く。

## Purpose and platform boundary

Lhamiel は Windows 用のアーカイブ圧縮・展開デスクトップアプリ。.NET 11 と Avalonia 12 で x64 / ARM64 の Native AOT を配布する。名前付きパイプ、シェル連携、COM、レジストリ、DPAPI、Authenticode、`LoadLibraryW` など Windows の機能に意図的に依存し、Linux / macOS への移植性は対象にしない。

## Major components and responsibilities

`Program` / `App` が起動と振り分け、`MainWindowViewModel` が UI 状態、`CompressionWorkerLauncher` が worker 起動、`ArchiveProcessor` と `ArchiveExtractor` / `ArchiveCompressor` が処理を担う。全体は[コンポーネント一覧](docs/design/major-components-and-responsibilities.md)を参照。

## Runtime data flows

全入口の圧縮・展開は設定スナップショットを持つ独立 worker で動き、一時出力を検証してから確定する。起動、展開、圧縮、設定と秘密、問い合わせの流れは[データフロー](docs/design/runtime-data-flows.md)を参照。

### Update and release

クライアントは固定の R2 ドメインから更新を取得し、署名済み成果物だけを配信する。[更新と配信](docs/design/release-process.md#update-and-release)を参照。

## Critical invariants

ネイティブ処理の直列化、展開パスの検証、リンク境界、既存出力の復元、容量検査、秘密の秘匿、署名などの条件は[不変条件](docs/design/critical-invariants.md)を参照し、変更時は必ず守る。

## Adopted design decisions and trade-offs

DI なしの MVVM、一時展開と最終移動、シェル登録、並行ゲート、除外処理、UnLhaRe、ダイアログ、SDK 復元などの理由と代償は[設計判断](docs/design/adopted-design-decisions-and-trade-offs.md)を参照。

### Local signed releases to R2

GitHub Actions とローカルは同じ `release-local.ps1` を使い、署名と再開の境界を共有する。[署名付きリリース](docs/design/release-process.md#local-signed-releases-to-r2)を参照。

## 製品ページの配信先

製品ページはアプリのリリースと独立して VPS へ配信する。[配信先](docs/design/release-process.md#製品ページの配信先)を参照。
