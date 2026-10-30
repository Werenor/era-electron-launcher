# Third-party notices

Era-Electron启动器 is an independent third-party launcher. It is not an official Era-Electron release and is not affiliated with its developers.

## Era-Electron

- Project and official downloads: https://gitgud.io/umaera/engine/era-electron
- Releases: https://gitgud.io/umaera/engine/era-electron/-/releases
- Copyright: upstream Era-Electron / UmaERA contributors.
- Upstream license: GNU General Public License, version 2; see the upstream LICENSE at tag 5.1.0.
- This distribution contains no original engine binaries, games, SDKs or personal data.
- The optional 5.1.0 runtime adapter modifies a private temporary copy of an engine supplied by the user. Its adaptation source and all source needed to build the launcher's own components are included in this project's source release. Original rendering/preload/assets are retained byte-for-byte.

The launcher, adapter source and native keystate component are released under GPL-2.0. The full license text and these notices are embedded in the launcher's About page and included as LICENSE and THIRD_PARTY_NOTICES.md in the source release. Engine downloads retain their own notices and licenses.

## Runtime

The Windows application uses the system .NET Framework and Windows APIs. No Python runtime, service, browser extension or global keyboard hook is installed.

## Launcher icon

The icon uses the user-selected Illustrious avatar from Azur Lane. Character artwork rights remain with their respective rights holders. This artwork is not covered by the launcher source code's GPL-2.0 license. assets/illustrious.png preserves the supplied image; tools/build-icon.ps1 converts it to the multi-size Windows icon without changing its composition.
