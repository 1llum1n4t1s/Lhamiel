# 圧縮出力台帳の大量計画 E2E

バッチ全体の N 計画を保持しながら、各項目の登録・走査開始・走査終了・解除を全 N 回実行します。失敗条件は、大量計画の先頭・末尾の除外欠落、走査中の完了出力の除外喪失、新走査での完成書庫の過剰除外、取消の受付、lease/payload の漏れ、旧形式の稼働登録の移行、旧 reader が新登録を読めないこと、破損状態の上書きです。

本番の `CompressionOutputRegistry.cs` を source link します。通常の依存 DLL は `dotnet build Lhamiel.slnx -c Release` の出力を使うため、単独 clone でも after を実行できます。以下の変更前後比較はローカルに保存した固定 baseline を `Assets` で明示します。保存先だけは可変 Settings fixture へ置換します。本物の readonly 設定を UnsafeAccessor で書き換える方法は、長時間実行時の tiered JIT との衝突が疑われるため使いません。UI・圧縮本体は起動しません。新形式は現在の metadata gate を source link し、旧形式の比較は baseline DLL の旧 gate を使います。両 metadata gate は同じ lexical mutex ID です。

```powershell
dotnet run --project scripts/CompressionRegistryScaleE2E/Probe.csproj -c Release -p:NoWarn=CS0436 -p:Assets=C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/baseline -- C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/scale-fixture-final after
dotnet run --project scripts/CompressionRegistryScaleE2E/Probe.csproj -c Release -p:NoWarn=CS0436 -p:Assets=C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/baseline -p:UseCurrentMetadataGate=false -p:RegistrySource=C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/backups/registry/CompressionOutputRegistry.cs -- C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/scale-fixture-final before
```

`result-before.json` / `result-after.json` は同じ入力パス・fixture hash・.NET runtime・baseline DLL SHA・未設定の設定条件を記録します。走査を一度終了して開始時台帳を空に揃えます。metadata の実ファイル長を各操作前後に加算し、不変 payload の wire byte 数は一度だけ加算します。OS の process IO transfer count と照合できます。これは論理転送量で、物理ディスク書込み量ではありません。経過時間には mutex・lease・列挙・canonical key 解決も含みます。

after は通常・走査途中の登録/完了・取消・別 process の退出と残骸・v1 の稼働継続・v1 空/残骸の移行・中断で残った partial payload の再確定・cache を持たない新 process での移行済み残骸の読込み・未知形式/負件数/途切れ/余剰データ/欠損 payload の拒否を実行します。完成書庫の明示ファイルとしての選択と TempCleanup の実圧縮経路は、統合 E2E 側で検証します。

試験が作る一時ディレクトリは引数 Root の内側だけです。`fixture/crash` と `fixture/v1-residue` は退出/移行検証の残骸、`after/corrupt-*` は破損拒否の原本です。結果・バックアップは証拠として残します。用途終了後は Windows の共通ごみ箱ヘルパーで Root 内の対象だけを清掃します。

## 長寿命走査と既存ディレクトリの追加検証

大量バッチの登録を保った一つの走査に、N=100/1,000 の単体要求を登録・完了します。最後の除外集合に通常ファイル F=10,000 を照合して、集合数・時間・大量バッチ集合の参照同一性を記録します。小さな履歴はまとめても、大きな cached set はコピーしません。台帳の履歴 metadata 更新時間は別に記録し、ファイル照合時間と区別します。多数の大規模要求の集合数はその登録数に依存します。

```powershell
dotnet run --project scripts/CompressionRegistryScaleE2E/Probe.csproj -c Release -p:NoWarn=CS0436 -p:RegistrySource=C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/backups/registry-followup/CompressionOutputRegistry.cs -- C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/scale-followup-20261007-1810 followup-before
dotnet run --project scripts/CompressionRegistryScaleE2E/Probe.csproj -c Release -p:NoWarn=CS0436 -- C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/scale-followup-20261007-1810 followup-after
```

こちらの before/after はどちらも schema 2 と現在の lexical metadata gate を使い、追加修正二点だけを比較します。既存の `archive-00001.zip` ディレクトリでも、隣にある staging/backup が登録されることを確認します。v1 の残骸・中断 payload の再移行と cache のない別 process 読込みも両側で確認します。`result-followup-before.json` / `result-followup-after.json` に結果を保存します。

## 出力 leaf のディレクトリリンク

`Plan.Create` は、圧縮の自動生成出力名が既存の Directory + ReparsePoint の場合だけ拒否します。そのリンクを置換すると実体キーがリンク先から親 + leaf に変わるためです。通常の既存ディレクトリ、利用者が選択した親/祖先のリンク、leaf がファイルの symlink/hardlink は許可します。ユーザー向けエラーの17ロケールと実圧縮経路の exit code は統合側が確認します。

```powershell
dotnet run --project scripts/CompressionRegistryScaleE2E/Probe.csproj -c Release -p:NoWarn=CS0436 -- C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/scale-slotguard-20261007-1830 slotguard-after
```

この追加 fixture は leaf の junction、parent の junction、file の symlink/hardlink を作ります。junction/symlink を含むため、共通清掃ヘルパーで扱えない範囲を直接削除せず、fixture の絶対パスを保持して親へ引き継ぎます。
