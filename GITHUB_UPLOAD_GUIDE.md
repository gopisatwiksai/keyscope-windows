# Upload KeyScope to GitHub

## Suggested repository details

**Repository name:** `keyscope-windows`

**Short description:**

> A free local Windows song-key detector using WASAPI loopback and progressive whole-track analysis. No audio uploads or virtual audio cable required.

**Suggested topics:** `windows`, `wasapi`, `music-key-detection`, `audio-analysis`, `dsp`, `csharp`, `winforms`, `open-source`

Choose a **public** repository only if you are comfortable sharing the GPL-licensed source, the included native bridge, and the third-party source under `third_party/`.

## Before you upload

Run this from the project folder:

```powershell
.\build.ps1 -RunTests -ProbeLoopback
```

Check that it finishes without errors. The generated `.build/` and `dist/` folders are already excluded by `.gitignore`; do not add them to the source repository.

Keep these tracked because the app needs them:

- `native/keyfinder_bridge.dll`
- `third_party/libKeyFinder/`
- `third_party/keyfinder_bridge/`
- `LICENSE.md`
- `THIRD-PARTY-NOTICES.md`

## Create and upload the repository

1. On GitHub, create an empty repository named `keyscope-windows`. Do not add GitHub's default README, licence, or `.gitignore`, because this folder already has them.
2. Open PowerShell in this project folder.
3. Replace `YOUR-USERNAME` below with your GitHub username, then run the commands one at a time:

```powershell
git init
git add .
git status
git commit -m "Initial public release of KeyScope"
git branch -M main
git remote add origin https://github.com/YOUR-USERNAME/keyscope-windows.git
git push -u origin main
```

Before `git commit`, read `git status`. You should see source files, documentation, `native/keyfinder_bridge.dll`, and `third_party/`. You should not see `.build/` or `dist/`.

## Recommended first release

After the repository is uploaded:

1. Run `./build.ps1`.
2. Zip the contents of `dist/` together: `KeyScope.exe`, `CSCore.dll`, `keyfinder_bridge.dll`, `LICENSE.md`, and `THIRD-PARTY-NOTICES.md`.
3. Create a GitHub Release named `v0.2.0-alpha`.
4. Upload that ZIP as the release file.

Use the release notes from `CHANGELOG.md`. Do not describe the result as “maximum accuracy” or quote an accuracy percentage unless you have evaluated it against a labelled test set. The current app is an early, local whole-track detector and needs real-song validation.

## If you change the native engine

The app includes source for the GPL key engine and bridge under `third_party/`. Keep that source, the GPL licence, and the third-party notice with every public release that contains `keyfinder_bridge.dll`.
