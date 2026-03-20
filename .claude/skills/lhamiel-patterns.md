---
name: lhamiel-patterns
description: Coding patterns extracted from Lhamiel repository - Avalonia MVVM desktop app for archive compression/decompression
version: 1.0.0
source: local-git-analysis
analyzed_commits: 200
---

# Lhamiel Patterns

## Commit Conventions

このプロジェクトでは **混合スタイル** のコミットメッセージが使われている:

### 主要パターン (頻度順)

1. **バージョンプレフィックス** (7%) — `v1.0.XXX: 日本語説明`
   - バージョンバンプ時に使用
   - 例: `v1.0.118: ローカライズ崩壊の真の根本原因を修正`

2. **日本語記述のみ** (最多) — 説明的な日本語メッセージ
   - 例: `不具合修正`, `リファクタリング`, `バージョンタブに7-Zipバージョン表示`

3. **Conventional Commits** (2.5%) — `fix:`, `refactor:`, `CI:` プレフィックス
   - 例: `fix: ARM64ビルドのリストアエラーを修正`

### バージョニング

- 偶数バージョン番号 (v1.0.90, v1.0.94, v1.0.98...)
- 2〜4ずつインクリメント
- `Directory.Build.props` の `<Version>` タグで一元管理

## Code Architecture

```
src/
├── Lhamiel/
│   ├── App.xaml(.cs)               # アプリエントリポイント、ロケール管理
│   ├── Program.cs                  # Velopackブートストラップ
│   ├── Models/                     # データモデル (1ファイル)
│   │   └── FileAssociationItem.cs
│   ├── View/                       # Avalonia XAML + コードビハインド (4ダイアログ)
│   │   ├── MainWindow.xaml(.cs)
│   │   ├── ProgressWindow.xaml(.cs)
│   │   ├── OverwriteConfirmDialog.xaml(.cs)
│   │   └── ErrorRecoveryDialog.xaml(.cs)
│   ├── ViewModels/                 # MVVM ViewModel (1ファイル)
│   │   └── MainWindowViewModel.cs
│   ├── Util/                       # ビジネスロジック全体 (26ファイル)
│   │   ├── Archive*.cs             # アーカイブ処理の中核
│   │   ├── Settings*.cs            # 設定管理
│   │   ├── Native*.cs / Shell*.cs  # ネイティブ相互運用
│   │   └── ...                     # ファイル操作、IPC、ログ等
│   └── Resources/
│       └── Locales/                # 17言語のローカライズファイル (.axaml)
└── Lhamiel.Tests.Unit/             # xUnit 3 + Moq テスト (7テストファイル)
```

## Hottest Files (変更頻度が高いファイル)

| ファイル | 変更回数 | 役割 |
|---------|---------|------|
| `Util/ArchiveExtractor.cs` | 53 | 展開ロジック — 最もバグ修正が多い |
| `App.xaml.cs` | 50 | ロケール管理、初期化 |
| `Util/ArchiveProcessor.cs` | 45 | オーケストレーター |
| `Util/ArchiveCompressor.cs` | 32 | 圧縮ロジック |
| `View/MainWindow.xaml.cs` | 30 | メインUI |
| `Directory.Build.props` | 28 | バージョン管理 |
| `ViewModels/MainWindowViewModel.cs` | 20 | ViewModel |

## Workflows

### バージョンリリース

1. コード修正・機能追加
2. `Directory.Build.props` の `<Version>` を更新
3. コミット: `v1.0.XXX: 変更内容の説明`
4. `release/*` ブランチにプッシュ → CI/CDが自動リリース

### ローカライズ追加/修正

1. `Resources/Locales/{xx_YY}.axaml` を作成/編集 (全17ファイル同時変更)
2. `App.xaml` に `ResourceInclude` エントリを追加
3. `App.SupportedLocales` と `App.LocaleDisplayNames` を更新
4. `MainWindowViewModel.LocaleOptions` に選択肢を追加

### 不具合修正 (典型的フロー)

1. `ArchiveExtractor.cs` または `ArchiveProcessor.cs` を修正
2. 関連テストを追加/更新 (Tests.Unit/)
3. `App.xaml.cs` で初期化やエラーハンドリングを調整
4. バージョンバンプ → リリース

### Native AOT 対応

- `PublishAot=true` で公開
- リフレクション回避パターンを使用
- `TrimmerRoots.xml` でアセンブリ保持
- `AppJsonContext.cs` で System.Text.Json のソースジェネレーター使用

## File Co-change Patterns

| グループ | 同時変更ファイル | トリガー |
|---------|---------------|---------|
| ローカライズ | 全17個の `*.axaml` ファイル | ロケールキー追加/修正 |
| アーカイブコア | `ArchiveExtractor.cs` + `ArchiveProcessor.cs` | バグ修正・機能変更 |
| バージョン | `Directory.Build.props` + 機能ファイル | リリース |
| UI変更 | `MainWindow.xaml` + `MainWindowViewModel.cs` + `App.xaml.cs` | UI機能追加 |
| CI/CD | `.github/workflows/*.yml` | ビルド/リリースプロセス変更 |

## Testing Patterns

- **フレームワーク**: xUnit 3 + Moq
- **テストプロジェクト**: `Lhamiel.Tests.Unit/`
- **テストファイル命名**: `{対象クラス名}Tests.cs`
- **テスト分離**: `SequentialCollection.cs` で順序実行コレクション定義
- **カバレッジ対象**: アーカイブ処理、設定、進捗ロジック、ロケール

## Technology Stack

| コンポーネント | 技術 |
|-------------|------|
| UI | Avalonia 11 + FluentTheme |
| パターン | MVVM (CommunityToolkit.Mvvm) |
| アーカイブ | 1llum1n4t1s.Sevenzip (7z.dll) |
| シリアライズ | System.Text.Json (AOTソースジェネレーター) |
| テスト | xUnit 3 + Moq |
| 更新 | Velopack |
| CI/CD | GitHub Actions |
| ターゲット | .NET 10.0 / win-x64 + win-arm64 |
