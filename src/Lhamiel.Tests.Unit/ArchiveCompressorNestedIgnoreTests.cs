using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveCompressor.ScanSourceFiles のネスト ignore 系テスト（ArchiveCompressorTests の partial 分割）。
/// RespectNestedGitignore・SourceIgnoreFileNames の優先順位継承・.lhaignore による枝刈り・
/// ディレクトリ否定再包含・圧縮元ルート名とパターンの照合を扱う。
/// テスト名（完全修飾名）とヘルパーは元のクラスと共有する。
/// </summary>
public partial class ArchiveCompressorTests
{
    // === ScanSourceFiles: ネストされた .gitignore 統合テスト ===

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignore_AppliesRulesScopedToSubdirectory()
    {
        // ディレクトリ構造:
        //   testRoot/repoA/.gitignore   → "*.log"
        //   testRoot/repoA/debug.log    → 除外されるべき
        //   testRoot/repoA/keep.txt     → 含まれる
        //   testRoot/repoB/debug.log    → 含まれる (repoA の .gitignore 範囲外)
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var repoADir = Path.Combine(testRoot, "repoA");
        var repoBDir = Path.Combine(testRoot, "repoB");
        try
        {
            Directory.CreateDirectory(repoADir);
            Directory.CreateDirectory(repoBDir);
            File.WriteAllText(Path.Combine(repoADir, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(repoADir, "debug.log"), "x");
            File.WriteAllText(Path.Combine(repoADir, "keep.txt"), "x");
            File.WriteAllText(Path.Combine(repoBDir, "debug.log"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // repoA/debug.log は .gitignore で除外される
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("repoA", "debug.log")));
            // repoA/keep.txt は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("repoA", "keep.txt")));
            // repoB/debug.log は repoA の .gitignore スコープ外なので含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("repoB", "debug.log")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_SourceIgnoreFileNames_HigherPriorityIsInheritedByDescendants()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var childDir = Path.Combine(testRoot, "child");
        try
        {
            Directory.CreateDirectory(childDir);
            File.WriteAllText(Path.Combine(testRoot, ".lhamielignore"), "*.tmp");
            File.WriteAllText(Path.Combine(testRoot, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(testRoot, "root.tmp"), "excluded by the higher-priority file");
            File.WriteAllText(Path.Combine(testRoot, "root.log"), "must remain because root .gitignore is not merged");

            // ルートで最優先の .lhamielignore が選ばれた後は、子孫で低優先の .gitignore へ戻らない。
            File.WriteAllText(Path.Combine(childDir, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(childDir, "child.log"), "must remain because priority never decreases");
            File.WriteAllText(Path.Combine(childDir, "child.txt"), "included");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: [],
                sourceIgnoreFileNames: [".lhamielignore", ".gitignore"]);

            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("root.tmp"));
            Assert.Contains(result, r => r.fullPath.EndsWith("root.log"));
            Assert.Contains(result, r => r.fullPath.EndsWith("child.log"));
            Assert.Contains(result, r => r.fullPath.EndsWith("child.txt"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_SourceIgnoreFileNames_HigherPriorityCanTakeOverWithinOneBranch()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var customDir = Path.Combine(testRoot, "custom");
        var customChildDir = Path.Combine(customDir, "child");
        var legacyDir = Path.Combine(testRoot, "legacy");
        try
        {
            Directory.CreateDirectory(customChildDir);
            Directory.CreateDirectory(legacyDir);

            // ルートでは低優先の .gitignore から開始する。
            File.WriteAllText(Path.Combine(testRoot, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(testRoot, "root.log"), "excluded by root .gitignore");

            // custom 分岐では高優先の .lhamielignore へ昇格し、親の除外から keep.log を救う。
            File.WriteAllText(Path.Combine(customDir, ".lhamielignore"), "!keep.log");
            File.WriteAllText(Path.Combine(customDir, "keep.log"), "included by higher-priority child rule");
            File.WriteAllText(Path.Combine(customDir, "debug.log"), "still excluded by inherited root rule");

            // 一度 .lhamielignore へ昇格した分岐では、さらに下の .gitignore へ戻らない。
            File.WriteAllText(Path.Combine(customChildDir, ".gitignore"), "*.txt");
            File.WriteAllText(Path.Combine(customChildDir, "note.txt"), "must remain");

            // 兄弟分岐は root の .gitignore 優先度を独立して継承し、同名ルールを追加できる。
            File.WriteAllText(Path.Combine(legacyDir, ".gitignore"), "*.txt");
            File.WriteAllText(Path.Combine(legacyDir, "note.txt"), "excluded in the legacy branch");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: [],
                sourceIgnoreFileNames: [".lhamielignore", ".gitignore"]);

            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("root.log"));
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("custom", "keep.log")));
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("custom", "debug.log")));
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("custom", "child", "note.txt")));
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("legacy", "note.txt")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_SourceIgnoreFileNames_EmptyHigherPriorityFileSuppressesFallback()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var childDir = Path.Combine(testRoot, "child");
        try
        {
            Directory.CreateDirectory(childDir);
            File.WriteAllText(Path.Combine(testRoot, ".lhamielignore"), string.Empty);
            File.WriteAllText(Path.Combine(testRoot, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(testRoot, "debug.log"), "must remain");
            File.WriteAllText(Path.Combine(childDir, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(childDir, "debug.log"), "must also remain");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: [],
                sourceIgnoreFileNames: [".lhamielignore", ".gitignore"]);

            Assert.Contains(result, r => r.fullPath.Equals(
                Path.Combine(testRoot, "debug.log"),
                StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("child", "debug.log")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_LhaignorePrunesSubtreeBeforeDiscoveringNestedGitignore()
    {
        // .lhaignore でディレクトリが枝刈りされていれば、その配下の .gitignore は読まれず
        // 中身のルールも適用されない（性能保護 + 仕様の両方を回帰から守る）。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var ignoredDir = Path.Combine(testRoot, "ignored");
        var keptDir = Path.Combine(testRoot, "kept");
        try
        {
            Directory.CreateDirectory(ignoredDir);
            Directory.CreateDirectory(keptDir);
            // ignored/ 配下の .gitignore は "*.txt" を除外しようとするが、
            // .lhaignore が ignored/ 自体を枝刈りするので発見されないはず。
            File.WriteAllText(Path.Combine(ignoredDir, ".gitignore"), "*.txt");
            File.WriteAllText(Path.Combine(ignoredDir, "data.txt"), "x");
            File.WriteAllText(Path.Combine(keptDir, "doc.txt"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Compile(["ignored/"]),
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: new[] { "ignored/" });

            // ignored/ 配下は丸ごと除外される (data.txt も含む)
            Assert.DoesNotContain(result, r => r.fullPath.Contains(Path.Combine("ignored", "data.txt")));
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("ignored", ".gitignore")));
            // kept/doc.txt は ignored/.gitignore の "*.txt" の影響を受けず残る
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("kept", "doc.txt")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_NestedGitignore_XcodeDirectoryReinclude_KeepsSharedFiles()
    {
        // バグ B の end-to-end 回帰テスト。標準 Xcode .gitignore（`*.xcodeproj/*` で潰し、
        // `!*.xcodeproj/xcshareddata/` 等のディレクトリ否定で共有メタを再包含）を実 DFS に通し、
        // git と同じく「再包含ディレクトリ配下の共有ファイルが含まれる」ことを保証する。
        // 構造（writingtoolsjp/macos/ を再現）。すべて実 git の挙動（git check-ignore で検証済）と一致する:
        //   testRoot/macos/.gitignore
        //   testRoot/macos/app.xcodeproj/project.pbxproj                                  → 含む（直接ファイル否定）
        //   testRoot/macos/app.xcodeproj/xcshareddata/xcschemes/App.xcscheme              → 含む（★修正点: dir 否定再包含）
        //   testRoot/macos/app.xcodeproj/xcshareddata/WorkspaceSettings.xcsettings        → 除外（再包含 dir 配下でも個別 re-exclude が効く）
        //   testRoot/macos/app.xcodeproj/project.xcworkspace/contents.xcworkspacedata     → 含む（★修正点: dir 否定再包含 + file 否定）
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var macos = Path.Combine(testRoot, "macos");
        var proj = Path.Combine(macos, "app.xcodeproj");
        try
        {
            Directory.CreateDirectory(Path.Combine(proj, "xcshareddata", "xcschemes"));
            Directory.CreateDirectory(Path.Combine(proj, "project.xcworkspace"));
            File.WriteAllText(Path.Combine(macos, ".gitignore"), string.Join('\n',
            [
                "*.xcworkspace",
                "*.xcodeproj/*",
                "!*.xcodeproj/project.pbxproj",
                "!*.xcodeproj/xcshareddata/",
                "!*.xcodeproj/project.xcworkspace/",
                "!*.xcworkspace/contents.xcworkspacedata",
                "**/xcshareddata/WorkspaceSettings.xcsettings",
            ]));
            File.WriteAllText(Path.Combine(proj, "project.pbxproj"), "x");
            File.WriteAllText(Path.Combine(proj, "xcshareddata", "xcschemes", "App.xcscheme"), "x");
            File.WriteAllText(Path.Combine(proj, "xcshareddata", "WorkspaceSettings.xcsettings"), "x");
            File.WriteAllText(Path.Combine(proj, "project.xcworkspace", "contents.xcworkspacedata"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // 直接ファイル否定（従来も OK）
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("app.xcodeproj", "project.pbxproj")));
            // ★ バグ B 修正: ディレクトリ否定再包含の配下が含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("xcschemes", "App.xcscheme")));
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("project.xcworkspace", "contents.xcworkspacedata")));
            // 再包含ディレクトリ配下でも、個別 re-exclude パターンに当たるファイルは除外されたまま（git と一致）
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("WorkspaceSettings.xcsettings"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_NestedGitignore_AllowListStarBangSrc_StillExcludesSubtree()
    {
        // Codex P2 の end-to-end 回帰防止。`*` + `!src/` の allow-list で、再包含された src の
        // 配下サブディレクトリ src/sub は枝刈りされ、その配下ファイルがアーカイブに混入しないことを保証する
        // （git と一致: src/keep.txt も src/sub/file.txt も top.txt も全て除外）。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var proj = Path.Combine(testRoot, "proj");
        try
        {
            Directory.CreateDirectory(Path.Combine(proj, "src", "sub"));
            File.WriteAllText(Path.Combine(proj, ".gitignore"), string.Join('\n', ["*", "!src/"]));
            File.WriteAllText(Path.Combine(proj, "top.txt"), "x");
            File.WriteAllText(Path.Combine(proj, "src", "keep.txt"), "x");
            File.WriteAllText(Path.Combine(proj, "src", "sub", "file.txt"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // ★ Codex P2: 枝刈りされた src/sub 配下は混入しない
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("sub", "file.txt")));
            // src 直下のファイルも top.txt も `*` で除外されたまま
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("src", "keep.txt")));
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("top.txt"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignore_FallbackMatcherRulesArePreserved()
    {
        // Codex P2 指摘対応 (#3305241279): respectNestedGitignore=true かつ globalIgnoreLines=null
        // のとき、呼び出し元から渡された matcher (fallbackMatcher) のルールが silent ドロップされる
        // 経路があった。修正後は fallbackMatcher が base layer として必ず保持されることを確認する。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(testRoot);
            // .lhaignore 由来の matcher を直接構築 (生 lines は ScanSourceFiles に渡さない)
            var fallbackMatcher = GitignoreMatcher.Compile(["*.tmp"]);

            // テストファイル: tmp/log の混在
            File.WriteAllText(Path.Combine(testRoot, "data.tmp"), "should be excluded");
            File.WriteAllText(Path.Combine(testRoot, "data.log"), "should be included");

            // globalIgnoreLines = null だが、fallbackMatcher の "*.tmp" は効くべき
            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                fallbackMatcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: null); // ← Codex 指摘の null パス

            // *.tmp は fallbackMatcher で除外される
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("data.tmp"));
            // *.log は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("data.log"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignore_RootGitignorePrunesNestedDiscovery()
    {
        // RTK レビュー Codex P2 対応: DiscoverGitignoreFiles が root の .gitignore を読み込んだ後、
        // サブディレクトリ走査でもそのルールを枝刈りに使うことを確認する。
        // 具体的には、root .gitignore で "vendor/" を除外している場合、vendor/ 配下の nested .gitignore
        // は読まれず、vendor/ 配下のファイルも圧縮対象から除外される。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(testRoot);
            // root .gitignore で vendor/ を除外
            File.WriteAllText(Path.Combine(testRoot, ".gitignore"), "vendor/\n");

            // vendor/ 配下にダミーファイルと、さらに別のルールを持つ nested .gitignore を配置
            // 期待: vendor/ ごと枝刈りされるので、nested .gitignore は読み込まれず、vendor/*.txt も含まれない
            var vendorDir = Path.Combine(testRoot, "vendor");
            Directory.CreateDirectory(vendorDir);
            File.WriteAllText(Path.Combine(vendorDir, ".gitignore"), "!keep.txt\n");
            File.WriteAllText(Path.Combine(vendorDir, "lib.txt"), "x");
            File.WriteAllText(Path.Combine(vendorDir, "keep.txt"), "x");

            // 一方、root 配下の通常ファイルは含まれる
            File.WriteAllText(Path.Combine(testRoot, "app.txt"), "x");

            // ルートの除外規則で枝刈りし、配下の否定ルールには到達しない。
            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // app.txt は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("app.txt"));
            // vendor/ 配下は root .gitignore で除外されて含まれない（nested .gitignore の !keep.txt も到達しない）
            Assert.DoesNotContain(result, r => r.fullPath.Contains("vendor"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignoreFalse_IgnoresGitignoreFiles()
    {
        // RespectNestedGitignore=false の場合、.gitignore は無視される
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(Path.Combine(testRoot, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(testRoot, "debug.log"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: false);

            // debug.log は .gitignore が読まれないので含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("debug.log"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_SourceRootBasenameMatchesAnchoredPattern_DoesNotEmptyArchive()
    {
        // ユーザーが「build」という名前のフォルダを圧縮対象として明示的に指定したとき、
        // `.lhaignore` / `.gitignore` の anchored パターン `/build` でルート自身が
        // 除外されて空アーカイブになる回帰を防ぐ（Codex P1 指摘 2026-05-26）。
        // gitignore セマンティクスでは `/build` は親基準でルート直下にマッチするものであり、
        // ユーザーが明示指定したソースルート自身を意味しない。
        var parent = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var buildDir = Path.Combine(parent, "build");
        try
        {
            Directory.CreateDirectory(buildDir);
            File.WriteAllText(Path.Combine(buildDir, "artifact.txt"), "x");
            File.WriteAllText(Path.Combine(buildDir, "log.txt"), "x");

            // anchored パターン `/build` を含む matcher
            var matcher = GitignoreMatcher.Compile(["/build"]);

            var result = await ArchiveCompressor.ScanSourceFiles(
                [buildDir],
                matcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: false);

            // ルート basename が "build" でも、anchored `/build` でルート自身は除外されない
            Assert.Contains(result, r => r.fullPath.EndsWith("artifact.txt"));
            Assert.Contains(result, r => r.fullPath.EndsWith("log.txt"));
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_SourceRootBasenameMatchesDirectoryPattern_DoesNotEmptyArchive()
    {
        // ユーザーが「node_modules」という名前のフォルダを圧縮対象として明示指定したとき、
        // `node_modules/` パターンで空にならず、配下ファイルが正しく含まれることを保証する。
        // ignore ルールは子エントリに適用されるべきで、ユーザー明示指定のルートを覆さない。
        var parent = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var nodeModulesDir = Path.Combine(parent, "node_modules");
        try
        {
            Directory.CreateDirectory(nodeModulesDir);
            File.WriteAllText(Path.Combine(nodeModulesDir, "package.json"), "{}");

            var matcher = GitignoreMatcher.Compile(["node_modules/"]);

            var result = await ArchiveCompressor.ScanSourceFiles(
                [nodeModulesDir],
                matcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: false);

            // ユーザー明示指定の node_modules ルート配下は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("package.json"));
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }
}
