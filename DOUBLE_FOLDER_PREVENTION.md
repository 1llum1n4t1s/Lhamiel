# 二重フォルダ防止ロジック実装ドキュメント

## 概要

圧縮ファイル解凍時に、**二重フォルダ構造**（A/A/... のようなフォルダ内にフォルダ名が一致するフォルダのみが存在する状態）が生成されるのを防ぐロジック（スマート解凍）を実装しました。

## 変更履歴

### 2026-02-01: 展開仕様の統一と出力パス設計の改善

**修正内容**:
- `outputPath` と `baseDirectory` の2つの概念を統一
- アーカイブ名フォルダを作成しない仕様に変更
- すべての展開で `baseDirectory` に直接展開
- 一時展開フォルダの内容を `baseDirectory` に移動する方式に変更
- リフトアップ処理で一時ディレクトリを経由する安全な実装に変更

**変更ファイル**:
- `Util/ArchiveProcessor.cs`: `outputPath = baseDirectory` に統一
- `Util/ArchiveExtractor.cs`: 展開先への移動処理を改善、リフトアップ処理を修正
- `App.xaml.cs`: `OpenExtractedFolder` で `GetBaseOutputDirectory` を使用

**詳細な説明**:
このドキュメント下部の「処理フロー」セクションを参照

### 2026-02-01: 二重フォルダ判定ロジックの精密化

**修正内容**:
- 単一ルートフォルダのスマート解凍を廃止
- **二重フォルダ構造のみをリフトアップ対象に限定**
- ファイルとフォルダの区別を厳密化

**変更ファイル**:
- `Util/ArchiveExtractor.cs`: `DetectDuplicateFolderStructure()`メソッドを新規追加、リフトアップ処理を復元
- `Util/ArchiveProcessor.cs`: 二重フォルダ検出ロジックに変更

### 2026-01-17: スマート解凍機能の実装と並列処理の最適化

**新機能**:
- スマート解凍機能の実装（二重フォルダ防止）
- システムフォルダ（`__MACOSX`など）を無視する機能

**最適化**:
- 並列展開/圧縮時のスレッド制御を改善
- 進捗報告の頻度を最適化

**変更ファイル**:
- `Util/ArchiveExtractor.cs`: `HasSingleRootItem()`, `GetBaseOutputDirectory()`を追加
- `Util/ArchiveProcessor.cs`: `ExtractArchiveAsync()`でスマート解凍を適用、並列度を最適化
- `Util/ArchiveCompressor.cs`: `CreateArchiveWriter()`にスレッド数制御を追加

## 要件

1. **指定された圧縮ファイル（.zipなど）を解凍する**
   - 既存の解凍機能を使用

2. **二重フォルダ構造の防止**
   - 例：
     - **リフトアップが適用される**：`abcdefg.zip/temp/A/A/content` が `baseDirectory/temp/A/A/content` と出力
     - その他のケースではリフトアップなし

3. **出力パス設計**
   - ルート要素が1つ（単一フォルダ）：`baseDirectory/フォルダ内容`
   - ルート要素が複数：`baseDirectory/アーカイブ名/複数の内容`
   - 二重フォルダ：`baseDirectory/外側フォルダ内容` （リフトアップ後）

## 実装詳細

### アーキテクチャ

スマート解凍機能は以下の3つのフェーズで動作します：

1. **判定フェーズ**: アーカイブ内に二重フォルダ構造が存在するかを判定
2. **展開フェーズ**: 一時ディレクトリに展開
3. **リフトアップフェーズ**: 二重フォルダの場合は内容を移動し、外側のフォルダを削除

### 1. 二重フォルダ検出メソッド：`DetectDuplicateFolderStructure()`

**場所**: `Util/ArchiveExtractor.cs`

```csharp
public static string? DetectDuplicateFolderStructure(string archivePath)
```

**機能**:
- アーカイブ内に二重フォルダ構造が存在するかを判定
- **二重フォルダ**：A階層にフォルダが1つだけあり、その中に同名のフォルダが1つだけ存在する状態

**判定ロジック**:
1. `ArchiveReader`でアーカイブを開く
2. 各エントリのパスを正規化（バックスラッシュをスラッシュに統一）
3. ルート階層（A階層）のフォルダを抽出（**フォルダのみを対象**）
4. A階層が1つのフォルダの場合、第2階層（B階層）を確認
5. B階層に同名のフォルダが1つだけ存在する場合のみ、二重フォルダと判定
6. システム管理用フォルダ（`__MACOSX`など）は除外
7. **ファイルとフォルダを厳密に区別**（`item.IsDirectory`で判定）

**戻り値**:
- 二重フォルダの場合：内側のフォルダ名
- それ以外：`null`

**エラーハンドリング**:
- アーカイブ読み込みエラー時は`null`を返す（安全側に倒す）

### 2. 基準出力ディレクトリ取得メソッド：`GetBaseOutputDirectory()`

**場所**: `Util/ArchiveExtractor.cs`

```csharp
public static string GetBaseOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
```

**機能**:
- アーカイブ名フォルダを含まない、基準となる出力ディレクトリを取得
- **このメソッドでアーカイブ名は使用されない**

**判定ロジック**:
- `outputToSameDirectory`が`true`の場合：アーカイブと同じディレクトリ
- `outputToSameDirectory`が`false`の場合：`defaultOutputDir`を使用
- `defaultOutputDir`が空の場合：アーカイブと同じディレクトリにフォールバック

### 3. ルート要素判定メソッド：`HasSingleRootItem()`

**場所**: `Util/ArchiveExtractor.cs`

```csharp
public static bool HasSingleRootItem(string archivePath)
```

**機能**:
- アーカイブのルートレベルに単一のアイテム（フォルダまたはファイル）しかないかを判定

**戻り値**:
- ルートレベルに単一アイテムのみ：`true`
- ルートレベルに複数アイテム：`false`

### 4. スマート解凍の適用判定

**場所**: `Util/ArchiveProcessor.cs` の `ExtractArchiveAsync()`

**処理フロー**:
1. `GetBaseOutputDirectory()`で基準ディレクトリを取得
2. `DetectDuplicateFolderStructure()`で二重フォルダ構造を検出
3. `HasSingleRootItem()`でルート要素の個数を判定
4. 出力先を決定：
   - 二重フォルダが検出された場合：`baseDirectory` に直接展開（リフトアップ予定）
   - ルート要素が1つだけの場合：`baseDirectory` に直接展開
   - ルート要素が複数の場合：`baseDirectory/アーカイブ名` に展開

### 4. リフトアップ処理

**場所**: `Util/ArchiveExtractor.cs` の `ExtractArchive()`メソッド内

**処理**:
- リフトアップフラグが設定されている場合のみ実行
- 一時ディレクトリを経由して安全に内容を移動
  1. `tempLiftUpPath` という作業用一時ディレクトリを作成
  2. 内側フォルダ（`tempOutputPath/rootItemName/rootItemName`）の中身を `tempLiftUpPath` に移動
  3. 内側フォルダと外側フォルダを削除
  4. `tempLiftUpPath` の中身を `tempOutputPath` に移動
  5. `tempLiftUpPath` をクリーンアップ

## 処理フロー

### 展開処理全体フロー

```
1. ファイル検証
   ↓
2. 基準ディレクトリ決定
   (GetBaseOutputDirectory)
   ↓
3. 二重フォルダ構造判定
   (DetectDuplicateFolderStructure)
   ↓
4. ルート要素個数判定
   (HasSingleRootItem)
   ↓
5. 出力先パス決定
   ├ 二重フォルダ検出？ → outputPath = baseDirectory
   ├ ルート要素が1つ？ → outputPath = baseDirectory
   └ ルート要素が複数？ → outputPath = baseDirectory/アーカイブ名
   ↓
6. 上書き確認（既存の outputPath が存在する場合）
   ↓
7. 一時ディレクトリへ展開
   (tempOutputPath/)
   ↓
8. リフトアップ処理（二重フォルダの場合のみ）
   - tempOutputPath/rootItemName/rootItemName の中身を
     一時作業ディレクトリ経由で tempOutputPath に移動
   - 空になったフォルダを削除
   ↓
9. tempOutputPath の中身を outputPath に移動
   ↓
10. 完了
```

### 展開先への移動処理の詳細

**従来の方式（問題あり）**:
```
tempOutputPath 全体を outputPath に移動
→ resultingPath = baseDirectory/tempOutputPathName/...
```

**新方式（修正後）**:
```
tempOutputPath 直下のディレクトリ・ファイルを
outputPath に移動
→ resultingPath = outputPath/... (outputPathは既に決定済み)
```

**メリット**:
- 複数ルートの場合、自動的にアーカイブ名フォルダが作成される
- パスが直感的で予測可能

## 例

### 例1：リフトアップが適用されるケース（二重フォルダ）

**圧縮ファイル構造と出力先指定**
```
c:\Downloads\abcdefg.zip
出力先: c:\Extracted
  └── temp/A/A/content（アーカイブ内の構造）
```

**展開処理の流れ**
1. `GetBaseOutputDirectory("c:\Downloads\abcdefg.zip", "c:\Extracted", false)` → `"c:\Extracted"`
2. `DetectDuplicateFolderStructure("c:\Downloads\abcdefg.zip")` → `"A"` (二重フォルダを検出)
3. `outputPath = "c:\Extracted"` に統一
4. 一時展開: `c:\Temp\Lhamiel_Extract_XXXX\temp\A\A\content`
5. リフトアップ処理実行: 内側フォルダを移動
6. 結果: `c:\Extracted\temp\A\A\content`

**重要ポイント**:
- `abcdefg` というアーカイブ名は一度も処理に使用されていない
- アーカイブ内の `temp/A/A/content` という構造がそのまま出力先に反映

### 例2：リフトアップが適用されないケース（単一フォルダ）

**圧縮ファイル構造**
```
ProjectA.zip
└── ProjectA/
    ├── src/
    │   └── main.cpp
    ├── CMakeLists.txt
    └── README.md
```

**展開結果**
```
出力先: c:\Extracted\
├── ProjectA/
│   ├── src/
│   │   └── main.cpp
│   ├── CMakeLists.txt
│   └── README.md
```

**ロジック**
1. `DetectDuplicateFolderStructure()` が `null` を返す
2. 一時展開: `c:\Temp\Lhamiel_Extract_XXXX\ProjectA\src\...`
3. リフトアップなし
4. 結果: `c:\Extracted\ProjectA\src\...`

### 例3：複数フォルダのケース

**圧縮ファイル構造と出力先指定**
```
c:\Downloads\ProjectB.zip
出力先: c:\Extracted
```

**アーカイブ内の構造**
```
ProjectB.zip
├── folder1/
│   └── file1.txt
└── folder2/
    └── file2.txt
```

**展開処理の流れ**
1. `GetBaseOutputDirectory()` → `"c:\Extracted"`
2. `DetectDuplicateFolderStructure()` → `null` (二重フォルダではない)
3. `HasSingleRootItem()` → `false` (複数アイテム)
4. `outputPath = "c:\Extracted\ProjectB"` に設定（アーカイブ名を使用）
5. 一時展開: `c:\Temp\Lhamiel_Extract_XXXX\folder1\file1.txt`, `folder2\file2.txt`
6. 一時ディレクトリの内容を `c:\Extracted\ProjectB` に移動
7. 結果: `c:\Extracted\ProjectB\folder1\file1.txt`, `c:\Extracted\ProjectB\folder2\file2.txt`

**重要ポイント**:
- ルート要素が複数の場合、**アーカイブ名「ProjectB」が使用される**
- ファイルが散らばることを防止

### 例4：システムフォルダを無視するケース

**圧縮ファイル構造**
```
ProjectD.zip
├── __MACOSX/
│   └── (システムファイル)
└── ProjectD/
    ├── src/
    └── README.md
```

**展開結果**
```
出力先: c:\Extracted\
├── ProjectD/
│   ├── src/
│   └── README.md
```

**ロジック**
1. `__MACOSX` は無視される
2. `DetectDuplicateFolderStructure()` が `null` を返す
3. リフトアップなし
4. 結果: `c:\Extracted\ProjectD\src\...`

## 重要な設計ポイント

### アーカイブ名を使用するタイミング

1. **複数ルート要素の場合のみ**：ファイルが散らばることを防ぐため
2. **単一ルート要素の場合**：アーカイブ名を使用しない（アーカイブ内の構造を優先）
3. **二重フォルダの場合**：アーカイブ名を使用しない（リフトアップで解消）

### なぜ複数ルート要素の場合はアーカイブ名を使うのか

複数のファイルやフォルダがアーカイブのルートレベルにある場合、それらを `baseDirectory` に直接展開するとファイルが散らばってしまいます：

**問題例**：
```
ProjectB.zip に folder1/, folder2/ が含まれる場合
もし baseDirectory に直接展開すると：
c:\Extracted\folder1\  ← ProjectBに属しているのに別のアーカイブと混在する可能性
c:\Extracted\folder2\
```

**解決方法**：
```
アーカイブ名でまとめるフォルダを作成：
c:\Extracted\ProjectB\folder1\
c:\Extracted\ProjectB\folder2\
```

### 一時ディレクトリを経由する理由

1. **安全性**：展開中にエラーが発生した場合、一時ファイルのみが削除される
2. **アトミック性**：展開とリフトアップが分離されているため、中断可能
3. **リカバリ**：一時ディレクトリから復旧できる可能性がある

### リフトアップ処理で作業用一時ディレクトリを使用する理由

1. **ディレクトリ削除の安全性**：親ディレクトリを削除せずに済む
2. **スレッドセーフ**：複数のリフトアップ処理が干渉しない
3. **エラーハンドリング**：移動失敗時のロールバックが容易

## エラーハンドリング

### 二重フォルダ判定時のエラー
- アーカイブ読み込みエラー時は `null` を返す（安全側に倒す）
- エラーログを出力し、問題の追跡を可能にする

### 移動・削除時のエラー
- ファイル移動失敗時は例外をスロー
- 一時ディレクトリは finally ブロックでクリーンアップ
- 属性の問題で削除失敗する場合、読み取り専用属性を解除してリトライ

### キャンセル処理
- `CancellationToken` を使用した適切なキャンセル処理
- キャンセル時は一時ファイルを削除し、リソースをクリーンアップ

