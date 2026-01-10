# Lhamiel Settings Schema Documentation

## 概要

Lhamielの設定は `settings.json` ファイルに JSON 形式で保存されます。このファイルはアプリケーションの初回起動時に自動的に作成され、ユーザーの設定変更に応じて更新されます。

## ファイルの場所

```
<アプリケーションディレクトリ>/settings.json
```

## スキーマ

### JSON構造

```json
{
  "compressionFormat": "zip",
  "extractionOutputDirectory": "C:\\Users\\YourName\\Desktop",
  "compressionOutputDirectory": "C:\\Users\\YourName\\Desktop",
  "extractionOutputToSameDirectory": false,
  "compressionOutputToSameDirectory": false,
  "enableShortcutCreation": true,
  "updateRepoOwner": "1llum1n4t1s",
  "updateRepoName": "Lhamiel",
  "updateChannel": "release"
}
```

## プロパティ詳細

### compressionFormat

**型**: `string`
**デフォルト値**: `"zip"`
**説明**: 圧縮時に使用する形式

**サポートされている値**:
- `"7z"` - 7-Zip形式（高圧縮率）
- `"xz"` - XZ形式
- `"bz2"` - BZip2形式
- `"gz"` - GZip形式
- `"tar"` - TAR形式
- `"zip"` - ZIP形式（デフォルト、互換性高い）
- `"wim"` - Windows Imaging形式
- `"cab"` - Cabinet形式

**例**:
```json
"compressionFormat": "7z"
```

---

### extractionOutputDirectory

**型**: `string`
**デフォルト値**: `Environment.GetFolderPath(Environment.SpecialFolder.Desktop)`（デスクトップ）
**説明**: アーカイブ展開時のデフォルト出力先ディレクトリ

**制約**:
- 有効な絶対パスである必要があります
- ディレクトリが存在している必要があります
- 書き込み権限が必要です

**例**:
```json
"extractionOutputDirectory": "D:\\Archives\\Extracted"
```

---

### compressionOutputDirectory

**型**: `string`
**デフォルト値**: `Environment.GetFolderPath(Environment.SpecialFolder.Desktop)`（デスクトップ）
**説明**: フォルダ圧縮時のデフォルト出力先ディレクトリ

**制約**:
- 有効な絶対パスである必要があります
- ディレクトリが存在している必要があります
- 書き込み権限が必要です

**例**:
```json
"compressionOutputDirectory": "D:\\Archives\\Compressed"
```

---

### extractionOutputToSameDirectory

**型**: `boolean`
**デフォルト値**: `false`
**説明**: アーカイブと同じディレクトリに展開するかどうか

**動作**:
- `true`: アーカイブファイルと同じディレクトリに展開
- `false`: `extractionOutputDirectory` で指定されたディレクトリに展開

**例**:
```json
"extractionOutputToSameDirectory": true
```

---

### compressionOutputToSameDirectory

**型**: `boolean`
**デフォルト値**: `false`
**説明**: 圧縮元フォルダと同じディレクトリにアーカイブを作成するかどうか

**動作**:
- `true`: 圧縮元フォルダと同じディレクトリにアーカイブを作成
- `false`: `compressionOutputDirectory` で指定されたディレクトリにアーカイブを作成

**例**:
```json
"compressionOutputToSameDirectory": true
```

---

### enableShortcutCreation

**型**: `boolean`
**デフォルト値**: `true`
**説明**: アプリケーション起動時にデスクトップショートカットを自動作成するかどうか

**動作**:
- `true`: 初回起動時にデスクトップにショートカットを作成
- `false`: ショートカットを作成しない

**例**:
```json
"enableShortcutCreation": false
```

---

### updateRepoOwner

**型**: `string`
**デフォルト値**: `"1llum1n4t1s"`
**説明**: 自動更新用のGitHubリポジトリオーナー名

**用途**: Velopackによる自動更新で使用されます

**例**:
```json
"updateRepoOwner": "your-github-username"
```

---

### updateRepoName

**型**: `string`
**デフォルト値**: `"Lhamiel"`
**説明**: 自動更新用のGitHubリポジトリ名

**用途**: Velopackによる自動更新で使用されます

**例**:
```json
"updateRepoName": "your-repo-name"
```

---

### updateChannel

**型**: `string`
**デフォルト値**: `"release"`
**説明**: 自動更新のチャンネル

**サポートされている値**:
- `"release"` - 安定版リリース（推奨）
- `"beta"` - ベータ版
- `"alpha"` - アルファ版

**例**:
```json
"updateChannel": "beta"
```

---

## 設定の検証

### IsValid() メソッド

アプリケーションは以下の条件で設定の妥当性を検証します：

1. `compressionFormat` がサポートされている形式であること
2. `extractionOutputDirectory` が存在すること
3. `compressionOutputDirectory` が存在すること

検証に失敗した場合、アプリケーションは警告を表示する場合があります。

## 設定のリセット

設定をデフォルト値にリセットするには：

1. アプリケーションを終了
2. `settings.json` ファイルを削除
3. アプリケーションを再起動（自動的にデフォルト設定が作成されます）

または、MainWindowの設定メニューから「設定をリセット」を選択してください。

## トラブルシューティング

### 設定ファイルが読み込めない

**症状**: 設定が保存されない、または読み込まれない

**解決方法**:
1. `settings.json` ファイルの権限を確認
2. JSON形式が正しいか確認（JSONバリデーターを使用）
3. ファイルを削除して再作成を試みる

### 不正な設定値

**症状**: アプリケーションがエラーを表示する

**解決方法**:
1. ログファイル（`Lhamiel.log`）を確認
2. このドキュメントを参照して正しい値を設定
3. 問題が解決しない場合は設定をリセット

## 例: カスタム設定

### 例1: 7z形式で圧縮、同じディレクトリに展開

```json
{
  "compressionFormat": "7z",
  "extractionOutputDirectory": "C:\\Users\\YourName\\Desktop",
  "compressionOutputDirectory": "C:\\Users\\YourName\\Desktop",
  "extractionOutputToSameDirectory": true,
  "compressionOutputToSameDirectory": false,
  "enableShortcutCreation": true,
  "updateRepoOwner": "1llum1n4t1s",
  "updateRepoName": "Lhamiel",
  "updateChannel": "release"
}
```

### 例2: ベータ版自動更新、ショートカット無効

```json
{
  "compressionFormat": "zip",
  "extractionOutputDirectory": "D:\\Downloads",
  "compressionOutputDirectory": "D:\\Archives",
  "extractionOutputToSameDirectory": false,
  "compressionOutputToSameDirectory": false,
  "enableShortcutCreation": false,
  "updateRepoOwner": "1llum1n4t1s",
  "updateRepoName": "Lhamiel",
  "updateChannel": "beta"
}
```

## プログラムによるアクセス

開発者向け情報:

```csharp
// 設定の読み込み
var settings = Settings.Load();

// 設定の変更
settings.CompressionFormat = "7z";
settings.EnableShortcutCreation = false;

// 設定の保存
settings.Save();

// 設定の妥当性確認
if (settings.IsValid())
{
    // 設定は有効
}

// デフォルト値にリセット
settings.ResetToDefaults();
settings.Save();
```

## 関連ファイル

- `Util/Settings.cs` - 設定管理クラスの実装
- `ARCHITECTURE.md` - アーキテクチャ全体のドキュメント
