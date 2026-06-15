# Contributing to LecturIA

Thanks for taking the time to contribute. This document captures the
expectations for contributions to keep the codebase consistent and easy to
maintain.

## Ground rules

- Be respectful in issues, pull requests and reviews.
- Keep changes focused. One pull request, one logical change.
- Open an issue before starting on a non-trivial feature so design can be
  discussed up front.
- Never commit personally identifiable information. The repository
  enforces this through `.gitignore`, but contributors are responsible for
  the contents of every patch.

## Coding standards

The repository ships with two steering documents that codify the standards
applied to every file:

- [`.kiro/steering/coding-standards.md`](./.kiro/steering/coding-standards.md)
- [`.kiro/steering/repository-conventions.md`](./.kiro/steering/repository-conventions.md)

Highlights:

- All comments are written in English. User-facing strings remain in
  Spanish (Chilean teachers are the audience).
- No emojis, anywhere except optional UI strings.
- 4-space indentation for C#, XAML, PowerShell, and Inno Setup. 2 spaces
  for YAML.
- C# follows the
  [Microsoft .NET coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions),
  enforced by `.editorconfig`.
- Public and protected members carry XML documentation following Microsoft
  conventions.
- `TreatWarningsAsErrors` is enabled; warnings must be addressed, not
  silenced.

## Workflow

1. Fork the repository and create a topic branch from `main`:
   ```bash
   git checkout -b feat/<short-description>
   ```
2. Make your changes. Keep commits focused and write commit messages in
   English (imperative mood, e.g. `Add Excel import retry`).
3. Run the full local build before opening the pull request:
   ```powershell
   ./build.ps1
   ```
4. Open the pull request against `main`. Reference any related issue and
   describe the user-visible impact.

## Pull request checklist

- [ ] `./build.ps1` succeeds locally.
- [ ] No new warnings are introduced.
- [ ] No new `TODO`, `FIXME`, or commented-out code is committed.
- [ ] Public APIs you added or changed have XML documentation.
- [ ] No personally identifiable information is included.
- [ ] If installer or distribution behavior changes, [`BUILD.md`](./BUILD.md)
      is updated accordingly.

## Reporting issues

Open a GitHub issue using the available templates and include:

- Application version (visible in *Add or remove programs*).
- Windows version (`winver`).
- Steps to reproduce, expected behavior, actual behavior.
- Log output or stack traces when available.
- Never paste real student names. Replace them with placeholders such as
  `STUDENT 1`, `STUDENT 2`.

## Security

For vulnerabilities or sensitive concerns, contact the maintainers at the
email listed in the GitHub repository profile rather than opening a public
issue.
