# Retained third-party legal material

Package-prefixed license and notice files are copied verbatim from the pinned
NuGet package cache. `package-inventory.json` is generated from the resolved
application, storage/service/UI tests, foundation tests and isolated
dependency-probe/test assets and the
corresponding NuGet metadata.
It includes build/test-only and non-target assets; it is not a claim that every
listed package is shipped in every output.

Where a package declares a license expression without embedding a copyright
notice, the upstream license is retained:

- Avalonia: repository commit `2d79ba55fae1b1e71c69f93a668080c9029e6a31`,
  `licence.md`.
- Avalonia.Controls.DataGrid 12.1.2: repository commit
  `1e12211eb0c52a09e314ca57a344147c37d0a26c`; its MIT license matches
  the retained Avalonia text. Avalonia.Headless 12.1.4 is test-only.
- MicroCom: repository commit `76785efcafd91b5902fd19dd11145f6dd655b7b4`,
  `LICENSE`.
- Tmds.DBus: repository commit `b4a7fed0b878f74cb54f7cca84d2889af4e596ba`,
  `COPYING`.
- VSTest: repository commit `36206450d47fec22e281ba9846445edc796f5080`,
  `LICENSE`.
- xUnit: repository commit `9712244020d385955d33136b3fe3e87de43539cd`,
  `license.txt`.

Runtime 10.0.13 legal files are copied explicitly from the restored
`Microsoft.NETCore.App.Runtime.*` packs; default self-contained publishing did
not include them. Unix runtime packs have byte-identical license/notice files
across our four targets. Both Windows packs share their own byte-identical files,
retained separately. SDK 10.0.402 license/notices are also retained.
Storage-probe licensing and provenance are documented separately by its
dependency evaluation. License material must be refreshed and reviewed with
package updates.

The isolated `SectorWorks.ManagedNtfs` source repair also retains the DiscUtils
MIT license and original per-file copyright headers. Its source and pinned
transformation provenance are part of Corresponding Source, not a substituted
NuGet assembly identity. `SectorWorks.NtfsSecurity` and original repair changes
follow the repository's GPL-3.0-only terms. The unchanged full upstream license
is already included here as `DiscUtils.txt` and copied into every publish.
See [dependency evaluation](../dependency-evaluation.md) for the repair boundary,
package identities and qualification evidence.

After a solution restore, regenerate the resolved legal-metadata inventory with
`python3 tools/update-package-inventory.py` from the repository root. Review the
result and retain any newly required full notices separately; generating metadata
does not replace a license review.
