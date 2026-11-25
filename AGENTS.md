# AGENTS.md

This file provides guidance to Codex when working in this repository.
本書は全作業に共通する規約の正本とし、詳細は `docs/agents/` に置く。設計・データフローの正本は [DESIGN.md](DESIGN.md)。

## Project Overview

Lhamiel は **Avalonia 12**（WPF ではない）と **.NET 11.0** の Windows 専用アーカイブ圧縮・展開アプリ。UI は日本語を基本に17言語、x64 / ARM64 の Native AOT で配布する。Linux / macOS 対応は想定しない。

## 全作業の規約

- ソリューションは `Lhamiel.slnx`。`TreatWarningsAsErrors` が ON で、警告はビルドエラーになる。
- Native AOT のためリフレクションに依存する実装を避け、DI コンテナを使わず手動で配線する。I/O は async/await + CancellationToken を使う。
- 設計の[不変条件](docs/design/critical-invariants.md)は必ず守る。`NativeArchiveGate` を再入せず、`ArchiveReader.Save` の前に全 `FullName` を検証する。
- 平文パスワード・秘密値・利用者を特定するパスをログや診断情報へ出さない。`Environment.UserName` をログ経路で呼ばない。
- 版は `Directory.Build.props` の `<Version>`、SDK は `global.json` の固定を正本とする。
- 署名を省略したリリースはしない。

## Build & Development Commands

ビルド・依存・SDK・lockfile を扱う前に [build-development-commands.md](docs/agents/build-development-commands.md) を読む。

```bash
dotnet build Lhamiel.slnx -c Debug
dotnet test Lhamiel.slnx -c Release
```

## Architecture

処理フロー・進捗・並行制御・テスト用の差し替え口・ダイアログを変える前に [architecture.md](docs/agents/architecture.md) を読む。

## Localization System

文言・ロケールを変える前に [localization-system.md](docs/agents/localization-system.md) を読む。XAML は `DynamicResource` を使う。

## Key Technical Details

形式、シェル連携、ネイティブ DLL、パスワード、設定、除外規則、UI、署名を変える前に [key-technical-details.md](docs/agents/key-technical-details.md) を読む。

## CI/CD

ワークフロー・配布・リリースを扱う前に [ci-cd.md](docs/agents/ci-cd.md) を読む。公開は版タグで起動する GitHub Actions が行う。

## Testing

テストの追加や検証の前に [testing.md](docs/agents/testing.md) を読む。変更領域ごとの必須テストを定めている。

## Documentation

文書の役割と優先順位は [documentation.md](docs/agents/documentation.md) を参照する。

## ドメイン移行（2026-07 開始・期限 2027/05/31）

ドメイン・配信ホスト・sparse MSIX 名を触る前に [移行規約](docs/agents/2026-07-2027-05-31.md) を読む。旧ホストの経路は期限まで消さず、MSIX 名は改名しない。
