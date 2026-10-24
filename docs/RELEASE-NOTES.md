# CAD Publish Kit v0.1.0-preview.1

First Windows desktop preview. Requires locally installed/licensed SOLIDWORKS.
Verified on Windows 11 x64, SOLIDWORKS 2026 SP0.0 and .NET Framework 4.8.

## Included

- STEP drag/drop analysis, selectable export preview and configurable destination.
- Multibody splitting and unique imported-assembly part export, with quantities.
- Original STEP + BOM.csv + parts/ deliveries; no silent overwrite.
- Export/reopen checks and per-file errors, with source-state checks.
- Folder inspection through ten descendant levels, status filtering and cancellation
  after the current file; no source/PDF extraction or relocation.
- Prominent single-file and batch navigation; Chinese interface and bilingual docs.

## Validation

11 core, 6 desktop publication, 16 batch/selection and 6 script publication checks
pass (39 synthetic checks, not CAD compatibility tests). Earlier real acceptance:
five-body splitting, 12 unique assembly parts / 19 instances, selected export,
failure isolation, four-case batch inspection and cancellation. The user confirmed
the exported models open for preview. Navigation and minimum-size layout passed.
This release adds packaging/startup checks; it does not expand the CAD test corpus.

## Limits

Native SLDASM input/whole-assembly regeneration, sheet/virtual/unloaded components,
geometric deduplication, browser 3D/exploded views and English UI are not included.
The complete assembly deliverable is the original STEP copy. Part files use local
coordinates. Imported names may vary. Shape range/count checks are not proof of
full geometric equivalence. Large libraries and other CAD versions are unverified.

MIT applies to project code. SOLIDWORKS and .NET are external prerequisites;
vendor libraries and private CAD fixtures are excluded from the archive.
See [installation](INSTALL.md) and [third-party notices](../THIRD-PARTY-NOTICES.md).

## 中文

V0.1 首个测试版，包含拖入分析、勾选导出、BOM、装配体零件统计和递归十层批量检查。
用户已确认拆出的模型可打开预览。保持现有实测范围，尚非跨版本兼容性承诺。
完整解压 Windows ZIP 后双击启动器，首次自动配置本机接口库，详见安装说明。
