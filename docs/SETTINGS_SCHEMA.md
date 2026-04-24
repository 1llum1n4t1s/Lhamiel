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
  "ExcludedFilePatterns": [".DS_Store", "Thumbs.db", "__MACOSX"],
  "ZipCompressionLevel": 5,
  "SevenZipCompressionLevel": 5,
  "LogMaxSizeMB": 10,
  "LogRetentionDays": 7,
  "UpdateChannel": "release"
}
```

> **ℹ️ 補足**: `UpdateRepoOwner` / `UpdateRepoName` は `[JsonIgnore]` 属性付きの読み取り専用プロパティのため `settings.json` には書き出されず、記述しても読み込まれません（悪意ある第三者リポジトリへの誘導を防ぐための固定値）。

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
| `ZipCompressionLevel` | int | `5` | ZIP圧縮レベル（0-9） |
| `SevenZipCompressionLevel` | int | `5` | 7z圧縮レベル（0-9） |
| `ExcludedFilePatterns` | string[] | システムファイル | 圧縮時に除外するパス/ファイル名（**glob 非対応、パスセグメント完全一致のみ**。例: `.DS_Store`、`__MACOSX`） |

### ログ設定

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `LogMaxSizeMB` | int | `10` | ログファイル 1 つあたりの最大サイズ (MB) |
| `LogRetentionDays` | int | `7` | この日数より古いログファイルは起動時に自動削除 |

### 自動更新設定

`UpdateRepoOwner` と `UpdateRepoName` は**セキュリティ上ハードコード固定**されており、`settings.json` から書き換えても反映されない（悪意ある第三者リポジトリへの誘導を防ぐため）。

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `UpdateRepoOwner` | string | `"1llum1n4t1s"` | **設定不可（固定）**。`[JsonIgnore]` により `settings.json` の読み書き対象外。記述しても反映されない |
| `UpdateRepoName` | string | `"Lhamiel"` | **設定不可（固定）**。`[JsonIgnore]` により `settings.json` の読み書き対象外。記述しても反映されない |
| `UpdateChannel` | string | `"release"` | 更新チャンネル（`"release"` / `"prerelease"`）。v1.0.160 から未知の値を検知した場合は `release` にフォールバックし警告ログを出す |

## DirectoryStructureMode

圧縮時のディレクトリ構造の扱い方を制御する。

| 値 | 動作 | 例: `MyFolder/sub/file.txt` |
|----|------|---------------------------|
| `IncludeRoot` | ルートディレクトリを含める（デフォルト） | → `MyFolder/sub/file.txt` |
| `ExcludeRoot` | ルートディレクトリを除外 | → `sub/file.txt` |
| `Flat` | 全ディレクトリ構造を除外 | → `file.txt` |

## 設定のリセット

`settings.json` を削除してアプリを再起動するとデフォルト設定が復元される。

## 破損検知時の挙動（v1.0.160〜）

`settings.json` が JSON として解析不能になった場合、Lhamiel は以下の順で処理する：

1. 破損ファイルを `settings.json.corrupt_<YYYYMMDDHHmmss>.bak` に退避
2. デフォルト設定を使用して起動
3. `Lhamiel_yyyyMMdd.log` に警告ログを出力

また、`ExtractionOutputDirectory` / `CompressionOutputDirectory` が存在しないパスや保護ディレクトリを指している場合は、起動時に自動的にデスクトップへフォールバックする。

## 関連ファイル

- `Util/Settings.cs` — 設定クラス
- `Util/SettingsManager.cs` — シングルトン管理
