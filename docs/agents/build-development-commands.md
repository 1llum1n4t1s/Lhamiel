# Build & Development Commands

```bash
# Build (x64, デフォルト)
dotnet build Lhamiel.slnx -c Debug
dotnet build Lhamiel.slnx -c Release

# Build (ARM64)
dotnet build src/Lhamiel/Lhamiel.csproj -c Debug -p:RuntimeIdentifier=win-arm64 -p:PlatformTarget=ARM64

# Run tests (xUnit 4)
dotnet test Lhamiel.slnx -c Release

# 追跡対象 packages.lock.json を既定の x64 / NuGet パッケージ構成で更新・検証
dotnet restore Lhamiel.slnx -p:KagayoiSupportClientUsePackage=true --force-evaluate
dotnet restore Lhamiel.slnx -p:KagayoiSupportClientUsePackage=true --locked-mode

# Run a single test
dotnet test Lhamiel.slnx --filter "FullyQualifiedName~TestMethodName"

# ネイティブシェルの選択リスト・登録照合テスト（VS Developer PowerShell）
msbuild src/Lhamiel.ShellExtension/ShellExtension.Tests.vcxproj /p:Configuration=Release /p:Platform=x64
./src/Lhamiel.ShellExtension/bin/x64/Release/Lhamiel.ShellExtension.Tests.exe

# Publish native AOT executable (x64)
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-x64 -p:OS=Windows_NT

# Publish native AOT executable (ARM64)
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-arm64 -p:PlatformTarget=ARM64 -p:OS=Windows_NT

# Run the app
dotnet run --project src/Lhamiel/Lhamiel.csproj

# 署名付きローカルインストーラー作成（配信なし。Sigilune の導入・登録が必要）
pwsh scripts/release-local.ps1 -SkipUpload -Runtimes win-x64 -WorkDirectory <空の絶対Tempパス>
# → 指定作業ルートの packages/Lhamiel-win-Setup.exe が生成される
```

> **Note**: Native AOT ビルド（`dotnet publish`）は VS の C++ ツールチェーン（`vswhere.exe`）が必要。ローカルテストでは `-p:PublishAot=false --self-contained` を使う。

Solution file: `Lhamiel.slnx` (VS 2026 format). **x64 / ARM64** 両対応。`TreatWarningsAsErrors` が ON — 警告は全てビルドエラーになる。

**SDK の固定**: リポジトリ直下の `global.json` が .NET SDK を `rollForward: disable` で厳密に固定し、CI も `setup-dotnet` の `global-json-file` で同じ SDK を使う。SDK の patch は Native AOT の暗黙 `Microsoft.DotNet.ILCompiler` 版を変え、追跡対象 lockfile の locked restore を NU1004 で失敗させるため、SDK を上げるときは同時に上記の package-mode restore 2 コマンドで lockfile を再生成する。製品の TargetFramework（`net11.0-windows8.0`）は SDK と同じメジャーなので、テストホスト用の追加ランタイムは不要。CI のテスト job は locked restore を独立ステップで実行してから `--no-restore` でテストする（RID 付き publish には locked-mode を掛けない）。

ARM64 の build / publish 後は、上記の `KagayoiSupportClientUsePackage=true` を指定した restore 2 コマンドで追跡対象 lockfile を更新・検証し、`git diff -- src/Lhamiel/packages.lock.json` で RID が既定の `win-x64` に戻ったことと意図しない依存変更がないことを確認する。`release-local.ps1` 終端の restore は、兄弟 SDK が存在する環境ではローカル用 lockfile だけを更新するため、追跡対象の検証を代替しない。復元モードと分離の理由は [DESIGN.md](../design/adopted-design-decisions-and-trade-offs.md#shared-support-sdk-with-two-resolution-modes) を参照する。
