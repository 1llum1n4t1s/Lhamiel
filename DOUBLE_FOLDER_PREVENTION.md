# 二重フォルダ防止ロジック実装ドキュメント

## 概要

圧縮ファイル解凍時に、**二重フォルダ構造**（A/A/... のようなフォルダ内にフォルダ名が一致するフォルダのみが存在する状態）が生成されるのを防ぐロジック（スマート解凍）を実装しました。

## 変更履歴

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
     - **回避したいケース**：`ProjectA.zip/ProjectA/ProjectA/files/data.txt`が`ProjectA/ProjectA/files/data.txt`と出力
     - **期待する挙動**：`ProjectA.zip/ProjectA/ProjectA/files/data.txt`が`ProjectA/files/data.txt`と出力（リフトアップ適用）
     - **正常な単一フォルダケース**：`ProjectA.zip/ProjectA/data.txt`は`ProjectA/ProjectA/data.txt`と出力（リフトアップなし）

3. **複数フォルダ・ファイル存在時の動作**
   - アーカイブの直下に複数のファイルやフォルダがある場合
   - アーカイブ名フォルダを作成してその中に展開

## 実装詳細

### アーキテクチャ

スマート解凍機能は以下の2つのフェーズで動作します：

1. **判定フェーズ**: アーカイブ内に二重フォルダ構造が存在するかを判定
2. **リフトアップフェーズ**: 判定結果に基づいて展開後の処理を実行

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

**判定ロジック**:
- `outputToSameDirectory`が`true`の場合：アーカイブと同じディレクトリ
- `outputToSameDirectory`が`false`の場合：`defaultOutputDir`を使用
- `defaultOutputDir`が空の場合：アーカイブと同じディレクトリにフォールバック

### 3. スマート解凍の適用判定

**場所**: `Util/ArchiveProcessor.cs` の `ExtractArchiveAsync()`

**処理フロー**:
1. `DetectDuplicateFolderStructure()`で二重フォルダ構造を検出
2. **二重フォルダが検出された場合のみ**：
   - 展開先を基準ディレクトリに設定
   - リフトアップフラグ（`rootItemName`）をセット
3. **二重フォルダが検出されない場合**：
   - 展開先を`基準ディレクトリ/アーカイブ名`に設定
   - リフトアップは実行されない

### 4. リフトアップ処理

**場所**: `Util/ArchiveExtractor.cs` の `ExtractArchive()`メソッド内

**処理**:
- リフトアップフラグが設定されている場合のみ実行
- アーカイブ展開後、内側のフォルダを外側のディレクトリに移動
- 空になった外側のフォルダを削除

## 処理フロー

### 展開処理全体フロー

1. **ファイル検証**
   - アーカイブファイルの存在確認
   - サポートされている形式かを確認

2. **二重フォルダ判定**
   - `DetectDuplicateFolderStructure()`で二重フォルダ構造を検出
   - 基準ディレクトリを`GetBaseOutputDirectory()`で取得

3. **展開先パス決定**
   - 二重フォルダの場合：基準ディレクトリに直接展開（リフトアップ予定）
   - 非二重フォルダの場合：`基準ディレクトリ/アーカイブ名`に展開

4. **上書き確認**
   - 展開先が既に存在する場合、ユーザーに確認

5. **展開実行**
   - `ArchiveExtractor.ExtractArchiveAsync()`で展開

6. **リフトアップ処理**
   - 二重フォルダの場合：内側のフォルダを移動し、外側のフォルダを削除

7. **完了**
   - 展開完了メッセージを表示

## 例

### 例1：リフトアップが適用されるケース（二重フォルダ）

**圧縮ファイル構造**
```
ProjectA.zip
└── ProjectA/
    └── ProjectA/
        ├── src/
        │   └── main.cpp
        ├── CMakeLists.txt
        └── README.md
```

**展開結果**
```
ProjectA/ (展開先フォルダ)
├── src/
│   └── main.cpp
├── CMakeLists.txt
└── README.md
```

**ロジック**
1. `DetectDuplicateFolderStructure()`が`"ProjectA"`を返す（二重フォルダ構造を検出）
2. 基準ディレクトリに直接展開
3. リフトアップ処理により、内側の`ProjectA`フォルダの中身を移動
4. 外側の`ProjectA`フォルダは削除
5. 結果として`ProjectA/ProjectA/...`という二重フォルダが防止される

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
ProjectA/ (展開先フォルダ)
├── src/
│   └── main.cpp
├── CMakeLists.txt
└── README.md
```

**ロジック**
1. `DetectDuplicateFolderStructure()`が`null`を返す（二重フォルダ構造ではない）
2. アーカイブ名フォルダ`ProjectA`を作成して展開
3. 意図的な単一フォルダ構造が保持される
4. ファイル名を`A.zip`に変更しても同じ構造で展開される（ユーザーの意図を尊重）

### 例3：リフトアップが適用されないケース（複数フォルダ）

**圧縮ファイル構造**
```
ProjectB.zip
├── folder1/
│   └── file1.txt
└── folder2/
    └── file2.txt
```

**展開結果**
```
ProjectB/ (展開先フォルダ)
├── folder1/
│   └── file1.txt
└── folder2/
    └── file2.txt
```

**ロジック**
1. `DetectDuplicateFolderStructure()`が`null`を返す（ルート要素が複数）
2. アーカイブ名フォルダ`ProjectB`を作成して展開
3. ファイルが散らばらないように保護

### 例4：リフトアップが適用されないケース（ルート直下に同名ファイル）

**圧縮ファイル構造**
```
A.zip
└── A/ (フォルダ)
    └── A (拡張子なしのファイル)
        └── content.txt
```

**展開結果**
```
A/ (展開先フォルダ)
└── A (ファイル)
    └── content.txt
```

**ロジック**
1. `DetectDuplicateFolderStructure()`が`null`を返す（第2階層の`A`はファイルであり、フォルダではない）
2. アーカイブ名フォルダ`A`を作成して展開
3. フォルダとファイルの区別を厳密化することで、誤判定を防止

### 例5：システムフォルダを無視するケース

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
ProjectD/ (展開先フォルダ)
├── src/
└── README.md
```

**ロジック**
1. `__MACOSX`は`IgnoredSystemDirectories`に含まれるため無視
2. `DetectDuplicateFolderStructure()`が`null`を返す（有効なルート要素は`ProjectD`のみだが、二重フォルダではない）
3. アーカイブ名フォルダ`ProjectD`を作成して展開

## 並列処理の最適化

### 並列展開の最適化

**場所**: `Util/ArchiveProcessor.cs` の `ExtractArchivesAsync()`

**最適化内容**:
- 並列度を`Environment.ProcessorCount / 2`に制限（最小2、最大4）
- ディスクI/O負荷を考慮した保守的な設定
- 個別進捗報告を無効化し、全体進捗のみを報告

### 並列圧縮の最適化

**場所**: `Util/ArchiveProcessor.cs` の `CompressItemsAsync()`

**最適化内容**:
- 並列度を`Environment.ProcessorCount / 2`に制限（最小1、最大4）
- 7-Zip等の圧縮エンジンが内部でマルチスレッドを使用するため、タスク並列数を抑制
- CPU競合を防ぎ、全体的なスループットを向上

### スレッド数制御

**場所**: `Util/ArchiveCompressor.cs` の `CreateArchiveWriter()`

**最適化内容**:
- `maxThreads`パラメータを追加し、圧縮エンジンのスレッド数を制御可能に
- 並列圧縮時は各タスクのスレッド数を制限することで、CPU競合を防止

## パフォーマンス考慮事項

### スマート解凍の判定コスト
- `HasSingleRootItem()`はアーカイブ内の全エントリをスキャンする必要がある
- ただし、2つ目のルート要素が見つかった時点で早期リターンするため、多くの場合は高速
- 大規模アーカイブでも無視できるレベルのオーバーヘッド

### 並列処理のトレードオフ
- 並列度を抑えることで、個々のタスクは遅くなる可能性がある
- しかし、CPU競合やディスクI/O競合を防ぐことで、全体的なスループットは向上
- 特にHDDでは、並列度を抑えることでシーク時間を削減できる

## エラーハンドリング

### スマート解凍のエラー処理
- `HasSingleRootItem()`でエラーが発生した場合は`false`を返す
- 安全側に倒し、通常のアーカイブ名フォルダを作成して展開
- エラーログを出力し、問題の追跡を可能にする

### 並列処理のエラー処理
- 各タスクのエラーは個別にキャッチし、失敗リストに追加
- 一部のタスクが失敗しても、他のタスクは継続実行
- 全タスク完了後に成功/失敗の統計を表示

### キャンセル処理
- `CancellationToken`を使用した適切なキャンセル処理
- キャンセル時は一時ファイルを削除し、リソースをクリーンアップ
