# Lhamiel アーキテクチャドキュメント

## 概要

Lhamielは、Windows向けのWPFベースのアーカイブ圧縮・展開ツールです。複数の圧縮形式をサポートし、ユーザーフレンドリーなGUIとファイル関連付け機能を提供します。

## 技術スタック

- **フレームワーク**: .NET 10.0 (Windows 10 26100.0以上)
- **UIフレームワーク**: WPF (Windows Presentation Foundation)
- **圧縮ライブラリ**: Cube.FileSystem.SevenZip
- **自動更新**: Velopack
- **ビルドシステム**: MSBuild / dotnet CLI

## アーキテクチャ構成

### レイヤー構造

```
┌─────────────────────────────────────┐
│        View Layer (UI)              │
│  - MainWindow.xaml                  │
│  - ProgressWindow.xaml              │
│  - OverwriteDialog.xaml             │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│      Utility Layer (Business Logic) │
│  - ArchiveProcessor                 │
│  - ArchiveExtractor                 │
│  - ArchiveErrorHandler              │
│  - PartialExtractionHandler         │
│  - Settings                         │
│  - Logger                           │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│    Infrastructure Layer             │
│  - FileAssociation (レジストリ)     │
│  - ShortcutCreator (COM相互運用)    │
│  - PathValidator                    │
│  - ArchiveFormatDetector            │
└─────────────────────────────────────┘
```

### 主要コンポーネント

#### 1. View Layer (プレゼンテーション層)

**MainWindow.xaml.cs**
- アプリケーションのメインウィンドウ
- ドラッグ&ドロップ処理
- ファイル関連付け管理
- ユーザー操作のエントリーポイント

**ProgressWindow.xaml.cs**
- 長時間処理の進捗表示
- キャンセル機能
- 非同期処理との連携

**OverwriteDialog.xaml.cs**
- ファイル上書き確認ダイアログ
- ユーザー選択の収集

#### 2. Utility Layer (ビジネスロジック層)

**ArchiveProcessor**
- アーカイブ処理の共通化
- 圧縮・展開処理の統合管理
- エラーハンドリングの統合

**ArchiveExtractor**
- アーカイブ展開の実装
- 出力ディレクトリの決定
- 自己展開形式の検出

**ArchiveErrorHandler**
- エラー分析と分類
- 破損アーカイブの検出
- リカバリー提案

**PartialExtractionHandler**
- 選択的ファイル展開
- エラーリトライ機能
- 部分展開結果の管理

**Settings**
- アプリケーション設定の管理
- JSON形式での永続化
- 設定の検証

**Logger**
- ログレベル付きログ記録
- ファイル出力
- 本番環境対応

#### 3. Infrastructure Layer (インフラストラクチャ層)

**FileAssociation**
- Windowsファイル関連付けの管理
- レジストリ操作 (HKEY_CURRENT_USER)
- アイコン登録

**ShortcutCreator**
- デスクトップショートカット作成
- COM相互運用 (WScript.Shell)
- リフレクションベースの型安全な実装

**PathValidator**
- ファイルパスの検証
- パストラバーサル対策
- Windows予約名チェック

**ArchiveFormatDetector**
- 自己展開アーカイブの検出
- マジックナンバー検証
- 複数フォーマット対応

**ArchiveConstants**
- マジックナンバー定数
- ファイルサイズ制限定数
- 共通定数の集約

## データフロー

### 1. アーカイブ展開フロー

```
ユーザー操作 (ファイル選択/D&D)
    ↓
MainWindow.ExtractArchiveButton_Click
    ↓
ArchiveProcessor.ExtractArchiveAsync
    ↓
┌─ ファイル形式チェック (.exe → IsSelfExtractingArchive)
│
├─ ArchiveExtractor.GetOutputDirectory
│  └─ 出力先決定 (同ディレクトリ or 指定ディレクトリ)
│
├─ Cube.FileSystem.SevenZip.ArchiveReader
│  └─ アーカイブ読み込み
│
└─ ProgressWindow表示
   ├─ 非同期展開処理
   ├─ 進捗コールバック
   └─ エラーハンドリング
       └─ ArchiveErrorHandler.AnalyzeError
```

### 2. アーカイブ圧縮フロー

```
ユーザー操作 (フォルダ選択)
    ↓
MainWindow.CompressFolderButton_Click
    ↓
ArchiveProcessor.CompressFolderAsync
    ↓
Settings.CompressionFormat取得
    ↓
Cube.FileSystem.SevenZip.ArchiveWriter
    ↓
ProgressWindow表示
    └─ 非同期圧縮処理
```

### 3. 設定管理フロー

```
アプリケーション起動
    ↓
Settings.Load()
    ├─ settings.json読み込み
    └─ デフォルト値設定 (存在しない場合)
    ↓
メモリ上で管理
    ↓
ユーザー設定変更
    ↓
Settings.Save()
    └─ settings.json書き込み
```

## エラーハンドリング戦略

### エラー分類

1. **Critical Errors (致命的エラー)**
   - ファイルアクセス拒否
   - メモリ不足
   - → ユーザーに通知して処理中断

2. **Recoverable Errors (回復可能エラー)**
   - 部分的な破損
   - 一時的なI/Oエラー
   - → リトライまたはスキップオプション提示

3. **Warnings (警告)**
   - パフォーマンス低下
   - 非推奨機能の使用
   - → ログ記録のみ

### エラーハンドリングオプション

- **StopOnError**: 最初のエラーで処理停止
- **SkipOnError**: エラーファイルをスキップして継続
- **AutoRetry**: 自動的に3回リトライ
- **AskUser**: 各エラーごとにユーザーに確認

## ログ戦略

### ログレベル

- **Debug**: 開発時のデバッグ情報 (DEBUGモードのみ)
- **Info**: 一般的な情報ログ (DEBUGモード時のみ)
- **Warning**: 警告 (常に記録)
- **Error**: エラー (常に記録)

### ログ出力

- ファイル: `Lhamiel.log` (アプリケーションディレクトリ)
- 最大行数: 1000行 (自動ローテーション)
- InnerException含む詳細なスタックトレース記録

## セキュリティ考慮事項

### 1. ファイルパス検証

- PathValidator による包括的な検証
- パストラバーサル攻撃対策
- Windows予約デバイス名チェック
- パス長制限チェック

### 2. レジストリ操作

- HKEY_CURRENT_USER のみ使用 (管理者権限不要)
- 操作後の検証
- セキュリティイベントのログ記録

### 3. COM相互運用

- リフレクションベースの型安全な実装
- 適切なCOMオブジェクト解放
- エラーハンドリング

## パフォーマンス考慮事項

### 既知の制限

**部分展開のパフォーマンス問題** (`PartialExtractionHandler.cs:239-243`)
- 現在: 全アーカイブを一時展開してからコピー
- 影響: 大きなアーカイブで著しいパフォーマンス低下
- 改善案: Cube.FileSystem.SevenZip の個別ファイル展開APIの利用検討

### 非同期処理

- すべての長時間処理は async/await パターンで実装
- CancellationToken によるキャンセルサポート
- UIスレッドのブロッキング回避

## 拡張性

### 新しい圧縮形式の追加

1. `Settings.SupportedCompressionFormats` に形式を追加
2. Cube.FileSystem.SevenZip が対応していることを確認
3. 必要に応じて `ArchiveFormatDetector` にシグネチャ検出を追加

### 新しいエラーハンドリングオプションの追加

1. `ErrorHandlingOption` enum に新しいオプションを追加
2. `PartialExtractionHandler.DetermineErrorHandling` にロジックを追加
3. UI側のダイアログに選択肢を追加

## テスト戦略

### ユニットテスト (Lhamiel.Tests.Unit)

- Settings クラスのテスト
- PathValidator のテスト
- ArchiveConstants の検証
- ArchiveFormatDetector のロジックテスト

### 統合テスト (今後の実装)

- 実際のアーカイブファイルを使用した展開テスト
- エラーシナリオのテスト
- エッジケースのテスト

### CI/CD

- GitHub Actions によるビルド自動化
- テスト実行
- コード品質チェック (dotnet format)
- 依存関係の脆弱性スキャン

## 自動更新メカニズム

- Velopack を使用した自動更新
- GitHub Releases からの配布
- バックグラウンドでの更新チェック
- ユーザー確認後の更新適用

## 設定ファイル形式

`settings.json` の詳細は `SETTINGS_SCHEMA.md` を参照してください。

## 今後の改善計画

### 短期 (1-2ヶ月)

1. 部分展開パフォーマンスの改善
2. テストカバレッジの向上
3. エラーメッセージの多言語化

### 中期 (3-6ヶ月)

1. 圧縮レベルの選択機能
2. パスワード保護アーカイブのサポート
3. プラグインアーキテクチャの導入

### 長期 (6ヶ月以上)

1. クラウドストレージ統合
2. コマンドラインインターフェース
3. Linux/macOS サポート (.NET MAUI への移行検討)
