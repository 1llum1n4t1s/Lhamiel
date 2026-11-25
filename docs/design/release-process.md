# リリースと配信

## Update and release

1. Installed clients obtain `releases.win.json` or `releases.win-arm64.json` from `https://lhamiel.kagayoi.com`.
2. `scripts/release-local.ps1` builds both Native AOT RIDs in isolated artifact trees, builds the native shell integration, packages with Velopack, and Authenticode-signs all distributed executables with the Certum SimplySign cloud key through Sigilune. Sigilune verifies validity, signer and RFC 3161 timestamp after each signature. Isolation includes project references.
3. The same script uploads immutable versioned packages and fixed-name manifests/installers to R2, then purges every non-`.nupkg` URL. Release completion requires successful upload and purge API acceptance, without downloading public artifacts, rechecking hashes, or waiting for CDN propagation. Cleanup preserves manifest-referenced files, fixed names, and the latest two versions of versioned artifacts. References include local and all bucket manifests read through the authenticated R2 API, protecting other RIDs during partial releases. If any bucket manifest cannot be fetched or parsed, deletion is skipped with a warning and the already-published release still succeeds.
   The silent update (`--update-check`) checks and downloads without holding the UI single-instance mutex, so the UI can start during the download. Velopack's startup auto-apply is disabled (`SetAutoApplyOnStartup(false)`), because applying runs Update.exe, which force-terminates every process in the install folder. Only the silent update and the update screen apply a downloaded package, and the silent update takes the mutex immediately before applying and skips it when another Lhamiel process (UI or worker) is running. `ApplyUpdatesAndExit` terminates the process immediately, so the mutex only guards the decision to start applying: a UI or worker launched during the swap is not prevented and may be terminated by Update.exe. A package that was skipped is applied at the next silent update or update-screen action, not at the next launch.
4. The [product landing page](#製品ページの配信先) is deployed to the VPS independently of desktop releases. The Cloudflare gateway preserves the existing update route.

## Local signed releases to R2

Signing uses the owner's Certum account through Sigilune without SimplySign Desktop. `release.yml` runs `release-local.ps1` for version tags; its `release` Environment holds the signing and Cloudflare secrets, making that environment part of the signing boundary. Only this workflow reads them, and its session is logged out on exit. Workstation runs use Credential Manager and `secrets.json` for verification and resumption, making the workstation another boundary. Old GitHub Releases remain only as a legacy migration bridge.

Work directories, resumption and reuse validation follow the [release resume contract](../agents/commands.md).

## 製品ページの配信先

製品ページの配信HTMLは兄弟リポジトリ `../Web/lp/lhamiel/`（編集元は `../Web/tools/lp/templates/`）、公開実体はVPSの `/srv/www/lp/lhamiel/`。
Cloudflare側の中継設定は `../Web/deploy/lp-gateways/lhamiel/` に置く。
公開URLと既存のR2・ライセンス通信を維持する中継構成で、配信経路は `../Web/deploy/deploy-lp.ps1`。実行時の規約は [AGENTS.md](../agents/ci-cd.md#cicd) を参照する。
