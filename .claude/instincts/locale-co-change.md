---
id: lhamiel-locale-co-change
trigger: "when adding or modifying localization keys in any .axaml locale file"
confidence: 0.95
domain: localization
source: local-repo-analysis
---

# 全17ロケールファイルを同時に更新する

## Action
ローカライズキーを追加・変更する際は、必ず `Resources/Locales/` 配下の全17個の `.axaml` ファイルを同時に更新する。

## Evidence
- 200コミット中、ロケールファイルの変更は常に全17ファイル同時に行われている
- 1ファイルだけ変更して他を忘れるとランタイムエラーの原因になる

## Files
`de_DE`, `en_US`, `es_ES`, `fil_PH`, `fr_FR`, `id_ID`, `it_IT`, `ja_JP`, `ko_KR`, `la_VA`, `pt_BR`, `ru_RU`, `sa_IN`, `ta_IN`, `uk_UA`, `zh_CN`, `zh_TW`
