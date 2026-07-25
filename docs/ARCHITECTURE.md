# Lhamiel アーキテクチャドキュメント

## 概要

LhamielはWindows向けのアーカイブ圧縮・展開デスクトップアプリ。Avalonia 12 + MVVM（CommunityToolkit.Mvvm）で構築。UIは日本語で17言語対応。

## 技術スタック

- **フレームワーク**: .NET 10.0
- **UIフレームワーク**: Avalonia 12（AXAML、compiled bindings）
- **圧縮ライブラリ**: 1llum1n4t1s.Sevenzip（7z.dll ラッパー）
- **MVVM**: CommunityToolkit.Mvvm（`[ObservableProperty]` ソースジェネレーター）
- **自動更新**: Velopack（2 系統運用: `Program.cs --update-check` サイレント CLI 経路 + `App.Check4Update` UI 経路（VelopackUpdateDialog.Avalonia ダイアログ））
- **テーマ**: FluentTheme
- **ビルド**: Native AOT 対応（`PublishAot=true`）

## レイヤー構造

```
┌─────────────────────────────────────┐
│        View Layer (UI)              │
│  - MainWindow.axaml                 │
│  - ProgressWindow.axaml             │
│  - FileConflictDialog.axaml         │
│  - ErrorRecoveryDialog.axaml        │
│  - DiskSpaceDialog.axaml            │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│      ViewModel Layer                │
│  - MainWindowViewModel              │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│      Utility Layer (Business Logic) │
│  - ArchiveProcessor（オーケストレーター）│
│  - ArchiveExtractor（展開）         │
│  - ArchiveCompressor（圧縮）        │
│  - ArchiveErrorHandler（エラー分類）│
│  - PartialExtractionHandler（部分展開）│
│  - DiskSpaceChecker（容量チェック） │
│  - Settings / SettingsManager       │
│  - Logger                           │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│    Infrastructure Layer             │
│  - FileAssociation（レジストリ）    │
│  - FileIconHelper（Shell API）      │
│  - ShortcutCreator（COM）           │
│  - StartupRegistration（HKCU\Run）  │
│  - ShellContextMenu（Win11/従来メニュー）│
│  - PathValidator                    │
│  - NativeLibraryManager（7z.dll）   │
│  - IpcService（Named Pipe）         │
│  - MotwPropagator（Zone.Identifier）│
│  - CrashHandler（MiniDump）         │
│  - DiagnosticsCollector（support ZIP）│
│  - TempCleanup（一時ファイル掃除）  │
│  - UpdateChecker（Velopack サイレント）│
│  - App.Check4Update（VelopackUpdateDialog UI）│
│  - LhamielUpdateStrings（IUpdateDialogStrings）│
└─────────────────────────────────────┘
```

## 主要コンポーネント

### View Layer

| ファイル | 責務 |
|---------|------|
| `MainWindow.axaml` | メインウィンドウ。D&D、設定画面（タブ切替） |
| `ProgressWindow.axaml` | 展開/圧縮の進捗表示。キャンセル機能、ディスク容量警告表示 |
| `FileConflictDialog.axaml` | ファイル衝突解決。展開時（2ペイン比較）と圧縮時（グリッドリスト）の両モード |
| `ErrorRecoveryDialog.axaml` | エラー発生時のリトライ/スキップ選択 |
| `DiskSpaceDialog.axaml` | ディスク容量不足時の一時停止・再開/キャンセル |
| `PasswordDialog.axaml` | パスワード保護アーカイブの入力ダイアログ。誤入力時は再試行表示、最大 3 回で自動キャンセル |

### ViewModel Layer

`MainWindowViewModel` — 全設定バインディング、D&Dからの処理振り分け、フォルダオープン

### Utility Layer

| クラス | 責務 |
|--------|------|
| `ArchiveProcessor` | オーケストレーター。展開/圧縮の判定、並列処理制御（SemaphoreSlim） |
| `ArchiveExtractor` | 展開ロジック。スマート解凍（二重フォルダ防止）、一時フォルダ方式 |
| `ArchiveCompressor` | 圧縮ロジック。ファイルスキャン、衝突検出、リネーム解決 |
| `ArchiveErrorHandler` | エラー分類（Critical/Recoverable/Warning） |
| `PartialExtractionHandler` | 破損アーカイブの選択的展開、エラーリトライ |
| `DiskSpaceChecker` | 事前容量チェック + 定期監視（10秒間隔） |
| `Settings` / `SettingsManager` | JSON設定。シングルトン管理、`MutateAndSave` で atomic 更新（Round 3c で追加） |
| `IpcService` | Named Pipe による二重起動引数の引き継ぎ。`PipeOptions.CurrentUserOnly` |
| `MotwPropagator` | 展開後ファイルへ Zone.Identifier ADS 伝播 |
| `CrashHandler` | 未処理例外時の MiniDump 生成 |
| `DiagnosticsCollector` | サポート用 ZIP（masked settings / logs / dumps / env info） |
| `TempCleanup` | 起動時に `%TEMP%\Lhamiel_Temp_*` の残骸を MinAge=30 分超で削除 |
| `App.Check4Update` | Velopack 自動更新の UI 経路。`Settings.Check4UpdatesOnStartup=true` で起動時 + 「アップデート確認」ボタンから手動起動。`VelopackUpdateDialog.UpdateDialogWindow` 経由 |
| `LhamielUpdateStrings` | `VelopackUpdateDialog.IUpdateDialogStrings` の Lhamiel 実装（シングルトン、`App.Text` 動的解決） |
| `UpdateChecker` | Velopack 自動更新のサイレント CLI 経路 (`--update-check`)。`StartupRegistration` の HKCU\Run 経由で Windows ログイン時に起動 |

詳細なクラス責務は [CLAUDE.md](../CLAUDE.md) の Key Util Classes 表を参照（こちらが single source of truth）。

## データフロー

### 展開フロー

```
D&D → MainWindowViewModel.ProcessDroppedPathsAsync
  → ArchiveProcessor.ExtractArchiveAsync
    → GetArchiveStructureInfo（構造解析）
    → 出力先決定:
        CreateArchiveNameFolder=ON:
          ルートフォルダがアーカイブ名と一致 → フォルダ作成スキップ（baseDir直接展開）
          それ以外 → baseDir/アーカイブ名/ を作成して展開
        CreateArchiveNameFolder=OFF:
          常に baseDir
    → DiskSpaceChecker.EnsureDiskSpaceAsync（容量チェック）
    → 既存ファイルあり？ → ExtractViaTempFolderAsync:
        一時フォルダに展開 → 衝突検出 → FileConflictDialog → 選択的移動
      既存ファイルなし？ → ExtractArchive（直接展開）
        └─ 暗号化エントリ検出時:
           ArchiveReader が ICryptoGetTextPassword コールバック発火
             → AsyncPasswordQuery → PasswordDialog.ShowFromBackgroundAsync
               → OK: パスワードを 7z.dll に返却 / NG: 次回再試行（最大 3 回）
               → Cancel or 上限超過: userCancelledPassword=1 → EncryptionException を
                 OperationCanceledException に変換して上位へ伝搬
```

### 圧縮フロー

```
D&D → MainWindowViewModel.ProcessDroppedPathsAsync
  → CompressMultipleAsOne?
    YES → ArchiveProcessor.CompressMergedAsync
      → ScanSourceFiles（DirectoryStructureMode適用）
      → DetectConflicts → 衝突あり？ → FileConflictDialog
      → CompressFilesAsync
    NO → ArchiveProcessor.CompressItemsAsync
      → 各ファイルに CompressItemAsync
```

## ファイル衝突解決

`FileConflictDialog` が展開・圧縮両方の衝突を統合処理:

- **展開時（2ペイン）**: 左「現在の場所」右「宛先の場所」でファイル比較。ヘッダーチェックで一括選択
- **圧縮時（グリッド）**: 同名ファイルをグループ表示。グループヘッダーチェックで一括選択
- **スキップ機能**: 日付とサイズが同じファイルをフィルタリング

## ローカライズ

17言語対応。`Resources/Locales/*.axaml` で ResourceDictionary 方式。
`App.SetLocale()` で実行時切替。`App.Text(key)` で文字列取得（自動で `Text.` プレフィックス付与）。

## CI/CD

- **PRビルド**: `.github/workflows/dotnet-build.yml`
- **リリース**: `.github/workflows/velopack-release.yml`（`release/*` ブランチ → Native AOT → Velopack → Cloudflare R2 `lhamiel-updates` バケット）
- **バージョン**: `Directory.Build.props` の `<Version>` タグで一元管理

## Velopack 自動更新 (配信戦略)

- **配信元**: Cloudflare R2 単独 (`https://lhamiel.kagayoi.com`)。`SimpleWebSource` 経由で取得。中立ドメイン `kagayoi.com` に移行済み（旧 `lhamiel.1llum1n4t1.com` はクラウド/企業 egress の SNI フィルタで false positive を起こすため）。
- **旧クライアント救済**: GitHub Releases には `kagayoi.com` 版を **踏み台** として publish。旧 GithubSource クライアント (v1.0.167 以下) 救済のため永続保持。通常リリースは R2 のみへ配信。
- **2 系統経路**: (1) `Program.cs --update-check` サイレント CLI 経路（`StartupRegistration` HKCU\Run から発火、UI 無し）、(2) `App.Check4Update` UI 経路（`VelopackUpdateDialog.Avalonia` 経由、`Check4UpdatesOnStartup=true` で起動時自動 + メニューから手動）。
- **配信元固定の根拠**: `Settings.UpdateBaseUrl` は `[JsonIgnore]` + getter-only でハードコード固定 (`CanonicalUpdateBaseUrl`)、settings.json 改竄経由の悪意ある第三者ホスト誘導を防ぐ。
- 詳細は CLAUDE.md の Velopack セクションを Single Source of Truth として参照。
