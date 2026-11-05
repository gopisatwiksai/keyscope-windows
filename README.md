# KeyScope

KeyScope is a small, local Windows 10/11 desktop app that estimates the **song or section key** of whatever is playing through the **default Windows playback device**. It uses WASAPI loopback, so it can listen to Spotify, YouTube in a browser, VLC, Ableton Live, and similar apps without an upload, an audio file, or a virtual audio cable.

It displays one whole-track or song-section key estimate such as `A minor`, its major/minor mode, accumulated evidence time, and a deliberately conservative rough BPM estimate when enough rhythmic audio is available. It is deliberately **not** a chord detector.

> **Status: v0.2.0-alpha.** The audio capture and whole-track analysis paths are tested, but the app has not been benchmarked on a labelled song dataset. It does not claim a measured accuracy percentage.

## Highlights

- Captures the default Windows speaker/headphone output using WASAPI loopback.
- Works locally with common playback apps; no microphone, file upload, account, cloud service, or virtual audio cable.
- Uses progressive whole-track analysis rather than choosing a key from the current chord.
- Waits for meaningful context and handles silence / unclear tonal material explicitly.
- Includes build scripts, source, tests, and the corresponding GPL source for its native key engine.

## Run the included build

1. Open PowerShell in this folder.
2. Run `./build.ps1`.
3. Open `./dist/KeyScope.exe`.

If PowerShell blocks local scripts, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The script downloads only the free CSCore audio library from NuGet, compiles the 32-bit Windows app, and creates `dist/KeyScope.exe` plus `CSCore.dll` and the bundled `keyfinder_bridge.dll`. A current Windows 10/11 installation normally already has the needed .NET Framework runtime/compiler. If it does not, install Microsoft's free **.NET Framework 4.8 Developer Pack** and run the script again. The 32-bit app runs normally on 64-bit Windows.

Optional verification commands:

```powershell
# Synthesized C-major/A-minor/silence/tempo checks
.\build.ps1 -RunTests

# Opens the default WASAPI loopback endpoint for 2 seconds and reports callback data
.\build.ps1 -ProbeLoopback
```

## Use it

1. Make sure the speaker/headphone device you are listening on is Windows' default output.
2. Open KeyScope and choose **Start listening**.
3. Play a tonal section for about 20 seconds. The display deliberately gathers whole-track evidence before reporting a key; it is designed not to change on every chord.
4. Choose **Stop** when done. The app then closes the loopback stream and clears the display.

Audio is analysed in memory only. The app does not save recordings, upload audio, open a microphone, or require an account.

## Reading the result honestly

- **Evidence is elapsed analysed audio, not an accuracy percentage.** The underlying engine does not expose a calibrated probability, so the app does not invent one.
- A full song can change key, use modal harmony, or have an intro whose chord progression points somewhere different from the overall key. Treat the displayed result as a useful starting point and check it musically when precision matters.
- The detector uses at least 18 seconds of captured tonal evidence before its first result. It retains the progressive whole-track chromagram and changes an existing key only after a sustained new result.
- Sparse single-note audio, drums, speech, ambience, silence, and non-tonal sound should show **No reliable key yet** or **No audible signal**, rather than inventing a key.
- **Rough BPM** is optional and intentionally conservative. It needs several seconds of rhythmic audio and may prefer a half/double tempo for some songs.

## Implementation

- `AudioLoopbackListener.cs` — shared-mode WASAPI loopback capture of the default render endpoint, queued into the whole-track analyser.
- `NativeKeyFinderDetector.cs` — managed wrapper for libKeyFinder's progressive audio analysis.
- `native/keyfinder_bridge.dll` — the bundled 32-bit native bridge; corresponding source is in `third_party/`.
- `MusicAnalysis.cs` — retained compact DSP reference and deterministic synthetic tests; it is not the runtime key engine.
- `MainForm.cs` — compact WinForms UI with Start/Stop controls and clear no-signal/uncertain states.
- `tests/SmokeTests.cs` — deterministic synthesized silence, C major, A minor, and 120 BPM envelope tests.
- `tests/LoopbackProbe.cs` — a real-device WASAPI callback check.

The detector runs entirely on-device. KeyScope uses libKeyFinder under GPL v3 or later and CSCore for Windows audio capture; see `THIRD-PARTY-NOTICES.md` and `LICENSE.md`.

## Publishing on GitHub

The repository description, upload commands, release checklist, and licence requirements are in [GITHUB_UPLOAD_GUIDE.md](GITHUB_UPLOAD_GUIDE.md). Release notes are in [CHANGELOG.md](CHANGELOG.md).
