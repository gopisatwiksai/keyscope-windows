# Third-party notices

## libKeyFinder 2.2.2

KeyScope uses libKeyFinder's progressive whole-track chromagram and key-classification engine. The compiled `keyfinder_bridge.dll` statically includes libKeyFinder together with the bridge source in `third_party/keyfinder_bridge/`.

- libKeyFinder source: `third_party/libKeyFinder/`
- libKeyFinder licence: GNU GPL v3 or later (the complete licence is `LICENSE.md`)
- Upstream: https://github.com/ibsh/libKeyFinder

Because KeyScope links to this GPL engine, the KeyScope application source and distributed executable are licensed under GPL v3 or later. Corresponding source for the bridge and engine is included in this folder.

## CSCore 1.2.1.2

KeyScope uses CSCore solely to open the Windows WASAPI loopback capture stream. CSCore is copyright Florian R. and contributors and is licensed under the Microsoft Public License (Ms-PL).

- Source and licence: https://github.com/filoe/cscore/blob/master/license.md
- Package used by the build: https://www.nuget.org/packages/CSCore/1.2.1.2
