using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// 同名ファイル衝突解決の嫌がらせテスト 😈
/// ArchiveCompressor.ResolveRelativePathConflicts / ResolveByRenaming / GetUniqueOutputPath を
/// 境界値・極端入力・状態遷移の矛盾で攻める
/// </summary>
public class FileConflictAdversarialTests
{
    // ═══════════════════════════════════════════════════
    // 🗡️ カテゴリ1: 境界値・極端入力（Boundary Assault）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 空のファイルリストを渡した場合にクラッシュしないこと
    /// </summary>
    [Fact]
    public void ResolveConflicts_空リスト_クラッシュしない()
    {
        var files = new List<(string fullPath, string relativePath)>();

        var resultRename = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);
        var resultPreserve = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: true);

        Assert.Empty(resultRename);
        Assert.Empty(resultPreserve);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 1件だけのリストでは衝突チェックが不要（早期リターン）
    /// </summary>
    [Fact]
    public void ResolveConflicts_単一ファイル_変更なし()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\file.txt", "file.txt"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Single(result);
        Assert.Equal("file.txt", result[0].relativePath);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 大量の同名ファイル（100件）でも正しく連番が振られること
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_100件同名_全てユニーク()
    {
        var files = new List<(string fullPath, string relativePath)>();
        for (var i = 0; i < 100; i++)
        {
            files.Add(($@"C:\Folder{i}\001.jpg", "001.jpg"));
        }

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(100, result.Count);
        // 全てのrelativePathがユニークであること
        var uniquePaths = new HashSet<string>(result.Select(r => r.relativePath), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(100, uniquePaths.Count);
        // 最初のファイルはオリジナル名を維持
        Assert.Equal("001.jpg", result[0].relativePath);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 拡張子なしファイル（Makefile, LICENSE 等）の衝突解決
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_拡張子なしファイル()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\Makefile", "Makefile"),
            (@"C:\B\Makefile", "Makefile"),
            (@"C:\C\Makefile", "Makefile"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(3, result.Count);
        Assert.Equal("Makefile", result[0].relativePath);
        Assert.Equal("Makefile_1", result[1].relativePath);
        Assert.Equal("Makefile_2", result[2].relativePath);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ドットファイル（.gitignore, .env 等）の衝突解決。
    /// Path.GetFileNameWithoutExtension(".gitignore") は "" を返す
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_ドットファイル()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\.gitignore", ".gitignore"),
            (@"C:\B\.gitignore", ".gitignore"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(2, result.Count);
        Assert.Equal(".gitignore", result[0].relativePath);
        // Path.GetFileNameWithoutExtension(".gitignore") = "" → "_1.gitignore" になるはず
        // 空の name + "_1" + ext → "_1.gitignore"
        Assert.Equal("_1.gitignore", result[1].relativePath);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 複数の拡張子（.tar.gz 等）のリネーム。
    /// Path.GetExtension は最後の拡張子のみ (.gz) を取得する
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_複数拡張子()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\archive.tar.gz", "archive.tar.gz"),
            (@"C:\B\archive.tar.gz", "archive.tar.gz"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(2, result.Count);
        Assert.Equal("archive.tar.gz", result[0].relativePath);
        // SplitStemAndExtension("archive.tar.gz") = ("archive", ".tar.gz")
        Assert.Equal("archive_1.tar.gz", result[1].relativePath);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// Unicode文字を含むファイル名の衝突解決
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_Unicode日本語ファイル名()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\写真A\画像.jpg", "画像.jpg"),
            (@"C:\写真B\画像.jpg", "画像.jpg"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(2, result.Count);
        Assert.Equal("画像.jpg", result[0].relativePath);
        Assert.Equal("画像_1.jpg", result[1].relativePath);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 既にリネームサフィックスっぽい名前（file_1.txt）との衝突
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_既存サフィックスとの衝突()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\file.txt", "file.txt"),
            (@"C:\B\file_1.txt", "file_1.txt"),   // 元からこの名前
            (@"C:\C\file.txt", "file.txt"),        // file.txt の重複 → file_1.txt にリネームしたいが...
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(3, result.Count);
        Assert.Equal("file.txt", result[0].relativePath);
        Assert.Equal("file_1.txt", result[1].relativePath);
        // file_1.txt は既に使われているので file_2.txt にスキップされるべき
        Assert.Equal("file_2.txt", result[2].relativePath);
    }

    // ═══════════════════════════════════════════════════
    // 🎭 カテゴリ5: 型パンチ・プロトコル違反（Type Punching）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category type @severity high
    /// Windows予約デバイス名（CON, NUL, COM1等）を含むファイル名
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_Windows予約名()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\CON.txt", "CON.txt"),
            (@"C:\B\CON.txt", "CON.txt"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(2, result.Count);
        Assert.Equal("CON.txt", result[0].relativePath);
        Assert.Equal("CON_1.txt", result[1].relativePath);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// パスセパレータを含む相対パス（サブフォルダ内でのリネーム）
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_サブフォルダ内衝突()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\X\Sub\001.jpg", Path.Combine("Sub", "001.jpg")),
            (@"C:\Y\Sub\001.jpg", Path.Combine("Sub", "001.jpg")),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(2, result.Count);
        Assert.Equal(Path.Combine("Sub", "001.jpg"), result[0].relativePath);
        Assert.Equal(Path.Combine("Sub", "001_1.jpg"), result[1].relativePath);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 非常に長いファイル名（240文字）のリネーム。サフィックス付与後も機能するか
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_超長ファイル名()
    {
        var longName = new string('a', 240) + ".txt";
        var files = new List<(string fullPath, string relativePath)>
        {
            ($@"C:\A\{longName}", longName),
            ($@"C:\B\{longName}", longName),
        };

        // クラッシュしないこと（パスの長さ制限はOSが管理）
        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);
        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].relativePath, result[1].relativePath);
    }

    // ═══════════════════════════════════════════════════
    // 🔀 カテゴリ4: 状態遷移の矛盾（State Machine Abuse）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category state @severity high
    /// 衝突 → 非衝突 → 衝突のパターン（交互に現れる同名ファイル）
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_交互衝突パターン()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\a.txt", "a.txt"),     // 初出
            (@"C:\B\b.txt", "b.txt"),     // 初出
            (@"C:\C\a.txt", "a.txt"),     // 衝突
            (@"C:\D\c.txt", "c.txt"),     // 初出
            (@"C:\E\b.txt", "b.txt"),     // 衝突
            (@"C:\F\a.txt", "a.txt"),     // 3回目の衝突
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(6, result.Count);
        Assert.Equal("a.txt", result[0].relativePath);
        Assert.Equal("b.txt", result[1].relativePath);
        Assert.Equal("a_1.txt", result[2].relativePath);
        Assert.Equal("c.txt", result[3].relativePath);
        Assert.Equal("b_1.txt", result[4].relativePath);
        Assert.Equal("a_2.txt", result[5].relativePath);
    }

    /// <summary>
    /// @adversarial @category state @severity high
    /// 全ファイルが同名（最悪ケースの性能テスト的な側面も）
    /// </summary>
    [Fact]
    public void ResolveConflicts_リネーム方式_全ファイル同名()
    {
        var files = new List<(string fullPath, string relativePath)>();
        for (var i = 0; i < 50; i++)
        {
            files.Add(($@"C:\Dir{i}\photo.jpg", "photo.jpg"));
        }

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(50, result.Count);
        Assert.Equal("photo.jpg", result[0].relativePath);
        for (var i = 1; i < 50; i++)
        {
            Assert.Equal($"photo_{i}.jpg", result[i].relativePath);
        }
    }

    // ═══════════════════════════════════════════════════
    // 🗡️ GetUniqueOutputPath の境界値テスト
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ディレクトリが存在する場合もユニーク化される（File.Exists だけでなく Directory.Exists もチェック）
    /// </summary>
    [Fact]
    public void GetUniqueOutputPath_同名ディレクトリが存在_回避される()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "UniquePathAdv_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // 出力パスと同名のディレクトリを作成
            var outputDir = Path.Combine(tempDir, "output.zip");
            Directory.CreateDirectory(outputDir);

            var result = ArchiveCompressor.GetUniqueOutputPath(outputDir);
            Assert.Equal(Path.Combine(tempDir, "output_1.zip"), result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// 存在しないディレクトリ内のパスが渡された場合（ディレクトリ自体は未作成）
    /// </summary>
    [Fact]
    public void GetUniqueOutputPath_存在しないディレクトリ_そのまま返す()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent", "file.zip");

        var result = ArchiveCompressor.GetUniqueOutputPath(path);
        Assert.Equal(path, result);
    }

    // ═══════════════════════════════════════════════════
    // 🗡️ パス保持方式の境界テスト
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// UNCパス（\\server\share\file.txt）からのパス保持
    /// </summary>
    [Fact]
    public void ResolveConflicts_パス保持方式_UNCパス()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"\\server\shareA\file.txt", "file.txt"),
            (@"\\server\shareB\file.txt", "file.txt"),
        };

        // クラッシュしないことを確認
        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: true);
        Assert.Equal(2, result.Count);
    }

    // ═══════════════════════════════════════════════════
    // 🌪️ カテゴリ6: 環境異常（Environmental Chaos）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// GetUniqueOutputPath: テスト実行中にファイルが作成される（TOCTOU競合のシミュレーション）
    /// 注: 実際のTOCTOUは再現困難だが、密な連番ファイルで擬似的にテスト
    /// </summary>
    [Fact]
    public void GetUniqueOutputPath_密な連番_全てスキップ()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "UniquePathDense_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // base + _1 ~ _99 まで全て作成
            File.WriteAllText(Path.Combine(tempDir, "dense.zip"), "");
            for (var i = 1; i <= 99; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"dense_{i}.zip"), "");
            }

            var result = ArchiveCompressor.GetUniqueOutputPath(Path.Combine(tempDir, "dense.zip"));
            Assert.Equal(Path.Combine(tempDir, "dense_100.zip"), result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

}
