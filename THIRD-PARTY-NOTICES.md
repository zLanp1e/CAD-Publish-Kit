# External runtime requirements

The MIT license covers CAD Publish Kit's original source code and binaries.
It does not grant rights to SOLIDWORKS, its interface libraries, .NET Framework,
or user-supplied CAD models.

- SOLIDWORKS is separately installed and licensed software from Dassault
  Systemes. `SolidWorks.Interop.sldworks.dll` and `SolidWorks.Interop.swconst.dll`
  are read from the user's local `api/redist` installation directory. They are
  excluded from the release archive and repository. The launcher copies them
  into the user's writable local `bin` directory for runtime use.
- Microsoft .NET Framework is an external Windows runtime and is not bundled.
- No third-party CAD models, templates, fonts, browser libraries, or example
  drawings are distributed in this release.

Verified environment: Windows 11 x64, SOLIDWORKS 2026 SP0.0, .NET Framework 4.8.
Other versions are unverified. A working SOLIDWORKS installation/license and
an empty part template are required for export.
