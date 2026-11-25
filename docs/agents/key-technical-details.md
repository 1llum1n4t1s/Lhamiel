# Key Technical Details

- 展開パス、パスワード圧縮、暗号化展開を変えるときは[展開とパスワードの安全規約](security.md)に従う。
- 設定の即時反映、Unicode、走査属性、除外規則を変えるときは[設定と圧縮除外の規約](configuration.md)に従う。
- タスクバー、Avalonia、設定アイコン、アクリルを変えるときは[UI 実装規約](conventions.md)に従う。
- Velopack・署名・公開を変えるときは[リリース手順](release-process.md)に従う。

- **Native AOT** (`PublishAot=true`) — avoid reflection-heavy patterns
- **対応形式の追加・変更** — 展開拡張子は `ArchiveFormatConstants.SupportedArchiveFormats` を更新し、展開判定・関連付け UI の共通カタログを維持する。圧縮形式は `Settings.SupportedCompressionFormats`、`ArchiveCompressor.WritableFormats` / `ParseFormat`、CLI の形式許可リストとパスワード可否を併せて照合する。
- **右クリックメニュー** — 登録・解除を変更するときは [シェル契約](../design/adopted-design-decisions-and-trade-offs.md#shell-command-visibility-separate-from-package-lifetime)を読み、失敗時の表示維持・COM 互換経路・パッケージ寿命を守る。Windows 10 build 19041 以降は sparse MSIX の COM 登録を使う。展開／圧縮の CLSID・canonical GUID・静的 verb 名を C++、manifest、`ShellContextMenu` で一致させる。`scripts/build-shell-integration.ps1` が x64/ARM64 DLL と sparse MSIX を生成し、`scripts/release-local.ps1` が署名後に配布する。Publisher は Certum 証明書 Subject と完全一致させる。
- **Kagayoi.Support の復元グラフ** — [2つの復元モード](../design/adopted-design-decisions-and-trade-offs.md#shared-support-sdk-with-two-resolution-modes)を維持する。Release に Debug DLL が混入しないよう `Directory.Build.props` の `ShouldUnsetParentConfigurationAndPlatform=false` と、直接参照の `SetConfiguration="Configuration=$(Configuration)"` / `SetPlatform="Platform=AnyCPU"` を維持する。依存更新時は[package-mode restore 2 コマンド](build-development-commands.md)で追跡対象 lockfile を更新・検証する。
- **CRDebugger の Debug 限定参照** — `PackageReference` は構成非依存のまま `ExcludeAssets="compile;runtime"` + `GeneratePathProperty="true"` を維持し、Debug 用の `<Reference>` だけを構成条件付きにする。`PackageReference` 自体を Debug 条件付きにすると、Release restore 後の Debug Rebuild が共有 `project.assets.json` から参照を解決できず CS0246 になる
- **7z.dll** — `1llum1n4t1s.Sevenzip` NuGet が同梱。`NativeLibraryManager` が起動時に `LoadLibrary` で固定。`GeneratePathProperty` と `licenses/7-Zip/License.txt` の build/publish コピーを維持し、同梱 DLL のライセンス告知を配布出力から欠落させない
- **ネイティブ操作の直列化** — [並行ゲート契約](../design/adopted-design-decisions-and-trade-offs.md#layered-concurrency-gates)に従い、SevenZip の reader/writer 生成から Dispose まで `NativeArchiveGate` で保護し、再入させない。全入口の worker 分離、展開先・圧縮出力のプロセス間保護、出力確定・復元までの保持と設定保存の整合を維持する。UnLhaRe は[別契約](../design/adopted-design-decisions-and-trade-offs.md#versioned-unlhare-integration)に従う。
- **Logger** — `SuperLightLogger` File Target。ログ保存先は [README.md](../readme/section-09.md#トラブルシューティング)。ログ経路で `Environment.UserName` を呼ばない（RDP 等で無期限ブロックする）。`MaskUserPath` は USERNAME とプロファイルフォルダ名の事前コンパイル済みパターンを使う。
- **AllowUnsafeBlocks** for P/Invoke (COM interop in `ShortcutCreator`, `FileIconHelper`, `CrashHandler`)
- Async/await + CancellationToken throughout all I/O operations
- Version: `Directory.Build.props` の `<Version>` タグで全プロジェクト共有
- **Support intake**: バージョンタブの問い合わせ導線は `SupportDialog` を開き、メール確認コードの検証後に `Kagayoi.Support` へ送信する。公開GitHub Issuesは開発者向け技術課題に限定し、利用者の問い合わせとメールアドレスは載せない
