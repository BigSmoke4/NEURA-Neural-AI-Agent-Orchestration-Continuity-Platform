# Vendored browser dependencies

These small, prebuilt browser assets are copied from exact npm package releases
so application scripts are same-origin and can run under the Content Security
Policy. Update only through a reviewed dependency bump, retain the upstream
license/notice files, and refresh the SHA-256 values below.

| Asset | Source | License | Asset SHA-256 |
|---|---|---|---|
| `cytoscape/cytoscape.min.js` | `cytoscape@3.34.3` (`dist/cytoscape.min.js`) | MIT; `cytoscape/LICENSE` | `5f3b5b529546d5af1fc5628590af033b74511a5b6f789f5f4682845863228b91` |
| `microsoft-signalr/signalr.min.js` | `@microsoft/signalr@8.0.29` (`dist/browser/signalr.min.js`) | MIT; `microsoft-signalr/LICENSE` and `THIRD-PARTY-NOTICES.txt` | `e7e61716d975e291b64ac904ce5ed49fea3b6290c740f33e6b26ebab327f343c` |

The npm tarball SRI values at vendoring time were:

- `cytoscape@3.34.3`: `sha512-yfYGhRcGAntq6YBD583j4n0Eg3jIxvWmZtz/5uz9UYkeIStSlMxuUja+ec5j3iBD8nv1rwaOAYMW09tBdkSeaQ==`
- `@microsoft/signalr@8.0.29`: `sha512-yzszFiTAuRxHm3qxNaSan3YzN8dffLqQcc3ZNJD2tn9eInCBBi7u2xCJ6AVNXjw19RDoBc+NPWfYN43K1HcjdA==`

`npm audit` against these exact package versions on 2026-10-12 reported zero
advisories. Re-run dependency and license checks for every vendor update and
release; this narrow check is not a full application SBOM.
