MeFriendos WindowsGSM Valheim Plugin 0.1.0
==========================================

INSTALL
-------
1. Copy the complete Valheim.cs folder into:
   <WindowsGSM>\plugins\Valheim.cs

2. Reload plugins or restart WindowsGSM.

3. Install "Valheim Dedicated Server".

4. Change the default password in Server Start Param.
   The plugin intentionally refuses to start with CHANGE_ME.

5. Do not add -crossplay. This MeFriendos build uses the Steam backend.

6. Open/forward only the required Valheim game ports for the configured
   game port and game port +1. Defaults:

   UDP 2456-2457

   Valheim has no built-in RCON port.

BEPINEX / MODDED SERVER
-----------------------
For Windows, install the current BepInExPack_Valheim contents directly into
Valheim's serverfiles root. The following must be beside valheim_server.exe:

   BepInEx\
   doorstop_config.ini
   winhttp.dll

No special Windows launch wrapper is needed. BepInEx loads through Doorstop
when WindowsGSM starts valheim_server.exe.

For r2modman / Thunderstore profiles, copy only server-compatible mod files
and configuration into the matching BepInEx folders on the dedicated server.
Always check each mod's server requirements.

SECURITY
--------
This build removes WindowsGSM's broad automatic application firewall rule for
the exact valheim_server.exe path before the server starts. If removal cannot
be verified, startup is blocked.

Manual port-specific firewall rules are left untouched.

SHUTDOWN
--------
Valheim should be stopped with CTRL+C. The plugin attempts CTRL+C first and
uses forced termination only as a final fallback.

PROJECT
-------
https://github.com/PapaGordon/WindowsGSM.Valheim-MeFriendos
