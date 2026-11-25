# Testing

Tests in `src/Lhamiel.Tests.Unit/` using **xUnit 4**. The test project references the main project directly via `InternalsVisibleTo`.

- 通常テスト: `*Tests.cs`
- 嫌がらせテスト (adversarial): `*AdversarialTests.cs` — 境界値、異常入力、状態遷移の矛盾
- `[Collection("ArchiveProcessor")]` — `ArchiveProcessor` の static プロパティを差し替えるテストは排他実行が必要
- ADS テスト (`MotwPropagatorTests`): Windows 限定、`Assert.SkipWhen(!OperatingSystem.IsWindows())` でスキップ
- ログの保持期限・命名規則を変更するときは `LoggerRetentionTests` を実行し、通常ログと worker ログのローテートを含む期限判定、境界日の保持、緊急ログ・未知の命名形式の除外を確認する。設計は [DESIGN.md](../design/adopted-design-decisions-and-trade-offs.md#diagnostic-log-retention) を参照する。
- IPC 圧縮の分岐・設定保存・プロセス間ゲートを変更するときは、既存の Lhamiel を閉じ、「元と同じ場所に保存」ON・パスワード保護 OFF にした環境で `pwsh -NoProfile -File scripts/Test-ParallelShellCompression.ps1 -AppPath <検証するLhamiel.exeの絶対パス> -ArtifactDirectory <未使用の成果物ディレクトリの絶対パス>` を実行する。並行 PID・後発処理の先行完了・ZIP 内容の SHA256 を `result.json` とログで確認する。スクリプトは設定を変更せず、入力とログを成果物として保持する。同一出力への直列化とパスワード保存の競合は別途確認する。
- 展開公開・MotW を変更するときは、通常展開と衝突解決の両経路で一時ルートと配下のリンクが公開前に拒否されること、MotW がリンクのルート・ファイルへ書き込まないことを確認する。属性検査後のリンク差し替えまで防げるとは扱わない（[設計の不変条件](../design/critical-invariants.md#critical-invariants)）。
- 上書き準備を変更するときは `ExtractionOverwriteAtomicityTests`、右クリックメニューの登録・解除を変更するときは `ShellContextMenuTests` と `ProgramLifecycleTests` を実行し、[DESIGN.md](../design/adopted-design-decisions-and-trade-offs.md#adopted-design-decisions-and-trade-offs) の復元・パッケージ寿命の契約を確認する。シェル変更時は[ネイティブシェルのテスト](build-development-commands.md)も実行し、実際の Explorer（Windows 11 の新メニュー／「その他のオプション」）と対象ファイルマネージャーで複数選択の一括引渡し・重複表示・OFF/ON 切替を確認する。COM 能力確認を変更するときは、片方／両方の有効コマンドでの `E_NOINTERFACE`、他の負の HRESULT、再適用後の能力回復を確認し、静的 verb・表示状態・UI と保存設定の整合を検証する。`Marshal.ThrowExceptionForHR` の例外型を `COMException` に固定せず HRESULT で照合する。GUID の一致だけで重複表示の解消を保証しない。
- LHA/LZH 境界の変更時は `LzhArchiveIntegrationTests` と `ArchiveErrorHandlerTests` を実行し、Unicode・空フォルダー・選択展開・時刻復元、visitor の途中失敗、既存出力保護、読めない入力のスキップ、上限・キャンセルを確認する。`LzhArchiveBackendProvider.Current` を差し替えるテストは `[Collection("Sequential")]` で排他実行し、`finally` で元へ戻す。形式やパスワード可否の変更には `SettingsTests`、`SettingsPasswordTests`、`ArchiveProcessorPasswordTests`、`AppCommandLineLifecycleTests`、`LocaleTests` も含める。
