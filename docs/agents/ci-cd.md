# CI/CD

- **PR・main の検証**: `.github/workflows/dotnet-build.yml` は main への push・全 PR・手動で動き、build（再利用 `build.yml`）と test（再利用 `test.yml`、restore・Release テスト・カバレッジ）を並列に実行する。互いの成果物を使わないため test に `needs: [build]` を戻さない。同じ ref の古い実行は `concurrency` で打ち切る。`test.yml` に `concurrency` を置かない（呼び出し側の group と衝突して待ち合わせになる）。
- **配信前のテスト**: `release.yml` は `test.yml` を `test` job として先に呼び、`release` job は `needs: test` で署名・R2 配信より前にテストが通ることを要求する。失敗したら配信しない。
- 製品ページの配信は兄弟リポジトリの `../Web/deploy/deploy-lp.ps1` を使う。公開ホスト・更新ファイルの既存経路を維持する。
- **RID 別リリース出力**: `release-local.ps1` の x64 / ARM64 `dotnet publish` は RID ごとの `--artifacts-path` を必須とし、ProjectReference 先を含む共通 `bin/obj` の再利用による異アーキテクチャ参照（CS8012）を防ぐ。
- **リリースの再開**: リポジトリルートで `pwsh scripts/release-local.ps1 -ReusePublish -WorkDirectory <既存の絶対Tempパス>` を使うと、指定作業ルートの `publish-<RID>/` を検証して再パッケージし、exe がない RID はビルドする。配信だけ再開する場合は同じ作業ルートを `pwsh scripts/release-local.ps1 -UploadOnly -WorkDirectory <既存の絶対Tempパス>` に渡す。通常実行は空の作業ルートを要求し、省略時は作成先を表示する。再開時に `-WorkDirectory` は必須。`-Runtimes win-x64` / `win-arm64` で対象を限定し、`-SkipUpload` は配信前で終了する。保存場所・検証条件・再開の境界は[再開契約](commands.md)を参照する。
- **ネイティブ配布物**: `.github/workflows/build.yml` と `scripts/release-local.ps1` の両 RID の publish 検査では、`Lhamiel.exe`、`7z.dll`、`unlhare.dll`、`licenses/Kagayoi.UnLhaRe/LICENSE`、`licenses/Kagayoi.UnLhaRe/THIRD_PARTY_NOTICES.md` の存在確認を維持する。UnLhaRe 更新時は `src/Lhamiel/Lhamiel.csproj` の `UnLhaReVersion` とパッケージ構成の lockfile を揃え、DLL の手動コピー経路を作らない。通常の依存更新でも locked restore と Release テストを維持する。
- **公開完了判定**: [DESIGN.md の配信フロー](../design/release-process.md#update-and-release) に従い、公開前検証と R2 書込み・固定 URL 全件のパージ API の受付成功を確認する。公開後の配信内容取得・ハッシュ照合・CDN 伝播待ちは行わない。
- **CodeQL**: `.github/workflows/codeql.yml` — C# security analysis on PR + weekly
- **Dependabot**: `.github/dependabot.yml` — NuGet weekly + github-actions weekly。`.github/workflows/dependabot-auto-merge.yml` は全リポジトリ共通テンプレートで、`dotnet-build.yml` の成功・他の check / status・依存ファイルだけの差分を確認した Dependabot PR をマージする。PR のコードは実行せず、判定は読み取り権限の `verify` job、書き込み権限はマージだけを行う `merge` job に限る
- **NuGet キャッシュ**: `build.yml` / `test.yml` / `codeql.yml` は `~/.nuget/packages` を復元するが、タグと `release/` ブランチでは復元せず、保存は main だけで行う。配信の経路をキャッシュに依存させない

## リリースの実行

版タグの push で `release.yml` が公開する。実行・ローカル検証・署名失敗時の扱いは[リリース手順](release-process.md#リリースの実行)を参照する。
