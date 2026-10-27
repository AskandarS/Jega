# Contributing to Jega

Issues and pull requests are welcome, most of all from people who use Jega on real projects. How Jega is built is in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); read its invariants (§10) before a change to `Jega.Revit`.

## Never a client's address

Issues are public. Never put a client's address in an issue, a screenshot, an attached `jega.log`, a DXF or CSV, a
test, test data or a commit message. Show the same thing with a public address: the test case, **Skagens Museum,
Brøndumsvej 4, 9990 Skagen**, or Vejle Rådhus, Skolegade 1, 7100 Vejle. Test data lives in
`tests/Jega.Core.Tests/Data/skagens-museum-*` and comes from there only. A key never goes into the code: the live
tests read it from an environment variable (below).

## Reporting a bug

Open an issue with Jega's version (About, at the bottom), Revit's version, what you did, what you saw and what you
expected. Attach `%APPDATA%\Jega\jega.log` once you have looked it through (view names, paths, error texts). Never
send your key or `settings.json`.

## Build

You need the .NET 8 SDK. `Jega.Revit` also needs Revit 2025 installed: it builds against `RevitAPI.dll` and
`RevitAPIUI.dll` in `C:\Program Files\Autodesk\Revit 2025`. If Revit is somewhere else, give its folder with
`-p:RevitDir=<folder>`. `Jega.Core` and its tests need no Revit.

```
dotnet build Jega.sln -c Release                            # everything; needs Revit 2025
dotnet build src/Jega.Core/Jega.Core.csproj -c Release      # Core only, no Revit
dotnet build src/Jega.Revit -c Release -p:DeployToRevit=true   # into Revit 2025 and 2026; close Revit first
powershell -ExecutionPolicy Bypass -File tools/Pack.ps1        # a release: the bundle as a zip and the installer, in dist/
```

- **Warnings are errors** in `Jega.Core` and `Jega.Revit`: a build with a warning fails.
- **Build what you share as Release.** A Debug build writes your source folder into `Jega.dll`, and About shows it.
  Release builds name the source `/_/` (`PathMap` in `Directory.Build.props`), in the DLLs, the PDBs and the stack
  traces in `jega.log`.
- **`-p:DeployToRevit=true`** puts the build into Revit as the installer does: an Autodesk bundle,
  `%APPDATA%\Autodesk\ApplicationPlugins\Jega.bundle\`, with `PackageContents.xml` for Revit 2025 and 2026, the DLLs
  (no PDBs) in `Contents\`, and `Contents\2025\Jega.addin` and `Contents\2026\Jega.addin` pointing at them by the
  relative path `..\Jega.dll`. It also removes a `Jega.addin` and `Jega\` folder left in
  `%APPDATA%\Autodesk\Revit\Addins\2025\` by earlier deploys, so Revit doesn't load Jega twice. With Revit open the
  copy fails and the build says so.
- **A release** is `tools/Pack.ps1`: a Release build staged as the bundle in `dist/stage/`, checked (no PDBs, no
  stray DLLs, no user's folder in any file), zipped as `dist/Jega-<version>-bundle.zip`, and built into
  `dist/Jega-<version>-setup.exe` from `tools/Jega.iss` with Inno Setup 6 (`winget install JRSoftware.InnoSetup`;
  `-NoInstaller` skips that step). `dist/` is git-ignored.
- **The version** is set once, in `Directory.Build.props` (`0.9.0-beta.1`).

## Test

```
dotnet test tests/Jega.Core.Tests/Jega.Core.Tests.csproj --filter "FullyQualifiedName!~LiveTests"
```

- The tests cover `Jega.Core`. Anything in `Jega.Revit` is tried by hand in Revit: say in your pull request what you
  tried, and in which Revit.
- **`LiveTests`** fetch from the real services. Without their environment variables they show as skipped:
  - `JEGA_DATAFORDELER_KEY`: your Datafordeler key. Fetches the test case and compares it with the stored data.
  - `JEGA_LIVE_ADDRESS` (with the key): any public address, for a look; nothing is checked.
  - `JEGA_LIVE_OUT`: the folder for the files (else `%TEMP%`). `JEGA_LIVE_EAVES=1` and `JEGA_LIVE_SETBACK` (m) for
    the address above.

## Code and texts

- **Code and comments are in English. The user interface is Danish by default** (a project that hasn't chosen a
  language speaks Danish) and English when the project chooses it in Settings.
- **Every text the user sees is in resx**, never in the code: `Strings.resx` (English) and `Strings.da.resx`
  (Danish), in `src/Jega.Revit/Resources/` or `src/Jega.Core/Resources/`, by where it is used. A new text goes into
  both, under the same key, grouped with its command's other keys. One whole sentence per entry, never glued from
  pieces. The rules are in [ARCHITECTURE §7](docs/ARCHITECTURE.md#7-texts).
- **The Danish words** follow [docs/GLOSSARY.da.md](docs/GLOSSARY.da.md). Revit's own names (Toposolid, Project Base
  Point, Detail Line …) stay in English in both.
- **The site plan's layer names** are Danish and the same in every language (`src/Jega.Core/SiteplanLayers.cs`). A new
  layer needs its name for Settings > Layers (`Layer_<NAME>`) in both resx of `Jega.Revit`; a test catches a missing
  one.

## Pull requests and commits

- **One change per commit**, with a message that says what changed and why. One subject per pull request.
- The tests must pass, and new behaviour in `Jega.Core` gets a test.
- Contributions are published under GPL-3.0, with the additional permission in [NOTICE](NOTICE) to run with Revit's
  API, as the rest of Jega.
