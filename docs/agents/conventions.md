# UI 実装規約

- **タスクバーのアプリ識別子** — 未パッケージの通常起動は、ショートカット経由に限らず実行ファイルの配置先から識別子を解決する。変更時は [TaskbarIdentity の不変条件](../design/critical-invariants.md#critical-invariants)に従い、`TaskbarIdentityTests` / `AppIconVariantTests` / `ProgramLifecycleTests` で両経路の一致・別配置の隔離・リンクの引数維持・再書込抑止を確認する。
- **Avalonia 12, not WPF** — compiled bindings (`x:CompileBindings="True"`), FluentTheme. `ExtendClientAreaChromeHints` は削除済み → `WindowDecorations` を使う
- **設定画面のカラーアイコン** — [DESIGN.md](../design/adopted-design-decisions-and-trade-offs.md#settings-navigation-assets) のリソース構成に従い、サイドバーと本文見出しで同じ画像を使う。画像を更新するときは Avalonia リソース登録とライセンスの埋込み・表示も維持する。
- **Acrylic blur / 可読性レイヤー** — 全ダイアログで `ExperimentalAcrylicBorder` + `ExtendClientAreaToDecorationsHint`。白いデスクトップ背景が透けても文字コントラストを保つため、root `Panel` の子は「アクリル境界 → `Brush.Window` / `Opacity=0.5` / `IsHitTestVisible=false` のスクリム → コンテンツ」の順を維持する（`AcrylicFallbackHelper` が直接のアクリル子を探索する契約も守る）。補助文字はテーマ別 `FG2` を使い、個別ウィンドウで薄い `TintOpacity` を再導入しない
