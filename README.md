# CAD Publish Kit

[简体中文](README.zh-CN.md)

A local Windows tool for preparing SolidWorks model deliveries. This prototype reads assembly components and multibody parts, and exports individual solid bodies to STEP. CAD models stay on your computer.

## Current status

- Tested on Windows 11 x64, SolidWorks 2026 SP0.0 and .NET Framework 4.8 or later. Other environments are unverified.
- 11 core tests pass.
- Five solid bodies from a real imported STEP model were individually exported and reopened in SolidWorks. Body counts, observed face counts and six axis extrema matched; save/load error codes were zero.
- The original STEP hash was unchanged. Source configuration, selection objects/marks, path, dirty flag and active document checks passed.
- Native assembly traversal/count acceptance is still pending. Assembly STEP export, configuration-specific part export, desktop UI, browser viewing and exploded views are not implemented.
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

## Build and scan

Run from the project directory in PowerShell:

```powershell
.\scripts\build.ps1
.\bin\CoreTests.exe
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

`export_report.json` maps original body names to output files and records checks, save/load codes, STEP AP preference and source-state results. `export_log.txt` records individual results. The case runner generates BOM.csv with body names, body quantities, relative STEP paths, export status, face counts and axis deviation in millimetres. Quantities count exported solid bodies, not reconstructed mechanical parts. Interactive preview is not yet implemented.

Existing STEP settings are used without changing global preferences. The tested AP setting was 203; the report does not capture every export preference. Only documents created by the run are closed. The source is reactivated without requesting a rebuild, and state discrepancies are reported. No native CAD source file is saved. No disk files are automatically deleted; failed outputs remain for diagnosis.

## Limitations and safety

- Scan mode only reads the active configuration. Export mode creates temporary documents as described above.
- Assembly deduplication uses normalized Windows paths and case-sensitive configuration names, not geometric similarity. Network aliases and symbolic links are not resolved.
- Suppressed/unloaded subassembly descendants may be unavailable. Counts are observed instances, not purchasing quantities. Virtual and unknown components are flagged. IDs are local to one run.
- CSV uses UTF-8 BOM, quoted fields and formula-prefix protection. JSON retains original text. Reports may contain private paths.
- Each run uses a separate output directory. Existing files are not intentionally overwritten. An interrupted run can leave temporary documents and pending report entries.
- Exit codes: `0` = implemented checks completed without additional issues; `1` = fatal/preflight error; `2` = review required. Scan coverage limitations still apply to `0`.

## Development and licensing

See [development history](docs/DEVELOPMENT.md) and [API notes](docs/API-NOTES.md), currently in Chinese. The next milestone is export preview/reporting and failure-case validation.

Interop DLLs are copied only to ignored local build output. Private CAD files and third-party binaries are not committed. An open-source license and dependency redistribution review have not yet been completed.
