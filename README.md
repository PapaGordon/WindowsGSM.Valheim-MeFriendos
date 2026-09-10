<p align="center">
  <img src="Valheim.cs/Valheim.png" alt="Valheim" width="128">
</p>

<h1 align="center">WindowsGSM.Valheim</h1>

<p align="center">
  MeFriendos build for running a Valheim dedicated server with WindowsGSM.
</p>

<p align="center">
  <a href="https://github.com/WindowsGSM/WindowsGSM"><img src="https://img.shields.io/badge/WindowsGSM-%E2%89%A51.21-38CDD4" alt="WindowsGSM 1.21+"></a>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/version-0.1.1-8802db" alt="Version 0.1.1"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
</p>

This plugin installs, updates and runs the official Valheim Dedicated Server through SteamCMD. The MeFriendos build is based on `WindowsGSM.Valheim` by Sarpendon and is prepared for a Steam-only, BepInEx-modded server without Crossplay.

## Features

- Installs and updates the official Valheim Dedicated Server through SteamCMD.
- Uses the Steam backend by default and explicitly rejects accidental `-crossplay` startup parameters.
- Supports BepInEx on Windows without a custom wrapper: when BepInEx is installed beside `valheim_server.exe`, launching the normal server executable loads it through Doorstop.
- Supports the WindowsGSM embedded console as read-only output while keeping the native console available for clean shutdown.
- Keeps Valheim's live runtime output available to the WindowsGSM embedded console by avoiding native `-logFile` redirection while Embed Console is enabled.
- Sends CTRL+C first when stopping the server, as recommended by Valheim, with a controlled fallback if the process does not exit.
- Validates the game port and requires a non-placeholder server password of at least five characters.
- Creates local `save-data` and `logs` directories when needed.
- Removes WindowsGSM's broad automatic firewall application exception for the exact `valheim_server.exe` before the server starts listening.
- Leaves targeted manual firewall rules unchanged.

## Quick overview

| Setting | Value |
| --- | --- |
| SteamCMD App ID | `896660` |
| Start executable | `valheim_server.exe` |
| Default game port | `2456/UDP` |
| Default query/secondary port | `2457/UDP` |
| Port increment | `2` |
| SteamCMD login | Anonymous |
| Networking | Steam backend, no Crossplay |
| Mod framework | BepInEx-ready |
| Built-in RCON | None |
| Firewall ports | Manual configuration only |

## Requirements

- [WindowsGSM](https://github.com/WindowsGSM/WindowsGSM) 1.21 or newer
- Administrator rights for WindowsGSM
- 64-bit Windows
- For mods: the current [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and server-compatible Valheim mods

Valheim, BepInEx and third-party mods are not distributed with this plugin.

## Plugin installation

1. Download the latest release archive from [Releases](https://github.com/PapaGordon/WindowsGSM.Valheim-MeFriendos/releases/latest).
2. Extract the complete `Valheim.cs` folder into `<WindowsGSM>\plugins\`.
3. Click **Reload Plugins** or restart WindowsGSM.
4. Add **Valheim Dedicated Server** and run **Install**.
5. Replace the default `CHANGE_ME` password in **Server Start Param** with a strong password of at least five characters.
6. Configure the required UDP game ports manually.
7. Start the server.

The default parameters are:

```text
-password "CHANGE_ME" -savedir ".\save-data" -public 1 -saveinterval 1800 -backups 4 -backupshort 7200 -backuplong 43200
```

The plugin intentionally refuses to start while the placeholder password is still configured.

If you upgrade an existing 0.1.0 server and `-logFile` is still present in **Server Start Param**, you do not have to remove it for Embed Console to work: version 0.1.1 filters that argument from the Valheim launch command while Embed Console is enabled. It is still recommended to remove the obsolete parameter from the saved configuration unless you intentionally want to use it with Embed Console disabled.

## Steam-only networking and ports

This build is intended for the normal Steam backend. Do **not** add `-crossplay`.

Valheim uses the selected game port and the following port. With the default WindowsGSM value this means:

| Purpose | Protocol | Port |
| --- | --- | --- |
| Game | UDP | `2456` |
| Secondary/query traffic | UDP | `2457` |

For the MeFriendos firewall policy, allow `2456-2457/UDP` publicly. No separate Valheim RCON rule is required because the dedicated server has no built-in RCON service.

If a future BepInEx administration mod opens its own management port, keep that port separate from the public game rules and restrict it to the trusted Private/VPN network.

## BepInEx and r2modman / Thunderstore

On Windows, BepInEx does not require a separate launch script. Install the contents of `BepInExPack_Valheim` directly into the Valheim server root so the following sit beside `valheim_server.exe`:

```text
BepInEx\
doorstop_config.ini
winhttp.dll
```

The current BepInExPack uses Doorstop to load BepInEx automatically when `valheim_server.exe` starts.

For a mod profile created with r2modman or Thunderstore Mod Manager:

1. Build and test the profile on a matching Valheim version.
2. Open the profile folder.
3. Copy the server-compatible contents from `BepInEx\plugins`, `BepInEx\config` and, when required, `BepInEx\patchers` into the corresponding server folders.
4. Copy additional root files only when a specific mod explicitly requires them.
5. Do not deploy client-only mods to the dedicated server unless their documentation says they support server installation.
6. Start the server and review `BepInEx\LogOutput.log` plus the WindowsGSM embedded console. If Embed Console is disabled and you intentionally configure Valheim's `-logFile`, review that file instead.

Back up the world, BepInEx configuration and mod list before Valheim or mod updates. Game updates can temporarily break individual mods even when BepInEx itself still loads.

## Security: automatic port opening is disabled

WindowsGSM can create an unrestricted application firewall exception for `valheim_server.exe`. This MeFriendos build removes the exact executable exception before starting the server.

The cleanup uses the Windows Firewall COM API family (`HNetCfg.FwMgr`), matching the approach used by WindowsGSM itself and the other hardened MeFriendos plugins. It does not depend on PowerShell `NetSecurity` cmdlets.

Only the exact server executable path is selected. After removal, the plugin reacquires the authorized-application list and verifies that the exception is gone. If Windows cannot remove or verify the rule, startup is stopped instead of silently continuing with broad firewall access.

The plugin does **not** create port rules. The intended MeFriendos policy is a narrow manual rule for `2456-2457/UDP` and no public administration port.

## Embedded console and shutdown

When **Embed Console** is enabled, Valheim's standard output and standard error streams are forwarded to WindowsGSM. Standard input is deliberately not redirected so the native console remains available for clean CTRL+C shutdown.

WindowsGSM writes the selected server's Embed Console state into the plugin's `AllowsEmbedConsole` field immediately before `Start()` is called. The plugin therefore uses that field as the effective runtime state, matching the normal WindowsGSM plugin flow.

Valheim's `-logFile` option redirects the live runtime log away from the process output used by WindowsGSM. Version 0.1.1 therefore does not include `-logFile` in the default parameters and removes a legacy/manual `-logFile <path>` argument from the launch command only while Embed Console is enabled. With Embed Console disabled, a manually configured `-logFile` is left untouched.

Valheim's own dedicated-server documentation recommends stopping the server with CTRL+C. The plugin therefore tries a console CTRL+C first and waits up to 20 seconds. If that is unavailable, it tries the original WindowsGSM Valheim console-keystroke method. A forced process kill is used only as the final fallback.

## Updating Valheim

1. Stop the server cleanly.
2. Back up `save-data`, the world files and the complete BepInEx configuration/mod set.
3. Click **Update** in WindowsGSM.
4. Confirm that the current BepInExPack and all server mods support the installed Valheim version.
5. Start the server and inspect the console/logs for mod-loader or plugin errors.

The plugin does not automatically install, remove or update third-party mods.

## Troubleshooting

### Startup says the password placeholder must be changed

Edit **Server Start Param** and replace `CHANGE_ME` with the real server password.

### Startup says Crossplay is disabled

Remove `-crossplay` from **Server Start Param**. This build is intentionally configured for Steam networking.

### Embedded console stops after the Unity memory setup lines

Check **Server Start Param** for a manually saved `-logFile` argument. Version 0.1.1 filters it from the actual Valheim command whenever Embed Console is enabled, so the live Valheim output stays attached to WindowsGSM. For a clean upgraded configuration, remove the old `-logFile` argument from the saved parameters as well.

### Players cannot connect

Confirm that the configured game port and port +1 are forwarded and permitted by targeted UDP firewall rules. With the default configuration this is `2456-2457/UDP`.

### BepInEx does not load

Confirm that `winhttp.dll`, `doorstop_config.ini` and the `BepInEx` directory are in the same server root that contains `valheim_server.exe`. Then inspect `BepInEx\LogOutput.log`.

### The server no longer starts after a Valheim update

Test without third-party plugins or update the BepInEx/mod set to versions compatible with the new Valheim build. A game update can break mods even when the WindowsGSM plugin itself is unchanged.

### WindowsGSM reports that automatic firewall access could not be disabled

Run WindowsGSM as administrator. If an unrestricted `valheim_server.exe` application rule exists, remove it manually and keep only the intended port-specific rules.

## Testing checklist

- WindowsGSM loads `Valheim.cs` without a plugin error.
- Install and Update complete through SteamCMD.
- The server refuses the default password placeholder.
- The server refuses `-crossplay` in the start parameters.
- The Steam backend starts on the configured port and port +1.
- BepInEx loads when correctly installed beside `valheim_server.exe`.
- With Embed Console enabled, live Valheim output continues after the Unity memory setup lines.
- A legacy/manual `-logFile` parameter is ignored for the actual launch only while Embed Console is enabled.
- With Embed Console disabled, a manually configured `-logFile` remains available to Valheim.
- Stop sends CTRL+C and the world shuts down cleanly.
- No broad WindowsGSM application exception remains for this server's `valheim_server.exe` after startup.
- Manually configured `2456-2457/UDP` rules remain present.
- A firewall-cleanup failure prevents the server process from starting.

## Project links

- Source: [PapaGordon/WindowsGSM.Valheim-MeFriendos](https://github.com/PapaGordon/WindowsGSM.Valheim-MeFriendos)
- Original plugin: [Sarpendon/WindowsGSM.Valheim](https://github.com/Sarpendon/WindowsGSM.Valheim)
- Valheim dedicated-server guide: [valheim.com/support/a-guide-to-dedicated-servers](https://valheim.com/support/a-guide-to-dedicated-servers/)
- BepInExPack Valheim: [Thunderstore](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- WindowsGSM: [github.com/WindowsGSM/WindowsGSM](https://github.com/WindowsGSM/WindowsGSM)
- Community: [mefriendos.de](https://mefriendos.de)

This is an independent community plugin. It is not affiliated with or endorsed by Iron Gate, Coffee Stain Publishing, BepInEx, Thunderstore, Sarpendon or WindowsGSM.

## License

The original plugin and this MeFriendos build are released under the [MIT License](LICENSE). The original copyright and license notice are retained.
