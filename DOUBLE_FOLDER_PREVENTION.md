# 二重フォルダ防止ロジック実装ドキュメント

## 概要

圧縮ファイル解凍時に、圧縮ファイルのルート内に「単一のフォルダ」のみが存在する場合に二重フォルダが生成されるのを防ぐロジックを実装しました。

## 要件

1. **指定された圧縮ファイル（.zipなど）を解凍する**
   - 既存の解凍機能を使用

2. **解凍先のルートディレクトリ構成**
   - 基本：圧縮ファイルのファイル名（拡張子なし）をフォルダとして作成し、その中に展開
   - 例：`ProjectA.zip` → `ProjectA/` フォルダを作成

3. **【重要：二重フォルダ防止ロジック】**
   - 圧縮ファイルのルート内に「単一のフォルダ」のみが存在する場合、そのフォルダを作成せず、中身を直接展開先フォルダに配置
   - 例：
     - **回避したいケース**：`ProjectA.zip` 内に `ProjectA/files/data.txt` がある場合、`ProjectA/ProjectA/files/data.txt` となってしまう
     - **期待する挙動**：`ProjectA/files/data.txt` となるようにリフトアップ

4. **複数フォルダ・ファイル存在時の動作**
   - ルートに「複数のファイルやフォルダ」が存在する場合は、通常通りフォルダを作成して解凍

## 実装詳細

### 1. ルートレベルアイテムの判定メソッド：`GetRootLevelItems()`

```csharp
private static List<(string Name, bool IsDirectory)> GetRootLevelItems(List<string> archiveContents)
```

- アーカイブ内のすべてのパスをスキャン
- ルートレベル（`/` や `\` で区切られる前の要素）のアイテムを特定
- ディレクトリかファイルかを判定

**判定ロジック**
- パスをノーマライズ（バックスラッシュをスラッシュに統一）
- 最初の `/` までの要素がルートレベルのアイテム
- `parts.Length > 1` またはパスが `/` で終わる場合はディレクトリ

### 2. フォルダ内のアイテム取得メソッド：`GetItemsInFolder()`

```csharp
private static List<(string Path, bool IsDirectory)> GetItemsInFolder(List<string> archiveContents, string folderName)
```

- 指定されたフォルダ内のすべてのアイテムを取得
- 複数アイテムが存在するか判定するために使用

**判定ロジック**
- フォルダのプレフィックス（`folderName/` または `folderName\`）で始まるパスをフィルタ
- フォルダプレフィックス削除後の相対パスを取得
- 相対パス内のルートレベルアイテムを抽出

### 3. 調整ファイル名取得メソッド：`GetAdjustedFileName()`

```csharp
private static string GetAdjustedFileName(string archivePath, string defaultFileName)
```

- アーカイブ内容に基づいて、適切なファイル名を返す
- ルートに単一フォルダのみがあり、その中に複数アイテムがある場合は **空文字列を返す**
- 空文字列が返された場合、展開先は `baseDirectory` となり、二重フォルダが防止される

### 4. 一時展開パス取得メソッド：`GetTemporaryExtractionPath()`

```csharp
private static string GetTemporaryExtractionPath(string archivePath, string outputPath)
```

- 二重フォルダ防止が必要な場合、一時的な展開パスを返す
- 一時パス：`outputPath_temp_<guid>`

### 5. ファイルリフトアップメソッド：`LiftUpFilesFromTemporaryPath()`

```csharp
private static void LiftUpFilesFromTemporaryPath(string tempPath, string outputPath)
```

- 一時パスから本来の展開先パスにファイルを移動
- 階層を調整（ルートフォルダの1階層を削除）

**処理フロー**
1. 一時パス直下のフォルダを取得（通常は1つ）
2. そのフォルダ内のすべてのファイルを本来のパスに移動
3. すべてのサブフォルダを本来のパスに移動
4. 一時パスを削除

### 6. 拡張メソッド：`RemoveReadOnlyAttributes()`

```csharp
private static void RemoveReadOnlyAttributes(string path)
```

- ファイルまたはディレクトリの読み取り専用属性を削除
- ファイル単体とディレクトリの両方に対応

## 処理フロー

### 展開処理（`ExtractArchive()`）

1. アーカイブファイルの存在確認
2. 展開先ディレクトリの上書き確認（必要に応じて）
3. **一時展開パスの確認**
   - `GetTemporaryExtractionPath()` で二重フォルダ防止の必要性を判定
   - 必要な場合は一時パスに展開、不要な場合は本来のパスに展開
4. アーカイブを展開
5. **ファイルのリフトアップ**（必要に応じて）
   - 一時パスから本来のパスにファイルを移動
   - 一時ディレクトリを削除

### 出力ディレクトリ取得（`GetOutputDirectory()`）

1. ファイル名を取得（拡張子なし）
2. `GetAdjustedFileName()` で調整
3. 調整されたファイル名が空文字列の場合は `baseDirectory` を返す
4. それ以外の場合は `Path.Combine(baseDirectory, adjustedFileName)` を返す

## 例

### 例1：二重フォルダ防止が必要なケース

**圧縮ファイル構造**
```
ProjectA.zip
├── ProjectA/
│   ├── src/
│   │   └── main.cpp
│   ├── CMakeLists.txt
│   └── README.md
```

**期待される展開結果**
```
ProjectA/ (展開先フォルダ)
├── src/
│   └── main.cpp
├── CMakeLists.txt
└── README.md
```

**ロジック**
1. ルートアイテム：`ProjectA` (1つ、ディレクトリ)
2. `ProjectA` 内のアイテム：`src/`, `CMakeLists.txt`, `README.md` (複数)
3. `GetAdjustedFileName()` が空文字列を返す
4. 一時パスに展開後、ファイルをリフトアップ

### 例2：二重フォルダ防止が不要なケース（複数フォルダ）

**圧縮ファイル構造**
```
ProjectB.zip
├── folder1/
│   └── file1.txt
└── folder2/
    └── file2.txt
```

**期待される展開結果**
```
ProjectB/ (展開先フォルダ)
├── folder1/
│   └── file1.txt
└── folder2/
    └── file2.txt
```

**ロジック**
1. ルートアイテム：`folder1/`, `folder2/` (複数)
2. `GetAdjustedFileName()` が `"ProjectB"` を返す
3. 通常通り `ProjectB/` フォルダに展開

### 例3：二重フォルダ防止が不要なケース（複数ファイル）

**圧縮ファイル構造**
```
ProjectC.zip
├── file1.txt
├── file2.txt
└── README.md
```

**期待される展開結果**
```
ProjectC/ (展開先フォルダ)
├── file1.txt
├── file2.txt
└── README.md
```

**ロジック**
1. ルートアイテム：`file1.txt`, `file2.txt`, `README.md` (複数ファイル)
2. `GetAdjustedFileName()` が `"ProjectC"` を返す
3. 通常通り `ProjectC/` フォルダに展開

## テストカバレッジ

ユニットテストが実装されており、以下の機能をカバーしています：

- `IsSupportedArchiveType()` - サポートされるアーカイブ形式の判定
- `GetOutputDirectory()` - 展開先ディレクトリの計算

詳細は `Lhamiel.Tests.Unit/ArchiveExtractorTests.cs` を参照

## パフォーマンス考慮事項

- ルートレベルアイテムの判定は、アーカイブ内のすべてのパスをスキャンする必要があるため、大規模なアーカイブでは若干のオーバーヘッドが発生する可能性があります
- ただし、ほとんどのアーカイブでは無視できるレベルです

## エラーハンドリング

- アーカイブ読み込みエラーは例外をキャッチし、デフォルトのファイル名を返す
- ファイル移動エラーは詳細なログを出力し、例外を再スロー
- キャンセル時は一時ディレクトリを確実に削除
