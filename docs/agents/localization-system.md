# Localization System

**17 languages** via Avalonia ResourceDictionary (XAML-based, not .resx).

```csharp
// C# — note: auto-prepends "Text." to the key
var msg = App.Text("Error.DuringExtraction", ex.Message);  // looks up "Text.Error.DuringExtraction"
```

```xml
<!-- XAML — use DynamicResource (not StaticResource) for runtime locale switching -->
<TextBlock Text="{DynamicResource Text.DropZone.Message}" />
```

Adding a new locale: create `Resources/Locales/{xx_YY}.axaml` → add `ResourceInclude` in `App.axaml` → add to `App.SupportedLocales` + `App.LocaleDisplayNames` (`MainWindowViewModel.LocaleOptions` derives from these)

**Pitfall**: `ResourceInclude` implements `IResourceProvider`, NOT `ResourceDictionary` — type casts must use `IResourceProvider`.
