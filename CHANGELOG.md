# Changelog

All notable changes to the MeFriendos build are documented here.

## 0.1.0 - 2026-09-10

### Added

- Created the MeFriendos build based on Sarpendon's `WindowsGSM.Valheim`.
- Added exact-path removal and verification of WindowsGSM's broad `valheim_server.exe` firewall application exception.
- Added explicit validation for the Valheim game port and password.
- Added a placeholder-password guard so a newly installed server cannot accidentally start with a known default password.
- Added output-only WindowsGSM embedded-console support while keeping stdin available for graceful shutdown.
- Added a CTRL+C-first shutdown path with the original console-keystroke method and process kill as fallbacks.
- Added BepInEx and r2modman/Thunderstore deployment documentation.
- Added Steam-backend and manual firewall documentation for UDP `2456-2457`.

### Changed

- Removed Crossplay from the default configuration and reject `-crossplay` at startup for this Steam-only build.
- Kept the official SteamCMD App ID `896660` and anonymous installation.
- Set the default world name to `Dedicated` and maximum-player display value to `10`.
- Retained Sarpendon's MIT license notice.
