---
id: lhamiel-aot-safe-patterns
trigger: "when writing new code, adding dependencies, or using serialization"
confidence: 0.9
domain: runtime
source: local-repo-analysis
---

# Native AOT セーフなパターンを使用する

## Action
- リフレクションベースのパターンを避ける
- `System.Text.Json` は `AppJsonContext.cs` のソースジェネレーターを使用する
- 新しいシリアライズ対象の型は `AppJsonContext` に追加する
- Avalonia の ComboBox アイテムには文字列やプリミティブではなく record 型を使用する

## Evidence
- AOT対応のための大規模リファクタリングが実施済み (コミット: `AOT対応`, `テーマComboBoxをAOT安全なThemeItem recordに変更`)
- `PublishAot=true` が有効化されている
- `TrimmerRoots.xml` でメインアセンブリを保持
