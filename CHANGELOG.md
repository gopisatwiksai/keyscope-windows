# Changelog

## v0.2.0-alpha

- Replaced the short-window prototype key path with progressive whole-track analysis via libKeyFinder.
- Captures default Windows playback through WASAPI loopback; no upload, file import, microphone, or virtual audio cable is required.
- Uses accumulated evidence time in the UI instead of an uncalibrated confidence percentage.
- Holds an established result while it checks whether a new song section is sustained.
- Includes deterministic DSP tests, a native key-engine test, and a real WASAPI callback probe.
- Includes GPL source and notices for the embedded native key-analysis engine.

## Known limitations

- This is an alpha release. It has not been benchmarked against a labelled music test set, so no accuracy percentage is claimed.
- Key estimation is inherently ambiguous for short excerpts, modal music, sparse audio, speech, drum-heavy material, and songs that modulate.
- Rough BPM can resolve to half or double the intended tempo.
