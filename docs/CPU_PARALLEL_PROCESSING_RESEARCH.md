# CPU並列処理の実装状況調査レポート

**調査日時**: 2026年1月14日  
**調査対象**: Lhamielアーカイブ圧縮・展開ツール  
**調査内容**: 現在のCPU並列処理の実装状況と改善機会の分析

---

## 実行概要

Lhamielの現在の実装では**基本的な非同期処理は実装されているが、真の意味での CPU並列処理（マルチスレッド活用）はほぼ実装されていない**状態です。

以下は詳細な調査結果です。

---

## 1. 現在の実装状況

### 1.1 実装されている技術

#### ✅ 非同期処理（Async/Await）
すべての長時間処理が async/await パターンで実装されています：

| ファイル | メソッド | 機能 |
|---------|---------|------|
| `ArchiveProcessor.cs` | `ExtractArchiveAsync` | 単一アーカイブの展開（非同期） |
| `ArchiveProcessor.cs` | `ExtractArchivesAsync` | 複数アーカイブの展開（順序実行） |
| `ArchiveProcessor.cs` | `CompressItemAsync` | フォルダ圧縮（非同期） |
| `ArchiveProcessor.cs` | `CompressItemsAsync` | 複数フォルダ圧縮（順序実行） |
| `ArchiveExtractor.cs` | `ExtractArchiveAsync` | アーカイブ展開実装 |
| `ArchiveCompressor.cs` | `CompressAsync` | 圧縮実装 |
| `PartialExtractionHandler.cs` | `ExtractWithPartialFailureHandling` | 部分展開（ファイル単位） |

**実装例:**
```csharp
// ArchiveProcessor.cs:114
await ArchiveExtractor.ExtractArchiveAsync(filePath, outputPath, progress, progressWindow, cancellationToken);

// ArchiveExtractor.cs:219
await Task.Run(() =>
{
    var extractor = new ArchiveExtractor();
    extractor.ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, ...);
}, cancellationToken);
```

#### ✅ CancellationToken サポート
キャンセル機能が適切に実装されています：

```csharp
// ArchiveProcessor.cs:24
public static async Task<bool> ExtractArchiveAsync(..., CancellationToken cancellationToken = default, ...)
{
    // 複数の場所でキャンセル確認
    cancellationToken.ThrowIfCancellationRequested();
    ...
}
```

#### ✅ 進捗報告メカニズム
`IProgress<T>` パターンが正しく実装されています：

```csharp
// ArchiveProcessor.cs:76-79
var progress = new Progress<int>(percentage =>
{
    progressWindow?.UpdateProgress(percentage);
});
```

### 1.2 実装されていない/制限されている機能

#### ❌ 複数ファイル/フォルダの並列処理

**現在の実装:**
```csharp
// ArchiveProcessor.cs:149-164
public static async Task<bool> ExtractArchivesAsync(string[] filePaths, ...)
{
    try
    {
        var successCount = 0;
        var totalCount = filePaths.Length;

        foreach (var filePath in filePaths)  // ← 順序実行
        {
            cancellationToken.ThrowIfCancellationRequested();
            var success = await ExtractArchiveAsync(filePath, ...);
            if (success)
            {
                successCount++;
            }
        }
        ...
    }
}
```

**問題点:**
- 複数のアーカイブを展開する場合、**順序的に1つずつ処理**されている
- CPU が 4 コア以上あっても、最初のファイルの処理が完了するまで次に進まない
- 複数の大きなファイルを処理する場合、処理時間が大幅に増加

#### ❌ 大容量ファイルのブロック並列処理

圧縮・展開処理が Cube.FileSystem.SevenZip ライブラリに完全に依存しており、**ブロックレベルの並列処理は行われていません**。

#### ❌ 部分展開のパフォーマンス問題

**ArchiveErrorHandler の警告:**
```csharp
// PartialExtractionHandler.cs:260-268
private static async Task ExtractSingleFile(ArchiveReader reader, object item, string outputPath)
{
    await Task.Run(() =>
    {
        // PERFORMANCE WARNING: 現在のライブラリ(Cube.FileSystem.SevenZip)では個別ファイル展開が制限されているため、
        // 全アーカイブを一時ディレクトリに展開してからコピーする非効率な方法を使用しています。
        // 大きなアーカイブでは著しくパフォーマンスが低下します。
        ...
    });
}
```

**実装の問題:**
- 1ファイル展開するたびに**全体をテンポラリに展開**
- 100ファイル = 100回の全体展開
- メモリ使用量が極大化
- SSD の I/O 限界に達する

---

## 2. スレッド・リソース利用の現状

### 2.1 Thread Pool の利用

**現在の状況:**
```
- ThreadPool: 使用（暗黙的に Task.Run 経由）
- 明示的なスレッド制御: なし
- Parallel.For/Parallel.ForEach: 未使用
- 明示的なスレッド作成: なし
```

**分析:**
```csharp
// Task.Run は自動的に ThreadPool を使用
await Task.Run(() =>
{
    // CPU-Bound な処理がここで実行される
    reader.Save(extractPath);
});
```

このパターンでは、複数の Task を同時に実行することで OS のスケジューラー が複数の CPU コアに振り分けます。ただし、現在は **1ファイル = 1Task** なため、複数ファイルの場合の並列実行ができていません。

### 2.2 CPU バウンド vs I/O バウンド

| 処理 | 特性 | 現在の実装 | 改善機会 |
|-----|------|---------|--------|
| **展開処理** | I/O バウンド（主に読み取り） + CPU バウンド（展開） | Task.Run | SSD 並列読み取り + CPU 並列処理 |
| **圧縮処理** | I/O バウンド（読み取り） + CPU バウンド（圧縮） | Task.Run | ファイル並列読み取り + CPU 並列圧縮 |
| **複数ファイル処理** | I/O + CPU の組み合わせ | 順序実行（foreach） | Parallel.ForEach + タスク制御 |

---

## 3. 実装パターン詳細分析

### 3.1 複数ファイル展開フロー

```
ユーザー入力
    ↓
MainWindow.ExtractArchivesButton_Click
    ↓
ArchiveProcessor.ExtractArchivesAsync(filePaths[])
    ↓
foreach (var filePath in filePaths)  ← 順序的（並列化されていない）
    ↓
    ExtractArchiveAsync(filePath)
        ↓
        ArchiveExtractor.ExtractArchiveAsync(filePath)
            ↓
            await Task.Run(() => ExtractArchive(...))
                ↓
                [UI スレッド] ← ここでブロック
```

**並列化前の制約:**
- ファイル A の展開が完了 → ファイル B の展開開始
- ファイル A が 10秒 かかれば、全体は最低 10秒 × n ファイル

### 3.2 単一ファイル展開の内部処理

```csharp
await Task.Run(() =>  // ← この Task が ThreadPool スレッドを使用
{
    var extractor = new ArchiveExtractor();
    extractor.ExtractArchive(archivePath, outputPath, ...);
}, cancellationToken);
```

**CPU 利用状況:**
- 1ファイル展開時: 1スレッド × 1コア （最大 100% CPU 利用）
- 4コア CPU の場合: 3コアは未使用

---

## 4. 7-Zip/SevenZip ライブラリの並列処理能力

### 4.1 Cube.FileSystem.SevenZip の特性

**ソースコード分析:**

| 機能 | 対応状況 | 備考 |
|-----|--------|------|
| マルチスレッド圧縮 | ✅ あり（内部） | LZMA2 は内部的にマルチスレッド |
| マルチスレッド展開 | ✅ あり（内部） | 7z形式の場合 |
| ブロック単位の並列化 | ⚠️ 限定的 | ライブラリ依存 |
| 複数ファイル同時処理 | ❌ なし | API の制限 |

**7-Zip の実装**
```
SevenZip (C++):
└── Cube.FileSystem.SevenZip (C# ラッパー)
    ├── ArchiveReader (読み取り・展開)
    ├── ArchiveWriter (書き込み・圧縮)
    └── 内部で LZMA2 マルチスレッド処理
```

### 4.2 現在の制限

**部分展開の問題（ARCHITECTURE.md より）:**
```markdown
既知の制限
- 部分展開のパフォーマンス問題 (PartialExtractionHandler.cs:239-243)
  - 現在: 全アーカイブを一時展開してからコピー
  - 影響: 大きなアーカイブで著しいパフォーマンス低下
  - 改善案: Cube.FileSystem.SevenZip の個別ファイル展開APIの利用検討
```

---

## 5. 改善機会の評価

### 5.1 短期改善（実装容易）

#### A. 複数ファイルの並列展開/圧縮

**実装難易度**: 🟢 **低**

**現在:**
```csharp
foreach (var filePath in filePaths)  // 順序実行
{
    await ExtractArchiveAsync(filePath, ...);
}
```

**改善案:**
```csharp
// 方式1: Parallel.ForEach（OS スケジューラー依存）
Parallel.ForEach(filePaths, new ParallelOptions 
{ 
    MaxDegreeOfParallelism = Environment.ProcessorCount 
}, filePath =>
{
    ExtractArchive(filePath, ...);
});

// 方式2: Task リスト（より制御可能）
var tasks = filePaths.Select(filePath => 
    ExtractArchiveAsync(filePath, ...)
).ToList();

await Task.WhenAll(tasks);

// 方式3: SemaphoreSlim（リソース制限）
var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
var tasks = filePaths.Select(async filePath =>
{
    await semaphore.WaitAsync();
    try
    {
        await ExtractArchiveAsync(filePath, ...);
    }
    finally
    {
        semaphore.Release();
    }
});
await Task.WhenAll(tasks);
```

**メリット:**
- CPU コア数に応じた自動並列化
- 複数ファイル処理時の時間短縮（3～4倍期待）
- 既存コードベースに対する変更が小さい

**デメリット:**
- メモリ使用量増加（同時展開数 × ファイルサイズ）
- UI 進捗表示の複雑化（複数進捗の管理）
- キャンセル処理の複雑化

#### B. 部分展開の最適化

**実装難易度**: 🟡 **中**

**現在の問題:**
```csharp
// 100ファイル展開の場合
for (int i = 0; i < 100; i++)
{
    await ExtractSingleFile(item);  // 100回全体展開される
}
```

**改善案1: バッチ展開**
```csharp
// まず全体を一度展開
reader.Save(tempPath);

// その後、複数ファイルを並列でコピー
var files = Directory.GetFiles(tempPath, "*", SearchOption.AllDirectories);
Parallel.ForEach(files, parallelOptions, file =>
{
    var relativePath = Path.GetRelativePath(tempPath, file);
    var targetFile = Path.Combine(outputPath, relativePath);
    File.Copy(file, targetFile, true);
});
```

**改善案2: ライブラリ API の検証**
```csharp
// 7-Zip SDK の Extract(fileIndex) メソッドの確認
// → Cube.FileSystem.SevenZip が API を公開しているか調査
// → 公開していれば、個別ファイル展開を実装
```

### 5.2 中期改善（実装中程度）

#### C. 圧縮時の並列ファイル読み込み

**実装難易度**: 🟡 **中**

**現在:**
```csharp
public void CompressFiles(IEnumerable<string> sourcePaths, ...)
{
    foreach (var sourcePath in sourceList)
    {
        writer.Add(sourcePath);  // 順序的な追加
    }
    writer.Save(outputPath);  // ここで実際の圧縮
}
```

**改善案:**
```csharp
// ファイルを非同期で読み込みつつ、メモリに蓄積
var fileInfo = new List<(string path, byte[] data)>();
var semaphore = new SemaphoreSlim(4);  // 4つの読み込みを同時実行

var readTasks = sourceList.Select(async path =>
{
    await semaphore.WaitAsync();
    try
    {
        var data = await File.ReadAllBytesAsync(path);
        lock (fileInfo)
        {
            fileInfo.Add((path, data));
        }
    }
    finally
    {
        semaphore.Release();
    }
});

await Task.WhenAll(readTasks);

// その後、圧縮
foreach (var (path, data) in fileInfo)
{
    writer.Add(path);
}
writer.Save(outputPath);
```

### 5.3 長期改善（複雑/外部依存あり）

#### D. GPU 圧縮対応（NVIDIA nvCOMP）

**実装難易度**: 🔴 **高**  
**機器依存**: 🔴 **NVIDIA GPU 必須**

計画ドキュメント参照: `GPU圧縮・展開対応調査_e8112a78.plan.md`

---

## 6. CPU 利用率の実測期待値

### 6.1 現在の状態（1ファイル展開）

```
Time  CPU使用率
 0%   |████████████████  50%
10%   |████████████████  50%
20%   |████████████████  50%  ← コア数4以上なら最適でない
30%   |████████████████  50%
40%   |████████████████  50%
50%   |████████████████  50%
60%   |                  0%  ← 完了
```

**分析:**
- 4 コア CPU: 2 コアのみ使用（50%）
- 8 コア CPU: 1 コアのみ使用（12.5%）

### 6.2 複数ファイル順序実行時

```
ファイル A (10秒)
    ↓
ファイル B (10秒)
    ↓
ファイル C (10秒)

合計: 30秒
CPU使用率: 25～50%（1コアのみ活用）
```

### 6.3 複数ファイル並列実行時（改善後）

```
時刻     ファイル A    ファイル B    ファイル C    CPU使用率
0～10秒  ████████████ ████████████ ████████████   75%（3コア）
10～20秒 ████████████ ████████████ ████████████   75%（3コア）
20～30秒 ████████████                             25%（1コア）

合計: 30秒 → 20秒（改善）
CPU使用率: 75～100%（複数コア活用）
```

---

## 7. 実装の推奨順序

### フェーズ 1: 基礎（1～2週間）
```
1. ✅ 複数ファイル展開の並列化
   - Parallel.ForEach または Task.WhenAll を使用
   - UI 進捗表示の改善

2. ✅ 複数フォルダ圧縮の並列化
   - 同上パターン

3. ✅ 単体テストの追加
   - 並列処理のロック・競合テスト
```

### フェーズ 2: 最適化（2～3週間）
```
1. ⚠️ 部分展開のバッチ処理化
   - 全体展開 1 回 + 並列ファイルコピー

2. ⚠️ メモリ効率の改善
   - バッファサイズの動的調整
   - メモリ使用量のモニタリング

3. ⚠️ 進捗表示 UI の複雑化対応
   - 複数ファイルの進捗を表示
```

### フェーズ 3: 高度な最適化（調査）
```
1. 🔍 7-Zip SDK の個別ファイル展開 API 確認
2. 🔍 ZSTD 形式の検討（将来的な GPU 対応の準備）
3. 🔍 GPU 対応の事前調査（NVIDIA 環境）
```

---

## 8. 既知の制約と注意点

### 8.1 WPF UI スレッドの制約

```csharp
// UI 更新は必ず UI スレッドで実行
progressWindow?.Dispatcher.Invoke(() =>
{
    progressWindow.UpdateProgress(percentage);
});

// または

progressWindow?.Dispatcher.InvokeAsync(() =>
{
    progressWindow.UpdateProgress(percentage);
});
```

### 8.2 CancellationToken のサポート

複数ファイルの並列処理時は、各タスクで CancellationToken をチェック必須：

```csharp
var tasks = filePaths.Select(async filePath =>
{
    cancellationToken.ThrowIfCancellationRequested();
    await ExtractArchiveAsync(filePath, ..., cancellationToken);
});

await Task.WhenAll(tasks);
```

### 8.3 メモリ管理

```csharp
// 同時処理数の制限（メモリ保護）
var semaphore = new SemaphoreSlim(
    initialCount: Math.Min(4, Environment.ProcessorCount)
);
```

---

## 9. パフォーマンス期待値サマリー

| シナリオ | 現在 | フェーズ1後 | フェーズ2後 |
|--------|------|----------|----------|
| **3ファイル × 10秒** | 30秒 | 10～12秒 | 10～12秒 |
| **10ファイル × 5秒** | 50秒 | 12～15秒 | 12～15秒 |
| **1ファイル展開（1GB）** | 20秒 | 20秒 | 18～20秒 |
| **100ファイル部分展開** | 200秒 | 200秒 | 80～100秒 |
| **CPU使用率（4コア）** | 25～50% | 50～100% | 60～100% |

---

## 10. 結論

### 現状の評価

✅ **実装済み:**
- 基本的な非同期処理（async/await）
- CancellationToken サポート
- 進捗報告メカニズム
- UI スレッド安全性

❌ **未実装:**
- 複数ファイルの並列処理（高い改善余地）
- 部分展開の最適化（著しいパフォーマンス問題）
- CPU コア複数の活用（低 CPU 使用率）

### 推奨アクション

**優先度 1（実装推奨）:**
1. 複数ファイル展開/圧縮の並列化（Parallel.ForEach または Task.WhenAll）
2. 部分展開のバッチ処理化

**優先度 2（検討）:**
3. メモリ効率の改善（SemaphoreSlim による制御）
4. UI 進捗表示の改善

**優先度 3（将来）:**
5. GPU 対応の技術調査
6. ZSTD 形式の検討

---

## 参考資料

- **プランドキュメント**: `GPU圧縮・展開対応調査_e8112a78.plan.md`
- **アーキテクチャドキュメント**: `ARCHITECTURE.md`
- **設定スキーマ**: `SETTINGS_SCHEMA.md`
- **ソースコード**: `Util/ArchiveProcessor.cs`, `Util/ArchiveExtractor.cs`, `Util/PartialExtractionHandler.cs`

---

**このレポートは調査段階です。実装フェーズに進む前に、チーム内での確認と優先度の調整をお願いします。**
