# Changelog

All notable changes to the MeFriendos build are documented here.

## 0.1.1 - 2026-09-10

### Fixed

- Fixed the WindowsGSM embedded console stopping after Valheim's initial Unity memory setup output.
- Removed `-logFile` from the default server parameters so Valheim keeps its live runtime output attached to the process streams used by WindowsGSM.
- Added compatibility handling for existing 0.1.0 installations: when Embed Console is enabled, a saved `-logFile <path>` argument is removed from the actual Valheim launch command without changing the saved server configuration.
- Kept manually configured `-logFile` support intact when Embed Console is disabled.
- Synchronized the README, installation notes and displayed version with plugin version 0.1.1.

### Notes

- WindowsGSM overwrites the plugin's `AllowsEmbedConsole` value with the selected server's effective Embed Console state immediately before calling `Start()`. Version 0.1.1 uses that runtime value intentionally.
- Existing users may remove an old `-logFile` argument from **Server Start Param** for a clean configuration, but it is no longer required for the embedded console fix to work.

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
