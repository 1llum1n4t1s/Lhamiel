# 二重フォルダ防止ロジック実装ドキュメント

## 概要

圧縮ファイル解凍時に、圧縮ファイルのルート内に「単一のフォルダ」のみが存在する場合に二重フォルダが生成されるのを防ぐロジック（スマート解凍）を実装しました。

## 変更履歴

### 2026-01-17: スマート解凍機能の実装と並列処理の最適化

**新機能**:
- スマート解凍機能の実装（二重フォルダ防止）
- システムフォルダ（`__MACOSX`など）を無視する機能

**最適化**:
- 並列展開/圧縮時のスレッド制御を改善
- LHA圧縮時の不要な一時コピーを削減
- 進捗報告の頻度を最適化

**変更ファイル**:
- `Util/ArchiveExtractor.cs`: `HasSingleRootItem()`, `GetBaseOutputDirectory()`を追加
- `Util/ArchiveProcessor.cs`: `ExtractArchiveAsync()`でスマート解凍を適用、並列度を最適化
- `Util/ArchiveCompressor.cs`: `CreateArchiveWriter()`にスレッド数制御を追加、LHA圧縮を最適化

## 要件

1. **指定された圧縮ファイル（.zipなど）を解凍する**
   - 既存の解凍機能を使用

2. **単一フォルダ・ファイル存在時の動作**
   - 例：
     - **回避したいケース**：`ProjectA.zip/ProjectA/files/data.txt`が`ProjectA/ProjectA/files/data.txt`と出力
     - **期待する挙動**：`ProjectA.zip/ProjectA/files/data.txt`が`ProjectA/files/data.txt`と出力
     - **期待する挙動2**：`ProjectA.zip/data.txt`が`data.txt`と出力

3. **複数フォルダ・ファイル存在時の動作**
   - `ProjectA.zip`の直下に複数のファイルがある場合
   - アーカイブ名の`ProjectA`を使用してフォルダを作成してその中に展開

## 実装詳細

### アーキテクチャ

スマート解凍機能は以下の2つのフェーズで動作します：

1. **判定フェーズ**: アーカイブのルート要素が単一かどうかを判定
2. **展開フェーズ**: 判定結果に基づいて適切な展開先パスを決定

### 1. ルート要素判定メソッド：`HasSingleRootItem()`

**場所**: `Util/ArchiveExtractor.cs`

```csharp
public static bool HasSingleRootItem(string archivePath)
```

**機能**:
- アーカイブのルート直下に単一の要素（フォルダまたはファイル）のみが存在するかを判定
- システム管理用フォルダ（`__MACOSX`など）は無視

**判定ロジック**:
1. `ArchiveReader`でアーカイブを開く
2. 各エントリのパスを正規化（バックスラッシュをスラッシュに統一）
3. パスを`/`で分割し、最初の要素（ルート要素）を取得
4. システム管理用フォルダ（`IgnoredSystemDirectories`）は除外
5. ルート要素が1つだけの場合は`true`、2つ以上の場合は`false`を返す

**エラーハンドリング**:
- アーカイブ読み込みエラー時は`false`を返す（安全側に倒す）

### 2. 基準出力ディレクトリ取得メソッド：`GetBaseOutputDirectory()`

**場所**: `Util/ArchiveExtractor.cs`

```csharp
public static string GetBaseOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
```

**機能**:
- アーカイブ名フォルダを含まない、基準となる出力ディレクトリを取得
- スマート解凍時に、この基準ディレクトリに直接展開する

**判定ロジック**:
- `outputToSameDirectory`が`true`の場合：アーカイブと同じディレクトリ
- `outputToSameDirectory`が`false`の場合：`defaultOutputDir`を使用
- `defaultOutputDir`が空の場合：アーカイブと同じディレクトリにフォールバック

### 3. スマート解凍の適用判定

**場所**: `Util/ArchiveProcessor.cs` の `ExtractArchiveAsync()`

**処理フロー**:
1. `GetBaseOutputDirectory()`で基準ディレクトリを取得
2. `HasSingleRootItem()`でルート要素が単一かを判定
3. **単一の場合**:
   - 展開先を基準ディレクトリに設定
   - アーカイブ名フォルダを作成せず、中身の要素名でフォルダ/ファイルが作成される
4. **複数の場合**:
   - 展開先を`基準ディレクトリ/アーカイブ名`に設定
   - 従来通りアーカイブ名フォルダを作成して展開

## 処理フロー

### 展開処理全体フロー

1. **ファイル検証**
   - アーカイブファイルの存在確認
   - サポートされている形式かを確認

2. **スマート解凍判定**
   - `HasSingleRootItem()`でルート要素が単一かを判定
   - 基準ディレクトリを`GetBaseOutputDirectory()`で取得

3. **展開先パス決定**
   - 単一ルート要素の場合：基準ディレクトリに直接展開
   - 複数ルート要素の場合：`基準ディレクトリ/アーカイブ名`に展開

4. **上書き確認**
   - 展開先が既に存在する場合、ユーザーに確認

5. **展開実行**
   - `ArchiveExtractor.ExtractArchiveAsync()`で展開
   - 進捗報告を行いながら処理

6. **完了**
   - 展開完了メッセージを表示

## 例

### 例1：スマート解凍が適用されるケース

**圧縮ファイル構造**
```
ProjectA.zip
├── ProjectA/
│   ├── src/
│   │   └── main.cpp
│   ├── CMakeLists.txt
│   └── README.md
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
1. `HasSingleRootItem()`が`true`を返す（ルート要素は`ProjectA`のみ）
2. 基準ディレクトリに直接展開
3. 結果として`ProjectA/ProjectA/...`という二重フォルダが防止される

### 例2：スマート解凍が適用されないケース（複数フォルダ）

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
1. `HasSingleRootItem()`が`false`を返す（ルート要素は`folder1`と`folder2`の2つ）
2. アーカイブ名フォルダ`ProjectB`を作成して展開
3. ファイルが散らばらないように保護

### 例3：スマート解凍が適用されないケース（複数ファイル）

**圧縮ファイル構造**
```
ProjectC.zip
├── file1.txt
├── file2.txt
└── README.md
```

**展開結果**
```
ProjectC/ (展開先フォルダ)
├── file1.txt
├── file2.txt
└── README.md
```

**ロジック**
1. `HasSingleRootItem()`が`false`を返す（ルート要素は3つのファイル）
2. アーカイブ名フォルダ`ProjectC`を作成して展開
3. ファイルが散らばらないように保護

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
ProjectD/ (展開先フォルダ)
├── src/
└── README.md
```

**ロジック**
1. `__MACOSX`は`IgnoredSystemDirectories`に含まれるため無視
2. `HasSingleRootItem()`が`true`を返す（実質的なルート要素は`ProjectD`のみ）
3. スマート解凍が適用される

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

### LHA圧縮の最適化

**場所**: `Util/ArchiveCompressor.cs` の `CompressFiles()`

**最適化内容**:
- 単一ディレクトリかつ除外ファイルがない場合、一時コピーを回避
- `LHAWriter.WriteLHAFile()`に直接ディレクトリパスを渡すことで、ディスクI/Oを削減

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
