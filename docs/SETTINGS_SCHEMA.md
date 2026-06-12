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
  "RespectNestedGitignore": false,
  "VerifyAfterExtraction": true,
  "NormalizeUnicodeFileNames": true,
  "PropagateMarkOfTheWeb": true,
  "ZipCompressionLevel": 5,
  "SevenZipCompressionLevel": 5,
  "IsPasswordProtectionEnabled": false,
  "PasswordMode": "PromptEachTime",
  "EncryptedCompressionPassword": null,
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
| `VerifyAfterExtraction` | bool | `true` | **[Legacy / no-op]** v1.0.183 以降は参照されない。CRC は展開中に 7z.dll が常時照合し、不一致は展開自体が失敗する（展開後の二度読み再検証パスは廃止）。キーは既存 settings.json 互換のため維持 |
| `NormalizeUnicodeFileNames` | bool | `true` | macOS HFS+ 由来の NFD ファイル名を NFC に正規化 |
| `PropagateMarkOfTheWeb` | bool | `true` | 元アーカイブの Zone.Identifier ADS を展開ファイルに伝播 |

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
| `IsPasswordProtectionEnabled` | bool | `false` | パスワード保護を有効化（ZIP=AES-256 / 7z=AES-256、TAR は非対応で UI ガード）。`v1.0.181+` |
| `PasswordMode` | string | `"PromptEachTime"` | パスワード入力モード。`"PromptEachTime"`（ドロップごとに確認）または `"Remember"`（DPAPI で保存）。`v1.0.181+` |
| `EncryptedCompressionPassword` | byte[]? (Base64) | `null` | DPAPI（CurrentUser scope）で暗号化された圧縮パスワード。`PasswordMode="Remember"` のときのみ書込み。4096 バイト超は破棄。`v1.0.181+` |

> **`EncryptFileNames` は永続化されない**: 7z のヘッダ暗号化（`-mhe=on` 相当）を制御する `EncryptFileNames` は **`[JsonIgnore]` で `settings.json` には保存されない**。`IsPasswordProtectionEnabled` を OFF→ON にする度に既定値 `true` に強制リセットされる仕様（誤って OFF にしたまま放置されるリスクを避けるため）。

> **`EncryptedCompressionPassword` の取り扱い**: DPAPI scope は `CurrentUser`。同じ Windows ユーザー + 同じ PC でのみ復号可能。別 PC への `settings.json` コピーや Windows パスワードリセット後は復号失敗 → UI で「再設定してください」と促す。Settings 側は自動 wipe しない（OneDrive 同期等の一時的失敗でパスワードを失わないため）。`PasswordMode="Remember"` で ciphertext 空の場合は起動時に `"PromptEachTime"` へ自動 degrade される。

> **除外パターンは別ファイル**: 圧縮時の除外パターンは `settings.json` ではなく [`%LocalAppData%\Lhamiel\.lhaignore`](#圧縮時の除外パターン-lhaignore) に保存される。`v1.0.171` 以降、`.gitignore` 互換構文に対応。

### 圧縮時の除外パターン (.lhaignore)

`.lhaignore` は `%LocalAppData%\Lhamiel\.lhaignore` に置かれるテキストファイルで、`.gitignore` と同じ構文で圧縮対象から除外するパターンを記述する。設定タブの「除外設定ファイルを開く」ボタンから既定のテキストエディタで開ける。

主要構文:

| 構文 | 意味 | 例 |
|------|------|----|
| `#` から始まる行 | コメント | `# 不要な build 成果物` |
| `*.log` | 拡張子マッチ（任意の階層） | `debug.log`, `src/sub/info.log` |
| `node_modules/` | 末尾 `/` でディレクトリ限定 | `node_modules/` 配下を枝刈り |
| `/build` | 先頭 `/` でルートにアンカー | ソースルート直下の `build` のみ |
| `**/cache` | `**` で任意階層を表現 | `cache`, `a/cache`, `a/b/cache` |
| `[Tt]humbs.db` | 文字クラス | `Thumbs.db`, `thumbs.db` |
| `!keep.log` | 先頭 `!` で否定（再包含） | 直前で除外された `keep.log` を取り戻す |
| `\#literal` | 先頭 `\` で `#`/`!` をエスケープ | リテラル `#literal` をマッチ |

挙動メモ:
- マッチは大小区別なし（Windows ファイルシステム互換）。
- ディレクトリ除外（`node_modules/` など）は配下を枝刈りするため、`!node_modules/keep.txt` での再包含は機能しない。
- 旧 `settings.json` の `ExcludedFilePatterns` 配列は初回起動時に自動で `.lhaignore` へ移行され、次回 Save で JSON から消える。

### ネストされた `.gitignore` の併用

設定 `RespectNestedGitignore` (デフォルト `false` / オプトイン) を有効にすると、圧縮対象のディレクトリツリー内にある `.gitignore` をスキャン前に発見し、各 `.gitignore` をそのディレクトリのスコープで `.lhaignore` のルールに **追加で** 適用する。

例: `C:\Users\IMT\dev` を圧縮するときに、

```
C:\Users\IMT\dev\
├── .lhaignore                  ← グローバル除外（全 source に適用）
├── repoA\
│   ├── .gitignore              ← repoA 配下にのみ適用
│   └── build\                  ← repoA/.gitignore に "build/" があれば除外
└── repoB\
    └── .gitignore              ← repoB 配下にのみ適用
```

各 `.gitignore` は独立してスコープされるので、`repoA/.gitignore` に書いた `/build` は `repoA/build` だけにマッチし、`repoB/build` には影響しない。

- 探索は `.lhaignore` の枝刈り後のディレクトリのみ対象（`node_modules/` 内の `.gitignore` は読まない）
- 圧縮実行毎に最新の `.gitignore` を読み直すため、編集後の再圧縮で即反映される
- 設定 UI: 「圧縮設定」タブ → 除外設定セクションの「圧縮対象内の .gitignore も併用する」チェックボックス

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `RespectNestedGitignore` | bool | `false` | 圧縮対象のサブディレクトリにある `.gitignore` を自動で除外ルールに追加 |

### ログ設定

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `LogMaxSizeMB` | int | `10` | ログファイル 1 つあたりの最大サイズ (MB) |
| `LogRetentionDays` | int | `7` | この日数より古いログファイルは起動時に自動削除 |

### 自動更新設定

`UpdateBaseUrl` は**セキュリティ上ハードコード固定**されており、`settings.json` から書き換えても反映されない（悪意ある第三者ホストへの誘導を防ぐため）。Velopack の `SimpleWebSource` が `{UpdateBaseUrl}/releases.{channel}.json` を取得して更新チェックを行う。

| プロパティ | 型 | デフォルト | 説明 |
|-----------|------|----------|------|
| `UpdateBaseUrl` | string | `"https://lhamiel.nephilim.jp"` | **設定不可（固定）**。Cloudflare R2 上の `lhamiel-updates` バケットへマップされた中立カスタムドメイン。`[JsonIgnore]` により `settings.json` の読み書き対象外。記述しても反映されない |
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
