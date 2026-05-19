# Lhamiel Settings Schema

## ファイルの場所

```
%LocalAppData%\Lhamiel\settings.json
```

## JSON構造

```json
{
  "Theme": "System",
  "Locale": "",
  "CompressionFormat": "ZIP",
  "ExtractionOutputDirectory": "C:\\Users\\YourName\\Desktop",
  "CompressionOutputDirectory": "C:\\Users\\YourName\\Desktop",
  "ExtractionOutputToSameDirectory": false,
  "CompressionOutputToSameDirectory": false,
  "OpenExtractionOutputFolder": true,
  "CreateArchiveNameFolder": true,
  "OpenCompressionOutputFolder": true,
  "CompressMultipleAsOne": true,
  "DirectoryStructureMode": "IncludeRoot",
  "IncludeHiddenAndSystemEntries": true,
  "ExcludedFilePatterns": [".DS_Store", "Thumbs.db", "desktop.ini", "__MACOSX"],
  "ZipCompressionLevel": 5,
  "SevenZipCompressionLevel": 5,
  "LogMaxSizeMB": 10,
  "LogRetentionDays": 7,
  "UpdateChannel": "release",
  "Check4UpdatesOnStartup": true,
  "IgnoreUpdateTag": ""
}
```

> **ℹ️ 補足**: `UpdateBaseUrl` は `[JsonIgnore]` 属性付きの読み取り専用プロパティのため `settings.json` には書き出されず、記述しても読み込まれません（悪意ある第三者ホストへの誘導を防ぐための固定値）。

## プロパティ一覧

### 全般設定

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `Theme` | string | `"System"` | テーマ。`"System"`, `"Dark"`, `"Light"` |
| `Locale` | string | `""` | ロケール。空文字はシステム自動検出。例: `"ja_JP"`, `"en_US"` |

### 展開設定

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `ExtractionOutputDirectory` | string | デスクトップ | 展開先ディレクトリ |
| `ExtractionOutputToSameDirectory` | bool | `false` | アーカイブと同じ場所に展開 |
| `OpenExtractionOutputFolder` | bool | `true` | 展開後にフォルダを開く |
| `CreateArchiveNameFolder` | bool | `true` | アーカイブ名でフォルダ作成（二重フォルダ防止含む） |

### 圧縮設定

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `CompressionFormat` | string | `"ZIP"` | 圧縮形式。`"ZIP"`, `"7z"`, `"TAR"` |
| `CompressionOutputDirectory` | string | デスクトップ | 圧縮先ディレクトリ |
| `CompressionOutputToSameDirectory` | bool | `false` | 元ファイルと同じ場所に作成 |
| `OpenCompressionOutputFolder` | bool | `true` | 圧縮後にフォルダを開く |
| `CompressMultipleAsOne` | bool | `true` | 複数ファイルを1つのアーカイブにまとめる |
| `DirectoryStructureMode` | string | `"IncludeRoot"` | ディレクトリ構造モード（下記参照） |
| `IncludeHiddenAndSystemEntries` | bool | `true` | 圧縮時に Hidden/System 属性のファイル・フォルダも列挙対象に含める |
| `ZipCompressionLevel` | int | `5` | ZIP圧縮レベル（0-9） |
| `SevenZipCompressionLevel` | int | `5` | 7z圧縮レベル（0-9） |
| `ExcludedFilePatterns` | string[] | システムファイル | 圧縮時に除外するファイル名・フォルダ名（**glob 非対応、パスセグメント完全一致のみ**。例: `.DS_Store`、`.git`、`__MACOSX`） |

### ログ設定

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `LogMaxSizeMB` | int | `10` | ログファイル 1 つあたりの最大サイズ (MB) |
| `LogRetentionDays` | int | `7` | この日数より古いログファイルは起動時に自動削除 |

### 自動更新設定

`UpdateBaseUrl` は**セキュリティ上ハードコード固定**されており、`settings.json` から書き換えても反映されない（悪意ある第三者ホストへの誘導を防ぐため）。Velopack の `SimpleWebSource` が `{UpdateBaseUrl}/releases.{channel}.json` を取得して更新チェックを行う。

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `UpdateBaseUrl` | string | `"https://lhamiel.1llum1n4t1.com"` | **設定不可（固定）**。Cloudflare R2 上の `lhamiel-updates` バケットへマップされたカスタムドメイン。`[JsonIgnore]` により `settings.json` の読み書き対象外。記述しても反映されない |
| `UpdateChannel` | string | `"release"` | 更新チャンネル（`"release"` / `"prerelease"`）。case-insensitive で受理し、canonical な小文字（`release` / `prerelease`）に正規化される。未知の値は `release` にサイレントフォールバック |
| `Check4UpdatesOnStartup` | bool | `true` | メイン画面起動時に Velopack 自動更新チェックを実行するか。`App.Check4Update(manually:false)` UI 経路の ON/OFF を切り替える。「全般」設定タブの「起動時にアップデートを確認」チェックボックスで変更可能 |
| `IgnoreUpdateTag` | string | `""` | 「このバージョンをスキップ」で記録された Velopack リリースタグ名（例: `"v1.0.166"`）。自動チェックで一致タグの更新は VelopackUpdateDialog で抑止される。手動チェックは無視タグを無視する。`SanitizeAfterLoad` で長さ 256 超 / 制御文字混入時は空文字に正規化される。「バージョン」設定タブの「スキップを取り消す」ボタンでクリア可能 |

## DirectoryStructureMode

圧縮時のディレクトリ構造の扱い方を制御する。

| 値 | 動作 | 例: `MyFolder/sub/file.txt` |
|----|------|---------------------------|
| `IncludeRoot` | ルートディレクトリを含める（デフォルト） | → `MyFolder/sub/file.txt` |
| `ExcludeRoot` | ルートディレクトリを除外 | → `sub/file.txt` |
| `Flat` | 全ディレクトリ構造を除外 | → `file.txt` |

## 設定のリセット

`settings.json` を削除してアプリを再起動するとデフォルト設定が復元される。

## 破損検知時の挙動

> **📝 履歴注記**: この機能は v1.0.160 で実装されたが、同バージョン取り下げ後に v1.0.161 で v1.0.159 状態へロールバックされ、その後再リリースで再導入された経緯がある。

`settings.json` が JSON として解析不能になった場合、Lhamiel は以下の順で処理する：

1. 破損ファイルを `settings.json.corrupt_<YYYYMMDDHHmmss>.bak` に **`File.Move`** で退避（次回起動で同じパースエラーが再発しないようパスから取り除く）
2. Move が失敗した場合（OneDrive 同期中・ウイルス対策ロック中等）は `File.Delete` → 空 JSON `{}` 上書きの順でフォールバック
3. デフォルト設定を使用して起動
4. `Lhamiel_yyyyMMdd.log` に警告ログを出力

また、`ExtractionOutputDirectory` / `CompressionOutputDirectory` が存在しないパスや**システム重大ディレクトリ**（Windows / Program Files / Program Files (x86) / System32 / ドライブルート / プロファイル根 `C:\Users\<user>`）を指している場合は、起動時に自動的にデスクトップへフォールバックする。

> **🛡️ 設計メモ**: `Desktop` / `Documents` / `Downloads` / `Music` / `Pictures` / `Videos` などの一般的なユーザーコンテンツフォルダは出力先として **正当な選択肢** として扱われ、サニタイズで除去されない（`PathValidator.IsSystemCriticalDirectory` がこれらを除外している）。

## 関連ファイル

- `Util/Settings.cs` — 設定クラス
- `Util/SettingsManager.cs` — シングルトン管理
