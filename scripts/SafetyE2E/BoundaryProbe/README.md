# AB-01 宛先境界の隔離 E2E

製品の Release DLL の最終配置・退避・復元・部分コピーを直接呼ぶ headless probe。
本体 UI、圧縮エンジン、実ユーザー設定、外部サービスは使わない。
単体テスト追加ではなく、NTFS 上の実ファイルと専用 junction を使う再現確認。

親が製品 Release build を終えた後に同じコマンドの phase を切り替える。

```powershell
dotnet run --project scripts/SafetyE2E/BoundaryProbe/BoundaryProbe.csproj -- before
dotnet run --project scripts/SafetyE2E/BoundaryProbe/BoundaryProbe.csproj -- after
```

- `before` は欠陥条件の成立と正常系を期待する。`after` は拒否・dummy境界外不変と正常系を期待する。
- JSON は `.codex/gogo-rere-20261007/evidence/boundary-{phase}-result.json`。製品 DLL の SHA256 と各ケースを残す。
- dummy fixture は同 evidence 内の `boundary-{phase}-<guid>`。ログもその配下へ隔離する。
- file symlink の作成権限がない場合はそのケースを `unverified` と記録する。成功扱いにはしない。
- probe 自身は fixture を削除しない。記録された junction leaf を共通 recyclehelper の `-AllowLeafJunction` で個別に処理し、リンクを残していないことを確認してから fixture root を同 helper で処理する。
- 清掃コマンドは `pwsh -NoProfile -STA -File scripts/SafetyE2E/BoundaryProbe/Cleanup.ps1 -Phase before`（後確認は `after`）。記録を終えた fixture の junction と root だけを処理し、結果JSON・台帳・probe出力を保持する。非junctionのリンクを発見したら保持して停止する。
- 外部プロセスによる検査後のリンク差替えへの原子的保護は検証しない。

確認範囲: merge の新規/上書き/空dir、合法 root/上位link、通常上書き/skip、深い overwriteCheckPaths の退避、部分展開コピー、祖先linkがある復元/退避破棄。
