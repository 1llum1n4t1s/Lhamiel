# 圧縮出力台帳の大量計画・破損・追跡清掃 E2E

既存 Release DLL の `Settings` / `TempCleanup` 等と、現在の `CompressionOutputRegistry.cs` をリンクして使います。製品 build と GUI は起動しません。`CS0436` は台帳のリンク実装と既存 DLL 内の同名実装の衝突だけを抑制する指定です。

```powershell
dotnet run --project scripts/CompressionRegistryE2E/Probe.csproj -c Release -p:NoWarn=CS0436 -- '.codex/gogo-grok-parallel-20261007/registry-count-after' --expect-fixed
```

指定したディレクトリは新規のものを使ってください。設定、登録台帳、出力候補、破損 fixture と `result.json` を指定先だけに保存します。正常な登録を 33,334 計画 / 100,002 パスで実行し、走査開始・終了、登録解除、次の小さな登録を通して、台帳が空の 12 byte に戻ることを確認します。

先に固定した失敗モードは、writer が正常保存した件数を reader の固定上限が拒否して後続操作まで永続的に失敗すること、壊れた負数または実データのない件数を受け入れること、未完結文字列を受け入れること、新しい staging 名が TempCleanup に認識されず追跡清掃が残ることです。破損 fixture は拒否と元バイトの維持を SHA256 で確認します。

追跡清掃は隔離した設定台帳と未来時刻を `TempCleanup.CleanupTrackedDirectories` に渡し、新しい `Lhamiel_Temp_GUID` staging が一件だけ回収されることを確認します。通常のユーザー設定と外部サービスは操作しません。

修正前の結果は `.codex/gogo-grok-parallel-20261007/registry-count-before/result.json`、修正後は `registry-count-after/result.json` に保存しています。修正前の source は `.codex/gogo-grok-parallel-20261007/backups/inputs/registry-count/src/Lhamiel/Util/CompressionOutputRegistry.cs` に保持しています。

before の実行時は同じ helper に `--expect-broken` を渡し、正常保存後の `BeginScan`・`Dispose`・`NextSmallRegister` が全て `InvalidDataException` となることを確認しました。検証成果物は保持し、不要になったときはグローバルのごみ箱対応 cleanup helper で今回の指定ディレクトリだけを清掃します。
