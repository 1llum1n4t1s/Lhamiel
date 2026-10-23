# 衝突画面の実 UI 検証

製品を Release build 後、`dotnet build scripts/ConflictUiE2E/Probe.csproj -c Release -o scripts/ConflictUiE2E/bin/e2e` を実行する。
別の製品・probe が動いていない状態で 環境変数 `LHAMIEL_CONFLICT_TEST_ROOT` を未使用のfixture絶対パスに設定し、`Lhamiel.Tests.Unit.exe --ui-review` を起動する。
実画面の UI Automation で左右と列の名前、選択状態を確認し、fixture 内に `locale.signal` を作成すると英語へ変更する。
`compression.signal` で圧縮モード、`finish.signal` で空一覧を確認して終了する。
各段階の名前・選択状態と result.json を保持する。ユーザーの設定は変更しない。

依存 DLL・アイコン・ネイティブ資産の一覧は [E2E.Assets.props](../E2E.Assets.props) で共通管理します。既定は本体の Release / win-x64 出力です。別のビルド出力は `-p:Assets=<絶対パス>` で指定でき、従来の `-p:AssetDir=<絶対パス>` も使用できます。両方の指定時は `Assets` を優先します。
