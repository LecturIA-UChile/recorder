# Building and packaging

## Prerequisites

| Tool | Version | Install |
| --- | --- | --- |
| .NET SDK | 8.0+ | `winget install Microsoft.DotNet.SDK.8` |
| Inno Setup | 6.x | `winget install JRSoftware.InnoSetup` |
| PowerShell | 5.1 or 7+ | Ships with Windows 10/11 |

After installing, open a new terminal so updated `PATH` entries are picked
up.

## One-shot build

```powershell
./build.ps1
```

This produces:

- `publish/win-x64/LecturIA.exe`, self-contained executable (~71 MB).
- `dist/lecturia-recorder-<version>.exe`, compressed installer (~66 MB).

To stop after the publish step (skip Inno Setup):

```powershell
./build.ps1 -SkipInstaller
```

## Manual build, step by step

### 1. Restore and build

```powershell
dotnet restore LecturIA.sln
dotnet build LecturIA.sln -c Release
```

### 2. Publish the self-contained executable

```powershell
dotnet publish src/LecturIA.App/LecturIA.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish/win-x64
```

Flag breakdown:

- `--self-contained true`: bundles the .NET runtime so the user does not
  need to install anything extra.
- `PublishSingleFile=true`: packs everything into a single executable.
- `IncludeNativeLibrariesForSelfExtract=true`: includes native dependencies
  (NAudio).
- `EnableCompressionInSingleFile=true`: shrinks the binary.

### 3. Generate the installer

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer/LecturIA.iss
```

The installer is written to `dist/`.

## Local installation test

1. Run `dist/LecturIA-Setup-1.0.0.exe`.
2. Install for the current user (recommended; no admin required).
3. The application is installed under
   `%LOCALAPPDATA%\Programs\LecturIA\LecturIA.exe`.
4. A Start menu entry is created (and, optionally, a desktop shortcut).
5. On launch, user data lives under `%LOCALAPPDATA%\LecturIA\` and
   recordings under `Desktop\LecturIA_grabaciones\`.

To uninstall: Settings → Apps → LecturIA → Uninstall.

## Bumping the version

Update both files and rerun `build.ps1`:

1. `Directory.Build.props`, `<Version>`, `<FileVersion>`,
   `<AssemblyVersion>`.
2. `installer/LecturIA.iss`, `#define AppVersion`.

## Distribution

The single artifact handed to end users is
`dist/lecturia-recorder-<version>.exe`. It is a standard Windows installer:
download and double-click.

> Tip: to avoid the SmartScreen warning ("Windows protected your PC"), the
> installer should be signed with a code-signing certificate. Without a
> signature the installer still works, but the user has to click
> "More info" → "Run anyway" on the first launch.

## Code signing

LecturIA combines two distribution channels at zero recurring cost:

1. **GitHub Releases** carrying an installer signed by **SignPath
   Foundation** (free for qualifying open-source projects).
2. **Manual download** of the installer from the same Release.

The unsigned installer works, but a signed binary defeats the most
aggressive SmartScreen block and lets reputation accumulate over time
(after a few dozen successful downloads, the warning disappears).

### Local signing with a personal certificate

Using a `.pfx` file:

```powershell
./build.ps1 -CertificatePath C:\path\to\cert.pfx -CertificatePassword '<pwd>'
```

Using a certificate already installed in the current user's certificate
store:

```powershell
./build.ps1 -CertificateThumbprint <THUMBPRINT>
```

The script signs both the self-contained executable and the installer.
`signtool.exe` (Windows SDK) must be available:

```powershell
winget install Microsoft.WindowsSDK.10
```

### Signing through SignPath Foundation (recommended for OSS)

[SignPath Foundation](https://signpath.org) issues OV-level signatures to
qualifying open-source projects free of charge.

Activation steps:

1. Apply at signpath.org with the URL of this repository.
2. Once approved, create the project in the SignPath console and capture:
   - `organization-id`
   - `project-slug`
   - `signing-policy-slug` (typically `release-signing`)
3. Generate a SignPath API token and store it as the GitHub secret
   `SIGNPATH_API_TOKEN`.
4. Edit `.github/workflows/release.yml`, uncomment the lines marked with
   `# SignPath:`, and replace `<SIGNPATH_ORG_ID>` with the real value.
5. Push a tag (`git tag v1.0.0; git push origin v1.0.0`) to trigger the
   workflow. The signed installer is then attached to the GitHub Release
   automatically.

While the SignPath application is in flight, the workflow already produces
unsigned artifacts that can be downloaded from the Actions tab.
