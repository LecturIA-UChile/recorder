# LecturIA Recorder

Windows desktop application for primary-school teachers in Chile. It manages
a class roster and records each student reading aloud, producing MP3 files
ready for downstream evaluation of reading quality (*dominio lector*).

The user interface is in Spanish because the target users are Chilean
teachers; everything else (code, comments, documentation, scripts) is in
English.

## Features

- Imports the student roster from an Excel (`.xlsx`, `.xls`) or CSV file.
- Incremental search over the loaded roster.
- Microphone recording captured as 16 kHz mono MP3 at 64 kbps, a format
  that keeps files small while remaining transparent for downstream speech
  recognition (Whisper and similar).
- Recordings are saved to
  `Desktop\Grabaciones LecturIA\<RUT>_<NOMBRE>_<APELLIDO>.mp3`. When a file
  already exists for the same student, a numeric suffix is appended
  automatically.
- Per-user installer that does not require administrator rights.

## Technical stack

| Layer | Technology |
| --- | --- |
| UI | WPF on .NET 8 with MVVM (`CommunityToolkit.Mvvm`) |
| Audio | NAudio |
| Roster import | ClosedXML (Excel) and CsvHelper (CSV) |
| Packaging | `dotnet publish` self-contained, single-file |
| Installer | Inno Setup 6 (per-user, no admin required) |

## Repository layout

```
lecturia-recorder/
├── .github/workflows/        CI/CD definitions (GitHub Actions).
├── docs/                     Long-form documentation (when present).
├── installer/                Inno Setup script.
├── src/
│   ├── LecturIA.Core/        Models and abstractions, no dependencies.
│   ├── LecturIA.Infrastructure/  Audio, storage, importers.
│   └── LecturIA.App/         WPF + view models (composition root).
├── tools/                    Developer utilities (PowerShell).
├── BUILD.md                  Build, sign, and release instructions.
├── CONTRIBUTING.md
├── Directory.Build.props     Shared MSBuild properties.
├── LecturIA.sln
├── LICENSE
├── README.md
├── build.ps1                 One-shot build entry point.
└── global.json               .NET SDK pin.
```

## Quick build

Requires .NET 8 SDK and Inno Setup 6 (see [BUILD.md](./BUILD.md)):

```powershell
./build.ps1
```

The installer is produced under `dist/LecturIA-Setup-<version>.exe`.

## User data and privacy

All personally identifiable information (student names and recordings)
stays **local on the teacher's machine** and is never bundled with the
application or synced to remote servers:

- Roster: `%LOCALAPPDATA%\LecturIA\names.json` with the shape
  ```json
  { "nombres": ["NAME 1", "NAME 2", "..."] }
  ```
- Recordings: `Desktop\Grabaciones LecturIA\<RUT>_<NOMBRE>_<APELLIDO>.mp3`

This repository **does not** contain real rosters or recordings. The
patterns `names.json`, `*.mp3`, `*.wav`, and the recordings folder are
blocked in `.gitignore` to prevent accidental commits.

## License

Distributed under the MIT License. Copyright © 2026 Facultad de Ciencias
Físicas y Matemáticas, Universidad de Chile. See [LICENSE](./LICENSE).
