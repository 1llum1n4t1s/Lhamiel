# Lhamiel

**シンプル＆高速なアーカイブ圧縮・展開ツール**

Lhamiel（ラミエル）は、Windows向けの使いやすいアーカイブ管理ツールです。ファイルやフォルダの圧縮、多数のアーカイブ形式の展開を、直感的なドラッグ＆ドロップ操作で快適に行えます。

<img width="600" alt="image" src="https://github.com/user-attachments/assets/b6f12698-9f45-45e6-88cb-3d6f57ae5482" />

<img width="600" alt="image" src="https://github.com/user-attachments/assets/f3e41ccc-2d32-4bb1-95a1-8c3a2065ea0b" />

## 主な特徴

- **かんたん操作** — ファイルをウィンドウにドラッグ＆ドロップするだけで、自動的に圧縮または展開を開始
- **二重ネスト防止** — 「アーカイブ名でフォルダを作成する」ON時、アーカイブ内ルートフォルダがアーカイブ名と一致する場合はフォルダ作成をスキップして二重ネストを防止
- **ロック中ファイルも圧縮可能** — 他プロセスが使用中のファイルもライブラリ側で自動的に一時コピーを作成して圧縮
- **隠し/システム属性も圧縮対象に含められる** — `.git` など Hidden/System 属性のフォルダが意図せず欠落しないよう、圧縮設定から対象有無を切り替え可能
- **圧縮除外リスト管理** — `.DS_Store`、`Thumbs.db`、`node_modules` など、圧縮時に除外したい名前を設定画面から追加・削除・既定値リセット可能
- **パスワード保護アーカイブ対応** — 暗号化された ZIP / 7z / RAR をドロップすると入力ダイアログが開き、誤入力時は自動的に再試行
- **ファイル衝突解決** — 展開・圧縮時に同名ファイルが競合した場合、Windows風のファイル比較ダイアログで選択的に処理。サムネイル表示や同一ファイル（日付・サイズが一致）の一括スキップに対応
- **ディスク容量チェック** — 展開・圧縮前に空き容量を事前確認し、処理中も定期監視。容量不足時は一時停止して対応可能
- **エラー回復** — 破損アーカイブでも可能な限りファイルを救出。「スキップ」や「再試行」を選択可能
- **ファイル関連付け** — よく使う形式を関連付ければ、ダブルクリックで素早く展開
- **テーマ切替** — ダーク / ライト / システム追従の3モード対応（アクリルブラー背景）
- **17言語対応** — 日本語・英語をはじめ17言語のUIローカライズに対応
- **ネイティブ AOT ビルド** — .NET ランタイム不要で起動が速く、Windows x64 / ARM64 の両方に対応
- **自動更新** — Velopack による差分アップデート。メイン画面起動時に GitHub Releases を自動チェックし、新バージョンが見つかれば `VelopackUpdateDialog.Avalonia` のダイアログで案内（「ダウンロード＆インストール」「このバージョンをスキップ」を選択可能）。手動チェックは「バージョン」タブの「アップデート確認」ボタンから実行可能

## インストール方法

1. **[GitHub Releases](https://github.com/1llum1n4t1s/Lhamiel/releases)** から最新の `Setup.exe` をダウンロード
2. `Setup.exe` を実行してインストール
3. デスクトップやスタートメニューから Lhamiel を起動

## アンインストール方法

Windows の「設定」→「アプリ」→「インストールされているアプリ」から「Lhamiel」を選択し、「アンインストール」を実行してください。

## システム要件

| 項目 | 内容 |
|------|------|
| **OS** | Windows 10 / 11 (x64, ARM64) |
| **ランタイム** | 不要（ネイティブ AOT ビルド、.NET 10） |
| **権限** | 一般ユーザー権限で動作 |

## 使い方

### 圧縮する

1. Lhamiel を起動
2. 圧縮したいファイルやフォルダをウィンドウにドラッグ＆ドロップ
3. 自動的に圧縮が開始

> 「全般」設定の「圧縮形式」から ZIP / 7z / TAR を切り替えられます。
> 「圧縮」設定で、複数ファイルを1つのアーカイブにまとめるか、ディレクトリ構造（ルート含める / ルート含めない / フラット）をどうするかを指定できます。

### 展開する

1. アーカイブファイルを Lhamiel のウィンドウにドラッグ＆ドロップ
2. 設定に従い自動展開（「アーカイブ名でフォルダを作成する」ON時はアーカイブ名フォルダに展開）
3. パスワード保護アーカイブの場合は入力ダイアログが開くのでパスワードを入力

### 設定

設定画面はサイドバーレイアウトで構成されており、変更は即時適用されます。

| タブ | 内容 |
|------|------|
| **全般** | テーマ切替、言語選択、デフォルト圧縮形式、起動時にアップデートを確認 ON/OFF、ショートカット作成 |
| **展開** | 展開先ディレクトリ指定（同一階層 / 固定ディレクトリ）、展開後にフォルダを開く、アーカイブ名フォルダ作成ON/OFF |
| **圧縮** | 圧縮先ディレクトリ指定、圧縮後にフォルダを開く、まとめ圧縮、ディレクトリ構造モード、ZIP / 7z の圧縮レベル、Hidden/System 属性の対象化、除外リスト管理 |
| **関連付け** | ダブルクリックで Lhamiel を開く拡張子の選択（一括選択 / 一括解除に対応） |
| **バージョン** | バージョン情報・7-Zip ライブラリバージョン表示、アップデートチェック（VelopackUpdateDialog 経由）、スキップしたバージョンの取り消し、ご意見・ご要望リンク、ライセンス表示 |

## 対応形式

### 圧縮

- ZIP
- 7z（高圧縮）
- TAR

### 展開

- ZIP, 7z, TAR
- RAR, LZH, CAB, ARJ
- GZIP (.gz, .tgz), BZIP2 (.bz2, .tbz2, .tbz), XZ (.xz, .txz), LZMA (.lzma, .tlz)
- Z (.z, .tz)

## 対応言語

| 言語 | コード | ネイティブ名 |
|------|--------|-------------|
| 英語 | `en_US` | English |
| 日本語 | `ja_JP` | 日本語 |
| 中国語（簡体字） | `zh_CN` | 简体中文 |
| 中国語（繁体字） | `zh_TW` | 繁體中文 |
| ドイツ語 | `de_DE` | Deutsch |
| フランス語 | `fr_FR` | Français |
| スペイン語 | `es_ES` | Español |
| イタリア語 | `it_IT` | Italiano |
| ポルトガル語（ブラジル） | `pt_BR` | Português (Brasil) |
| ロシア語 | `ru_RU` | Русский |
| ウクライナ語 | `uk_UA` | Українська |
| インドネシア語 | `id_ID` | Bahasa Indonesia |
| タガログ語 | `fil_PH` | Filipino |
| タミル語 | `ta_IN` | தமிழ் |
| 韓国語 | `ko_KR` | 한국어 |
| ラテン語 | `la_VA` | Latina |
| サンスクリット語 | `sa_IN` | संस्कृतम् |

> 言語はシステムのロケールから自動検出されます。「全般」設定から手動で変更することも可能です。

## トラブルシューティング

- **展開できない場合** — アーカイブが破損している可能性があります。ダイアログで「スキップ」を試すと一部ファイルを取り出せる場合があります
- **「パスワードが必要、または不一致です」と表示される** — パスワード保護アーカイブです。入力ダイアログに正しいパスワードを入力してください。キャンセルすると展開は中止されます
- **関連付けが効かない** — 他のソフトが優先されている場合があります。「関連付け」タブで再設定するか、Windows の「既定のアプリ」を確認してください
- **ログファイルの場所** — `%LocalAppData%\Lhamiel\Lhamiel_yyyyMMdd.log`（ローリング保存、既定で 7 日間保持）
- **クラッシュダンプの場所** — `%LocalAppData%\Lhamiel\dumps\*.dmp`（未処理例外時に自動生成、最新 5 件まで保持）。サポート問い合わせ時に診断 ZIP に含まれる
- **診断情報の取得方法** — サポート問い合わせの際は「バージョン」設定タブの「診断 ZIP を出力」ボタンを使用してください。マスク済み設定・ログ・環境情報・MiniDump がまとめて ZIP 化されます
- **一時ファイルの自動削除** — アプリ起動時に `%TEMP%\Lhamiel_Temp_*` の 30 分以上前の残骸を自動で掃除します（前回クラッシュ時の中間ファイル等）
- **自動更新が失敗する場合** — Velopack 自動更新が動かない場合は、[GitHub Releases](https://github.com/1llum1n4t1s/Lhamiel/releases) から最新の `Lhamiel-win-Setup.exe` を手動ダウンロードして上書きインストールしてください
- **アップデートダイアログが起動毎に出る** — 「このバージョンをスキップ」を押すと該当タグが `settings.json` の `IgnoreUpdateTag` に保存され、次回以降の自動チェックではダイアログを表示しません。完全に無効化したい場合は「全般」設定の「起動時にアップデートを確認」を OFF にしてください
- **スキップしたバージョンを取り消したい** — 「バージョン」設定タブにスキップ中のバージョン情報と「スキップを取り消す」ボタンが表示されます。ボタンを押すと即座に取り消され、次回起動時から再びアップデート通知が出ます
- **手動でアップデートを確認したい** — 「バージョン」設定タブの「アップデート確認」ボタンを押すと、`VelopackUpdateDialog` ダイアログが開いて最新バージョンを確認できます（手動チェックは「このバージョンをスキップ」を無視して常に最新を表示します）

## 更新履歴

### v1.0.168 (2026-05-20)

- **🚨 自動更新の配信元を GitHub Releases → Cloudflare R2 に切替** — `Settings.UpdateBaseUrl` (`https://lhamiel.1llum1n4t1.com`、`[JsonIgnore]` + getter-only ハードコード固定) を `Velopack.Sources.SimpleWebSource` 経由で取得。CI workflow (`velopack-release.yml`) を `gh release create/upload` から `wrangler r2 object put` + 配信確認 `curl --fail` に書き換え (`actions/setup-node@v6.4.0` / `wrangler@4.92.0` バージョン固定、workflow level `permissions: contents: read` に最小化)。⚠️ **v1.0.167 以下のユーザーは旧 `GithubSource` クライアントなので自動更新が届きません**。手動で本リリース版を再インストールしてください
- **アップデート確認ボタンの自動無効化** — `App.UpdateCheckStateChanged` 静的イベントを追加し、`_isCheckingUpdate` フラグ遷移を `TryBeginUpdateCheck` / `EndUpdateCheck` ヘルパーで一元化。`MainWindowViewModel.IsCheckingUpdate` がイベントを購読することで、起動時自動チェック中も「アップデート確認」ボタンが disabled になる (並走実行を未然に防止)
- **Velopack 重複ダイアログ撤去** — 17 ロケールから `Text.Update.AlreadyChecking` キーを削除、`App.Check4Update` の `MessageService.ShowInfo("AlreadyChecking")` 呼び出しを撤去 (Velopack 自身のプログレスダイアログと表示が重複していたため)
- **テスト** — `SettingsTests` / `VelopackIntegrationAdversarialTests` を `UpdateBaseUrl` の JSON injection 防御 / 不変性テストに更新 (692/692 合格)
- **ドキュメント** — `docs/SETTINGS_SCHEMA.md` / `docs/ARCHITECTURE.md` / `CLAUDE.md` を R2 配信元に追従

### v1.0.167 (2026-05-18)

- **「アップデート確認」ボタンのサイレント failure を修正** — リポジトリ未設定 / 開発実行 (`IsInstalled=false`) / 既に確認中の 3 経路で UI フィードバックがなく「ボタンを押しても何も起きない」状態だったのを、それぞれメッセージダイアログで明示するよう修正。17 ロケールに `Text.Update.AlreadyChecking` を追加
- **プライバシー強化** — MiniDump の tier を `MiniDumpWithDataSegs + MiniDumpWithHandleData` (0x05) から `MiniDumpNormal` (0x00) に削減し、診断 ZIP 経由でグローバル変数 (Settings 内容) やファイルハンドル情報が漏れる経路を遮断。`PasswordDialog` も取得直後にダイアログ側 Password 参照を即クリア
- **信頼性向上** — `Logger._logger` 未初期化時の `LogException` で `%LocalAppData%\Lhamiel\Lhamiel_emergency.log` への直書きフォールバックを追加。Avalonia 起動失敗時にも例外情報を残せるように。`DiagnosticsCollector` も Logger 非同期クリーンアップとの race で `FileNotFoundException` / `DirectoryNotFoundException` が出ても警告を出さず無視する
- **パフォーマンス** — `MotwPropagator.PropagateToDirectory` を `Parallel.ForEach` で並列化 (CPU 数上限、最大 8)。`ArchiveExtractor.DetectExtractionConflicts` の stat 重複 (`File.Exists` + `new FileInfo`) を 1 回にまとめて I/O 半減。`IpcService` の固定 50ms リトライを指数バックオフ (50ms → 400ms) に変更
- **保守性** — `PartialExtractionHandler` の未使用 `[Obsolete] RetryExtraction` 削除。`AppPathResolverTests` を Windows 専用テスト 3 件に再構成。`IpcServiceTests` に `[Collection("Sequential")]` を付与してパイプ名衝突による flaky を解消
- **ドキュメント** — `SETTINGS_SCHEMA.md` に `Check4UpdatesOnStartup` / `IgnoreUpdateTag` を追加。`ARCHITECTURE.md` の Infrastructure / Utility レイヤー一覧を最新化

### v1.0.166 (2026-05-05)

- **Hidden/System 属性の圧縮対象化** — 圧縮時のファイル列挙で `.git` など Hidden/System 属性のファイル・フォルダを既定で含めるよう変更。設定から従来どおりスキップする挙動にも切り替え可能
- **圧縮除外リスト管理UIを追加** — 圧縮設定から除外パターンの追加・削除・既定値リセットが可能に。`.DS_Store`、`Thumbs.db`、`node_modules`、`__MACOSX` などを既定除外として管理
- **設定とドキュメント整備** — 17言語ローカライズ、設定スキーマ、AGENTS.md を実装に合わせて更新

### v1.0.164 (2026-04-29)

- **依存パッケージ更新** — Avalonia 12.0.1 → 12.0.2（バグ修正パッチ：OneWay バインディング修正、TabControl 高速切替クラッシュ修正等）、MessageBox.Avalonia 3.3.1.1 → 12.0.0（Avalonia 12 対応版、API 互換）、Microsoft.NET.Test.Sdk 18.4.0 → 18.5.1
- **確認ダイアログのデザイン統一** — サポートページ遷移時の確認ダイアログを `MessageBox.Avalonia` の既定デザインから、他のダイアログと同じアクリル背景のカスタムデザイン (`ConfirmDialog`) に統一

### v1.0.163 (2026-04-29)

- **ロケール辞書のロード方式を ResourceInclude 静的登録に戻す** — v1.0.162 までの「選択ロケールのみ `AvaloniaXamlLoader.Load` でオンデマンドロード」方式は Native AOT / compiled XAML 環境で辞書がビルド成果物に含まれない問題があったため、17 言語すべてを `App.axaml` の `ResourceInclude` として静的登録する方式に戻した。`MergedDictionaries` への投入は引き続き選択ロケールのみで、複数言語のキーが同時に有効化されることはない
- **`_localeCache` 撤去** — 上記方式変更に伴い `App.axaml.cs` のオンデマンドキャッシュ用 `Dictionary<string, IResourceProvider>` および try/catch 付きランタイムロード処理を削除。`Resources[localeKey]` 参照のみのシンプルな実装に
- **依存パッケージ更新** — `Microsoft.DotNet.ILCompiler` / `Microsoft.NET.ILLink.Tasks` を 10.0.6 → 10.0.7 に更新（Native AOT ツールチェーン）

### v1.0.162 (2026-04-25)

- **/rere 6 人分隊レビュー指摘 33 件を再適用 + 17 ラウンドのレビュー反映** — v1.0.161 ベースに対して PR #47 の 33 件を再提出（PR #48）し、CodeRabbit / Gemini / Codex の 17 ラウンド指摘を順次反映。主な変更:
  - **セキュリティ**: `IsSystemCriticalDirectory` のサブディレクトリ未保護バグ修正（`C:\Windows\System32\drivers` 等が settings.json 改竄経由で素通りしていた経路を `StartsWith` ベース + Subdir/Exact 2 段分割で塞いだ）/ Mutex（`Local\` プレフィックス）+ IPC パイプ名（`_S<SessionId>` 付与）+ `ActivateExistingInstance` のセッションスコープ統一 / NTFS ADS（`:`）拒否 / `--format` 引数の allow-list 検証 / CRDebugger.Avalonia をリリースバイナリから除外
  - **障害耐性**: Settings 破損時の段階的フォールバック（Move → Delete → 空 JSON 上書き）/ Logger 未初期化時のサイレント握りつぶし対策 / `PasswordDialog` の Cancel-前-Show Race 解消 / `FileConflictDialog` 二重 Closed 冪等化 / IPC タイムアウト逆転（`RequestReadTimeoutMs > ConnectTotalTimeoutMs`）解消
  - **設定永続化**: 出力先設定の `Directory.Exists` チェックを撤去し、未接続 NAS / USB ドライブが Desktop へサイレントリセットされる経路を解消
  - **CI**: `gh release create` に `--target ${{ github.sha }}` を明示（タグが既定ブランチ HEAD ではなく push 対象 commit に固定される）
  - **パフォーマンス**: `PathValidator` の重複コードを `ResolveNormalizedCandidates` / `IsAnyCandidateProtected` に共通化 / IPC リクエストの `Span<byte>` 直接 Deserialize / `ArrayPool` 経由バッファ管理 / `Parallel.ForEachAsync` への移行
  - **ドキュメント**: SETTINGS_SCHEMA.md / CLAUDE.md / AGENTS.md の整合性回復、サイレントスキップ xUnit を `Assert.SkipUnless` 化
- **テストカバレッジ強化** — `IsSystemCriticalDirectory` 直接テスト 11 件、Theme 正規化テスト 3 件、UpdateChannel canonical テスト 1 件等を追加し 555 → 568 件
- **総コミット数**: PR #48 で 17 ラウンドにわたる修正を 28 ファイル / +1713 / -264 行で squash merge

### v1.0.161 (2026-04-25)

- **v1.0.160 を取り下げ、v1.0.159 の状態で再リリース** — v1.0.160 で取り込んだ修正に不具合が確認されたため、当該変更を revert し、v1.0.159 と同等のコード状態に戻したうえで再リリース。v1.0.160 の GitHub Releases / タグは削除済み

### v1.0.159 (2026-04-22)

- **パスワード保護アーカイブ対応** — 暗号化 ZIP / 7z / RAR をドロップすると専用の入力ダイアログが開くように。誤入力時は再試行メッセージ付きで自動的にダイアログが再表示される
- **暗号化エラーの表示改善** — これまで「I/O error occurred.」と表示されていた `EncryptionException` を、ユーザーが理解できる「パスワードが必要、または不一致です」に分類して案内
- **パスワード入力キャンセル対応** — ダイアログで「キャンセル」を押した場合は通常のキャンセルフローとして扱い、誤って「パスワードが違います」と案内しないよう修正
- **ロケール拡張** — 17 言語に `Text.Password.*` / `Text.ErrorHandler.EncryptedOrWrongPassword*` の 7 キーを追加

### v1.0.158 (2026-04-20)

- **セキュリティ強化** — Zip Slip ガード、`UpdateRepoOwner/Name` のハードコード固定化、保護ディレクトリチェック、`IsProtectedDirectory` のシンボリックリンク追跡、`FolderOpener` の `ArgumentList` 化、IPC の `PipeOptions.CurrentUserOnly`、GitHub Actions の SHA 固定など P0/P1 系 25 件を反映
- **設定スナップショット** — `Settings.Snapshot` / `SettingsManager.CreateSnapshot` を導入し、圧縮・展開中に UI 側で設定を変更されても race しないよう根治
- **パフォーマンス改善** — `ShouldExcludeFile` の stackalloc 64 超え対応、`DetectFileSystemConflicts` の stat 回数削減、`DeduplicateByIdentity` の OS 依存コンパラ最適化、`FileIconHelper` のスレッドセーフなアイコンキャッシュ、`RefreshCompressionLevels` の差分更新化など 12 件
- **7z.dll 配置刷新** — CI での直ダウンロードを廃止し `1llum1n4t1s.Sevenzip` NuGet 同梱方式に一本化
- **TempCleanup 追加** — アプリ起動時に残存した `Lhamiel_Temp_*` ディレクトリを安全に掃除
- **ロケール追加キー** — 17 言語に `Error.ProtectedDirectory` / `Compressor.Processing` を追加
- **ドキュメント整合** — `SETTINGS_SCHEMA.md` / `ARCHITECTURE.md` を実装と同期
- **テスト** — 524 / 524 合格（`UpdateRepoOwner_IsHardcodedAndImmutable` 新規追加）

### v1.0.157 (2026-04-16)

- **依存パッケージ更新** — 1llum1n4t1s.Sevenzip 1.0.66、SuperLightLogger 1.0.6、CRDebugger.Avalonia 1.0.24 に更新。パフォーマンス改善とバグ修正を含む
- **コンパイル済みバインディング有効化** — メイン画面に `x:CompileBindings` を適用しバインディングパフォーマンスを向上
- **UI調整** — タイトルバーにバージョン表示を追加、アクリル背景の透明度を調整

### v1.0.152 (2026-04-11)

- **Avalonia 12 対応** — Avalonia UI を 11.3 から 12.0 にアップグレード。削除された `ExtendClientAreaChromeHints` プロパティを除去
- **圧縮時の一時コピー方式を最適化** — 全ファイルを事前コピーする方式を廃止し、ライブラリ（1llum1n4t1s.Sevenzip v1.0.52）側でロック中ファイルのみ自動コピーする方式に変更。ディスク使用量と処理時間を大幅に削減

### v1.0.150 (2026-04-11)

- **展開後にフォルダを開く機能** — 展開完了後にアーカイブ名フォルダをエクスプローラーで自動的に開く機能を追加。二重ネスト防止スキップ時もルートフォルダを正しく開くよう対応
- **展開後に開くフォルダのバグ修正** — 「展開後にフォルダを開く」設定ON時にアーカイブ名フォルダではなく親ディレクトリが開かれるバグを修正
- **圧縮コピーの I/O 最適化** — 圧縮前の一時コピー処理でバッファサイズ最適化、`CopyToAsync` を手動ループに置き換えてスナップショット一貫性を保証
- **圧縮・展開パフォーマンス改善** — 圧縮・展開処理全体のパフォーマンスを最適化
- **TOCTOU レース修正** — 圧縮時のゼロバイトファイル判定における競合状態を修正
- **CI ビルド安定性向上** — 7z.dll ダウンロードにリトライ処理を追加し、タイムアウトによるビルド失敗を防止

### v1.0.144 (2026-04-02)

- **展開ロジックの簡略化** — スマート展開の複雑なヒューリスティック判定を除去。「アーカイブ名でフォルダを作成する」設定に基づくシンプルな ON/OFF 方式に変更。二重ネスト防止はアーカイブのルートフォルダ名とアーカイブ名の一致判定のみで実現
- **ロック中ファイルの圧縮対応** — 圧縮前に全ファイルを一時コピーすることで、プロセスがロック中のファイルも圧縮可能に
- **進捗ダイアログの改善** — 進捗表示の精度向上とUI改善
- **全ハードコード文字列のローカライズ** — コード内に残っていたハードコード文字列を全てローカライズリソースに移行
- **複合アーカイブ拡張子の正規化** — `.tar.gz` / `.tar.xz` 等の複合コンテナ拡張子を正しく処理してフォルダ名を決定
- **空ディレクトリの圧縮修正** — 空ディレクトリが圧縮時にアーカイブに含まれないバグを修正
- **展開時のバグ修正** — 一時フォルダ方式の展開における衝突検出・上書き確認の不具合を複数修正

### v1.0.130 (2026-03-25)

- **ファイル衝突ダイアログ** — 展開・圧縮時に同名ファイルが競合した場合、Windows風のファイル比較ダイアログで選択的に処理。サムネイル表示・一括チェック・同一ファイルスキップ機能を搭載
- **展開オプション追加** — 「アーカイブ名でフォルダを作成する」設定を追加。OFFにするとアーカイブ内容を直接展開先に配置
- **圧縮オプション追加** — ディレクトリ構造モード（ルートディレクトリを含める / 含めない / フラット）を選択可能に
- **ディスク容量チェック** — 展開・圧縮前に空き容量を事前チェック。処理中も定期監視し、容量不足時は一時停止して対応可能
- **アクリルブラー効果** — 全ダイアログにアクリルブラー背景を適用
- **ARM64対応** — Windows ARM64ビルドを追加

### v1.0.118 (2026-03-19)

- **ローカライズ不具合の修正** — アプリ起動時にロケール辞書が適用されず、全UIにリソースキーがそのまま表示される問題を修正
- **バージョンタブに7-Zipバージョン表示** — 設定画面のバージョンタブで使用中の7-Zipライブラリバージョンを表示
- **ファイル関連付けの即時反映** — ファイル関連付けの変更が即座にシステムに適用されるよう修正
- **ライセンス参照の修正** — ライセンス表示のリンク先を修正
- **アクセントカラーオーバーレイ追加** — UIにアクセントカラーのオーバーレイ効果を追加

### v1.0.106 (2026-03-16)

- **デザインリニューアル** — サイドバーにアクリル風マテリアルを適用
- **テーマ切替** — ダーク / ライト / システム追従の3モード対応
- **17言語対応** — ラテン語、サンスクリット語を追加
- **複数ファイルまとめ圧縮** — 複数ファイルを1つのアーカイブにまとめる機能を追加
- **複数ファイル処理のバグ修正** — 複数ファイル指定時にアーカイブファイルが誤って展開される問題を修正

### v1.0.90 (2026-03-07)

- コード品質・パフォーマンス改善

### v1.0.80 (2026-02-18)

- 上書き確認ダイアログの不具合修正
- 更新確認ボタンの追加

### v1.0.70 (2026-02-10)

- ネイティブ AOT ビルド対応
- アプリ更新プロセスの改善

### v1.0.60 (2026-02-06)

- パフォーマンス改善
- `.tz` ファイル未認識、並列圧縮の進捗計算、設定読み込みのバグ修正

### v1.0.50 (2026-02-03)

- UI 改善
- README 整備

### v1.0.0 (2026-02-02)

- 初回リリース
- ドラッグ＆ドロップによる圧縮・展開
- スマート展開（二重フォルダ防止）
- エラー回復機能
- ファイル関連付け
- 自動更新（Velopack）

## 連絡先

- **メール:** 1llum1n4t1@duck.com
- **バグ報告・要望:** https://github.com/1llum1n4t1s/Lhamiel/issues

## ライセンス

Lhamiel は MIT License の下で公開されています。

Copyright (c) 2025-2026 ゆろち

詳細は [LICENSE](LICENSE) ファイルをご参照ください。
