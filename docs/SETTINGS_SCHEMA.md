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
  "UpdateRepoOwner": "1llum1n4t1s",
  "UpdateRepoName": "Lhamiel",
  "UpdateChannel": "release"
}
```

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
| `ExcludedFilePatterns` | string[] | システムファイル | 圧縮時に除外するパターン |

### 自動更新設定

`UpdateRepoOwner` と `UpdateRepoName` は**セキュリティ上ハードコード固定**されており、`settings.json` から書き換えても反映されない（悪意ある第三者リポジトリへの誘導を防ぐため）。

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `UpdateRepoOwner` | string | `"1llum1n4t1s"` | **設定不可（固定）**。書き換え検知時は警告ログを出してデフォルトへ戻す |
| `UpdateRepoName` | string | `"Lhamiel"` | **設定不可（固定）**。書き換え検知時は警告ログを出してデフォルトへ戻す |
| `UpdateChannel` | string | `"release"` | 更新チャンネル（`"release"` / `"prerelease"`） |

## DirectoryStructureMode

圧縮時のディレクトリ構造の扱い方を制御する。

| 値 | 動作 | 例: `MyFolder/sub/file.txt` |
|----|------|---------------------------|
| `IncludeRoot` | ルートディレクトリを含める（デフォルト） | → `MyFolder/sub/file.txt` |
| `ExcludeRoot` | ルートディレクトリを除外 | → `sub/file.txt` |
| `Flat` | 全ディレクトリ構造を除外 | → `file.txt` |

## 設定のリセット

`settings.json` を削除してアプリを再起動するとデフォルト設定が復元される。

## 関連ファイル

- `Util/Settings.cs` — 設定クラス
- `Util/SettingsManager.cs` — シングルトン管理
