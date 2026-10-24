# CAD Publish Kit

[简体中文](README.zh-CN.md)

A local Windows tool for preparing STEP deliveries with SolidWorks: preview, selectable part/body export, BOM and batch folder inspection. CAD models stay on your computer.

**V0.1 Preview** — [Installation](docs/INSTALL.md) · [Release notes](docs/RELEASE-NOTES.md) · [Downloads](https://github.com/zLanp1e/CAD-Publish-Kit/releases).

## Current status

- Tested on Windows 11 x64, SolidWorks 2026 SP0.0 and .NET Framework 4.8 or later. Other environments are unverified.
- 11 core tests pass.
- Five solid bodies from a real imported STEP model were individually exported and reopened in SolidWorks. Body counts, observed face counts and six axis extrema matched; save/load error codes were zero.
- The original STEP hash was unchanged. Source configuration, selection objects/marks, path, dirty flag and active document checks passed.
- Native assembly traversal/count acceptance is still pending. Native SLDASM whole-assembly export, browser viewing and exploded views are not implemented. Imported STEP assembly part export is supported as described below.
- Axis-range checks do not prove full geometric equivalence or compatibility with other CAD applications.

## Folder layout

```text
CAD-Publish-Kit/
├── src/                   C# application source
├── tests/                 Automated code tests, not CAD models
├── scripts/               Build and case-run scripts
├── docs/                  API notes and development history (Chinese)
├── bin/                   Local build output; ignored by Git
├── test-cases/            Private local CAD cases; ignored by Git
│   └── <case-name>/
│       ├── original.step  Original source model
│       ├── BOM.csv        Body inventory and export checks
│       └── parts/         Exported STEP files
└── output/                Internal logs/history; ignored by Git
```

The delivery contains just the source STEP, BOM.csv and parts/. Internal reports stay under output/, outside the delivery. Original filenames are preserved, including non-English names. Private cases are not distributed with this repository: create your own case after cloning.

## Desktop quick start

For the Windows release ZIP, extract it completely; no build is needed. For source checkouts, build once. Then double-click `Start CAD Publish Kit.cmd` or `bin/CADPublishKit.Desktop.exe`. The current interface is Chinese.

1. Drop one `.step` / `.stp` onto the import area, or choose a file. Analysis starts automatically; SolidWorks is connected or started for you.
2. Review names, configurations, instance/body counts, face counts and output filenames. Check the items to export; select a row or expand details for full paths and errors. Type an output path or browse for a folder.
3. Click export. The delivery contains the original STEP copy, `BOM.csv` and `parts/`. Existing deliveries are refused, never silently replaced.

The default destination is a new `<source-name>_delivery` folder beside the input. You can select the input folder if it does not contain BOM.csv or parts/. This interface exports readable solids from imported part documents and unique referenced parts from STEP assemblies. Sheet bodies are not supported. Use the template button to select an empty `.prtdot` if automatic discovery fails, then retry analysis.

SolidWorks and its license remain required. Use one instance with the same Windows account/elevation; do not edit CAD documents while processing. Analysis uses an independent local copy. Internal copies and reports remain in `output/desktop-runs/`; they are not uploaded or automatically deleted. Only owned temporary CAD documents are closed. The project directory must be writable. The portable Windows ZIP has no installer; see the installation guide.

The real five-body drag/drop, analysis, export and reopen workflow passed; the interface remained responsive. An earlier SolidWorks disconnection was reported as failure without publishing a delivery; restarting and retrying passed. More import types and large models remain unverified.

## Batch folder inspection

Click the large folder-icon navigation tab at the top (green when selected), then type or browse for a root folder. The scanner includes that directory (depth 0) and descendant depths 1–10, matching STEP/STP extensions case-insensitively. Same-named files remain separate by full path. Reparse points are not followed; inaccessible directories and the depth boundary are reported. Existing deliveries inside the root are included; this tool's internal workspace is excluded.

Actual export/reopen verification is enabled by default. Results distinguish verified splittable files, structure-only candidates (if verification is unchecked), single-body/single-instance files that cannot be split by the current workflow, review/errors, and unprocessed files. A read or export failure is not labelled as a definitive inability to split. Repeated assembly instances may share one unique output part. Counts describe the supported workflow, not arbitrary geometric decomposition.

Filter to problem rows, open the original folder, or load a row in the single-file preview. Double-click a result to expand details. No extraction, relocation or companion-PDF copying is performed. Internal analysis copies, temporary verification exports and JSON/CSV logs are retained under `output/desktop-runs/`; no delivery package is published by batch inspection. The records button opens the saved results, which remain available after the window closes. Large collections have not been benchmarked and retained working files consume disk space.

Stop requests take effect after the current file finishes. Remaining files stay unprocessed. COM disconnection or unsafe cleanup stops the queue; the tool does not forcibly interrupt an in-flight CAD call. Do not edit CAD documents during processing.

## Selective export

Preview supports select-all, clear-all and individual checkboxes. Output filenames retain their identities when a subset is selected. A subset BOM explicitly states `SELECTED ITEMS ONLY; not a complete BOM` in Notes; the original source STEP remains complete. Preview changes or stale selection identities require analysis again.

## Build and command-line scan

Run from the project directory in PowerShell:

```powershell
.\scripts\build.ps1
.\bin\CoreTests.exe
.\bin\DesktopPublishTests.exe
.\bin\BatchTests.exe
```

The build uses the installed .NET Framework compiler and local SolidWorks interop libraries. No .NET SDK download is required. If COM registration does not locate the installation:

```powershell
.\scripts\build.ps1 -SolidWorksDirectory 'D:\SOLIDWORKS Corp\SOLIDWORKS'
```

Create `test-cases/my-case/` and place exactly one input STEP there. Open the intended model manually in SolidWorks, activate the desired configuration, then run:

```powershell
.\scripts\run-case.ps1 -CaseName 'my-case'
```

Use one SolidWorks instance and the same Windows account/elevation as the tool. Do not edit or switch models during a run. `CaseName` selects the output location only: it does not open the source or verify the active model belongs to that case.

Internal results go to a timestamp/GUID directory under `output/my-case/`. After a fully successful body export, the case runner publishes BOM.csv and parts/ beside the original STEP. It refuses to replace an existing BOM.csv or parts/ directory. Assembly scans produce `manifest.json`, `parts.csv` and `instances.csv`. Part scans produce `manifest.json` and `bodies.csv`, listing solid/sheet bodies (including hidden bodies), face counts and issues. A body inventory is not an original component BOM.

The lower-level command accepts any output parent:

```powershell
.\bin\CadPublishKit.exe .\output
```

## Export solid bodies

With the intended part active:

```powershell
.\scripts\run-case.ps1 -CaseName 'my-case' -ExportBodies
```

If no default part template is configured, supply an existing empty part template. Adjust this example to your installation:

```powershell
.\scripts\run-case.ps1 -CaseName 'my-case' -ExportBodies -Template 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_part.prtdot'
```

Each solid body is copied into a separate temporary part without applying transforms and exported as `parts/body_001.step`, etc. Sheet bodies are not exported. Each STEP is reimported and checked for exactly one solid and no sheets, with six axis extrema compared within 1e-6 metres. Face counts are recorded for inspection.

`export_report.json` maps original body names to output files and records checks, save/load codes, STEP AP preference and source-state results. `export_log.txt` records individual results. The case runner generates BOM.csv with body names, body quantities, relative STEP paths, export status, face counts and axis deviation in millimetres. Quantities count exported solid bodies, not reconstructed mechanical parts. The desktop provides a pre-export body preview.

Existing STEP settings are used without changing global preferences. The tested AP setting was 203; the report does not capture every export preference. Only documents created by the run are closed. The source is reactivated without requesting a rebuild, and state discrepancies are reported. No native CAD source file is saved. No disk files are automatically deleted; failed outputs remain for diagnosis.

## Limitations and safety

- Scan mode only reads the active configuration. Export mode creates temporary documents as described above.
- Assembly deduplication uses normalized Windows paths and case-sensitive configuration names, not geometric similarity. Network aliases and symbolic links are not resolved.
- Suppressed/unloaded subassembly descendants may be unavailable. Counts are observed instances, not purchasing quantities. Virtual and unknown components are flagged. IDs are local to one run.
- CSV uses UTF-8 BOM, quoted fields and formula-prefix protection. JSON retains original text. Reports may contain private paths.
- Each run uses a separate output directory. Existing files are not intentionally overwritten. An interrupted run can leave temporary documents and pending report entries.
- Exit codes: `0` = implemented checks completed without additional issues; `1` = fatal/preflight error; `2` = review required. Scan coverage limitations still apply to `0`.

## Development and licensing

See [development history](docs/DEVELOPMENT.md) and [API notes](docs/API-NOTES.md), currently in Chinese. Next: collect V0.1 user feedback; native assembly acceptance remains future work.

Interop DLLs are copied only to ignored local build output. Private CAD files and third-party binaries are not committed. Project code is licensed under MIT. Release archives exclude vendor DLLs; the launcher obtains them from the local SolidWorks installation. See LICENSE and THIRD-PARTY-NOTICES.md.

Assembly STEP inputs support preview and export by normalized imported component path + case-sensitive referenced configuration. Each unique part exports all its solid bodies together; repeated instances contribute to BOM quantities. Referenced configuration and issues are available in the status tooltip and CSV. Suppressed, unloaded, virtual, sheet, assembly-modified or unknown geometry blocks complete publication. No configuration is switched. Runtime imported component paths may not exist on disk, and generated imported names may vary between sessions; original names cannot always be recovered.

## Assembly STEP acceptance

The CZ-80 sample passed: 12 unique referenced parts, 19 part instances and 4 assembly nodes. Two unique parts contain multiple solids (2 and 3); each remains a single STEP file. All 12 exports reopened with matching solid and face counts and aggregate six-axis extrema (zero deviation in this sample). Component transforms, configurations and document dirty flags remained unchanged. The original assembly STEP is copied byte-for-byte, preserving its original hierarchy and placement; no new whole-assembly STEP is generated. Individual files use component-local coordinates.

The report retains the hierarchy and raw SolidWorks Transform2.ArrayData per instance (rotation 0–8, translation 9–11 in metres, scale 12, unused 13–15). Export does not apply these transforms. Geometry checks do not prove full shape equivalence or purchasing-BOM completeness. Other configurations, assembly features and third-party viewers still require real acceptance tests. Names use a safe prefix and a path/configuration hash to prevent collisions within an import.

Validation: 11 core checks, 6 desktop publication/naming checks and 6 script publication checks pass. The original five-body workflow also passed a real regression export. Run `bin/DesktopPublishTests.exe` after building to exercise assembly BOM quantities, configuration quoting, filename collisions and incomplete-report rejection using synthetic files.
A real CZ-80 failure-isolation run injected a conflicting file before the second save: that file was preserved, the remaining 11 parts exported and verified, source state was unchanged, and no incomplete delivery was published.

Batch/preview validation: 16 synthetic checks cover the ten-level boundary, duplicate filenames, PDF preservation, excluded-path boundaries, cancellation, result classification and selective-export identities. Real checks used a STEP assembly, a multibody part, a one-solid part and an intentionally invalid file: two verified, one unsplittable, one read error. The PDF fixture and all source hashes remained unchanged. Quick-mode filtering and cooperative cancellation also passed. An opt-in real cancellation check is available as `bin/BatchCadTests.exe --cancel-after-first <folder-with-at-least-two-STEP-files>`; it performs structural analysis only and creates internal records.