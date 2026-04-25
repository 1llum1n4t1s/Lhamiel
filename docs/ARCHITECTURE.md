# Lhamiel アーキテクチャドキュメント

## 概要

LhamielはWindows向けのアーカイブ圧縮・展開デスクトップアプリ。Avalonia 12 + MVVM（CommunityToolkit.Mvvm）で構築。UIは日本語で17言語対応。

## 技術スタック

- **フレームワーク**: .NET 10.0
- **UIフレームワーク**: Avalonia 12（AXAML、compiled bindings）
- **圧縮ライブラリ**: 1llum1n4t1s.Sevenzip（7z.dll ラッパー）
- **MVVM**: CommunityToolkit.Mvvm（`[ObservableProperty]` ソースジェネレーター）
- **自動更新**: Velopack
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
│  - PathValidator                    │
│  - NativeLibraryManager（7z.dll）   │
│  - UpdateChecker（Velopack）        │
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
| `Settings` / `SettingsManager` | JSON設定。シングルトン管理 |

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
- **リリース**: `.github/workflows/velopack-release.yml`（`release/*` ブランチ → Native AOT → Velopack → GitHub Releases）
- **バージョン**: `Directory.Build.props` の `<Version>` タグで一元管理
