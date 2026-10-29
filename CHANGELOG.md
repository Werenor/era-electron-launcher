# Changelog

## 0.3.0 — 2026-10-29

- Public release as **Era-Electron启动器**.
- Rename the display-name field to 游戏名称.
- Automatically save valid settings after editing; remove the save button and save-confirmation prompts.
- Register and select multiple engine versions. Standard launch no longer rejects engines based on the 5.1.0 allowlist.
- Display detected engine versions and expose the official engine release page in the default browser.
- Build the optional 5.1.0 adapter locally at launch; distribute no engine or private game binaries.
- Keep version-specific keystate extensions separate from unrestricted standard launch.
- Retain temporary engine isolation, game-directory locking, launch logs and exit cleanup.
