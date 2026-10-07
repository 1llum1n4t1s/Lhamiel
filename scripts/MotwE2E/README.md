# MotW列挙中の取消検証

製品Release build後、`dotnet run --project scripts/MotwE2E/Probe.csproj -c Release -- <未使用の成果物絶対パス>` を実行する。
5万個の無害な空ファイルを一フォルダーへ生成し、15ms後に取消を要求する。取消の観測、通知から復帰までの時間、ADS書込み件数をresult.jsonへ記録する。
内部の位相hookは使用しないため、取消の列挙中到達は無取消列挙時間とADS未書込みからの推定として扱う。
ソースとresult.jsonを保持し、生成dataフォルダーだけを共通ごみ箱helperで清掃する。
