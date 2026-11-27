# Installation and connection

## Windows

Use the self-contained x64 desktop folder from `artifacts/windows`. Start `ChudLink.Desktop.exe`. Choose a private IPv4 interface in Overview, then Start host. The host listens on TCP 47831 with TLS and discovery listens on UDP 47832. The host rejects peers outside private/loopback IPv4 ranges. There is no public-internet binding, router configuration or mandatory online service.

Closing the window hides it in the tray. Double-click the tray icon to reopen it. Choose Quit ChudLink to close connections and stop all workers. Keep only one host instance for the same data directory. The standalone `artifacts/host/ChudLink.Host.exe <private-IP>` offers `pair`, `pending`, `approve <ticket>`, `deny <ticket>`, `devices`, `revoke <id>` and `quit` from its local console; it has no remote administrator endpoint.

Allow ChudLink through Windows Firewall on the intended private network. For narrowly scoped rules, inspect `scripts/firewall.ps1`, then run it from an administrator PowerShell. It is never run automatically by the build. Guest Wi-Fi, wireless client isolation, VPN routing and different subnets can prevent phone-to-PC access. Manual addressing works even when broadcast discovery is blocked.

## Android 14 and Oreo

Transfer `artifacts/android/ChudLink-0.1.0-Alpha.apk` to the phone using any file transfer method. Open it from your file manager and allow that file manager to install this APK. The alpha is signed with the local .NET Android development signing key and is intended for testing, not Play Store distribution. Later builds signed with another key require uninstall/reinstall. App backup is disabled, and uninstalling removes pairing and local notes.

The universal phone APK includes ARM32 (`armeabi-v7a`) and ARM64 (`arm64-v8a`) runtime libraries, declares minimum API 26 and uses the current installed Android target platform. The separate x64 Emulator configuration is only for testing. A successful build does not establish compatibility on every physical phone; see STATUS.md for actual tests performed.

Connect the phone to the same private network as the PC. Discover the host or type its private IPv4 address. Discovery suggests a certificate fingerprint. **Compare the entire fingerprint with the desktop** before using the pairing code; an attacker on the LAN can send false discovery responses. Generate a new two-minute code, submit it on the phone, and approve that exact phone in Devices. The code is consumed by the first valid request.

The phone automatically receives its assigned dashboard. On later launches it uses its cached dashboard and reconnects with the stored identity and credential. Reconnection backs off up to 15 seconds. When the app is in the background it disconnects and stops expensive updates; resume reconnects. Forget host removes the phone credential locally; revoke it on the desktop to invalidate the host credential too.

Phone Settings controls the app window's brightness, keeping the screen awake, Legacy/Balanced/Performance profile and accelerometer sharing. These features do not require system settings access, location, microphone or storage permission. Sensor sharing also needs per-device permission on the PC. Notes/tasks in Productivity Station are edited on the phone and stored locally.

## First custom dashboard

Open Presets and clone a station, or open Blank Workspace in Designer. Drag a widget onto the canvas, select it, edit properties and apply. Choose a press/change event and a known action from the visual action selector. PC launch/shortcut/sound/build actions must first be configured in Settings. Assigning a dashboard to a phone and choosing Save + send live pushes changes immediately.

Ctrl-click for multiple selection. Ctrl+C/V/D copy, paste and duplicate. Ctrl+Z/Y undo and redo. Drag the bottom-right handle to resize; use the snap checkbox for free positioning. Alignment, containers, layers, pages, colors, gradients, fonts, embedded images and portable .chudlink import/export are supported. Phone font names resolve to installed Android fonts, not Windows font files.

## First C# extension

In Developer Studio load Hello World, Build, review diagnostics, select the trust checkbox and Run. A separate worker registers a new dashboard. In Devices grant Approved PC actions to your phone and assign Hello World. Tap SAY HELLO. The message updates without replacing the dashboard or rebuilding the APK. Stop disables workers; extensions do not automatically run on startup.

Packages in `artifacts/extensions` contain compiled C#, source, manifest and settings schema. Install for review in Extensions. Inspect the author, source and requested permissions, then explicitly enable only trusted code. JSON API status requires a `url` setting and optional `property`; BuildMonitor requires a `buildActionId` from an approved host build action.

## Troubleshooting

If the fingerprint changes unexpectedly, stop and verify the PC's data folder has not been replaced. Reinstalling Windows or deleting host.pfx changes identity and requires pairing again. Do not disable certificate validation.

If no telemetry appears, verify the phone says CONNECTED, the host is running and the assigned widgets reference valid keys such as host.cpu or host.ram. The host samples only while at least one phone is connected; CPU needs a previous sample. GPU/FPS/thermal values are intentionally absent unless supplied by an actual configured provider.

If a button does nothing, inspect its event binding and the device permissions. An extension action requires both an enabled worker and Approved PC actions. Sound playback currently supports local WAV through the default Windows output. Master-volume control requires an active Windows audio endpoint. Diagnostics includes errors, worker console output and actual byte/update counters.
