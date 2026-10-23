# 終端例外の診断ログ検証

先に `dotnet build Lhamiel.slnx -c Release` を実行し、その成果物を参照して検証します。

```powershell
dotnet run --project scripts/DiagnosticsE2E/Probe.csproj -c Release -- C:\Users\IMT\dev\Lhamiel\.codex\gogo-rere-20261007\diagnostics-result
```

最後の引数は未使用の成果物ディレクトリです。ログと `result.json` を保持します。
実クラッシュ・自己ダンプは発生させず、製品の終端ハンドラーへ dump 失敗を注入します。
通常 Logger の Dispose 前にファイルを読み、要約の先行保存、型・HResult・スタックの保持、
外側・内側のダミーメッセージ非保存を確認します。GUI・大量ファイルの MotW 取消は対象外です。

依存 DLL・アイコン・ネイティブ資産の一覧は [E2E.Assets.props](../E2E.Assets.props) で共通管理します。既定は本体の Release / win-x64 出力です。別のビルド出力は `-p:Assets=<絶対パス>` で指定でき、従来の `-p:AssetDir=<絶対パス>` も使用できます。両方の指定時は `Assets` を優先します。
