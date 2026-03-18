# GitMishigeh

Cross-platform desktop Git GUI built with .NET and Avalonia.

This project is currently a vibe-coded WIP experiment. It is intentionally lightweight, still evolving, and very much not pretending to be a finished product yet.

## Builds

GitHub Actions produces downloadable builds for:

- macOS Apple Silicon: `GitMishigeh-macOS-AppleSilicon.zip`
- macOS Intel: `GitMishigeh-macOS-Intel.zip`
- Windows x64: `GitMishigeh-Windows-x64.zip`
- Linux x64: `GitMishigeh-Linux-x64.tar.gz`

You can get them from:

- Latest prerelease assets: `https://github.com/brooklynDev/GitMishigeh/releases/tag/latest`
- Individual workflow run artifacts: the `Artifacts` section on each GitHub Actions run

## Local Development

Prerequisite: .NET SDK 10+

```bash
make build
make run
```

To generate local release packages:

```bash
make publish
```

## macOS Gatekeeper

If macOS blocks the downloaded app, remove the quarantine attribute before opening it:

```bash
xattr -dr com.apple.quarantine "GitMishigeh (Apple Silicon).app"
```

For the Intel build, use:

```bash
xattr -dr com.apple.quarantine "GitMishigeh (Intel).app"
```
