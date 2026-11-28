# Installation and connection

## Windows

Use the self-contained x64 desktop folder from `artifacts/windows`. Start `ChudLink.Desktop.exe`. The host starts automatically on the active private IPv4 network. Overview shows the actual address; Settings can disable automatic startup. TCP 47831 serves pinned TLS on the selected address and loopback for USB; UDP 47832 serves discovery. The host rejects public-network peers. There is no public-internet binding, router configuration or mandatory online service.

Closing the window hides it in the tray. Double-click the tray icon to reopen it. Choose Quit ChudLink to close connections and stop all workers. Keep only one host instance for the same data directory. The standalone `artifacts/host/ChudLink.Host.exe <private-IP>` offers `pair`, `pending`, `approve <ticket>`, `deny <ticket>`, `devices`, `revoke <id>` and `quit` from its local console; it has no remote administrator endpoint.

In Devices, click **Allow Wi-Fi connection** and approve the Windows administrator prompt if needed. It adds rules only for ChudLink's executable/ports, Private networks and LocalSubnet peers; it does not disable Windows Firewall. Setup runs only when clicked. The optional administrator-run `scripts/firewall.ps1` also remains available. Guest Wi-Fi, wireless client isolation, VPN routing and different subnets can prevent phone-to-PC access. Manual addressing works when broadcast discovery is blocked. Both apps include a numbered **Connection guide** with setup and troubleshooting steps.

## USB: pair once, then plug in

1. Install the latest phone APK. In the PC's Devices page click **Connect by USB**.
2. On Android, tap **Settings > About phone > Build number** seven times (some manufacturers put it under Software information). Enable **USB debugging** in Developer options.
3. Plug in a **data** cable, unlock the phone, and accept Android's **Allow USB debugging** prompt for your own PC. Some phones require the manufacturer's Android USB driver on Windows.
4. ChudLink opens automatically on the phone. Compare the PC fingerprint and confirm it. Approve that phone in Devices on the PC once. No address, fingerprint or pairing-code typing is needed.
5. Leave USB mode enabled and the PC app running. Future replugging opens the app and reconnects with the saved credential. Use **Retry USB** after installing a newer APK or cancelling setup. Turning USB off removes ChudLink's tunnel.

This alpha's USB mode uses authorized ADB reverse and requires USB debugging. Wi-Fi pairing does not. USB needs no Wi-Fi, firewall rule, root or bootloader changes. The app never enables debugging, approves Android prompts, installs APKs, enables wireless ADB or kills another app's ADB server. `build.ps1` copies Google Platform Tools and NOTICE.txt from the local SDK into local Windows builds; those binaries are not committed to Git. USB mode is remembered across app launches; it does not add a Windows startup task.

## Android 14 and Oreo

Transfer `artifacts/android/ChudLink-0.1.1-Alpha.apk` to the phone using any file transfer method. Open it from your file manager and allow that file manager to install this APK. The alpha is signed with the local .NET Android development signing key and is intended for testing, not Play Store distribution. Later builds signed with another key require uninstall/reinstall. App backup is disabled, and uninstalling removes pairing and local notes.

The universal phone APK includes ARM32 (`armeabi-v7a`) and ARM64 (`arm64-v8a`) runtime libraries, declares minimum API 26 and uses the current installed Android target platform. The separate x64 Emulator configuration is only for testing. A successful build does not establish compatibility on every physical phone; see STATUS.md for actual tests performed.

For Wi-Fi, put both devices on the same private network. Open Devices on the PC for its two-minute code. The phone searches automatically and selects a single discovered PC for you. Enter the six-digit code, **compare the entire fingerprint with the desktop** when asked, then approve that exact phone in Devices. Discovery is untrusted; LAN attackers can send false responses. The code is one-use. Address and fingerprint fields remain under **Manual connection**.

The phone receives its assigned dashboard automatically. Later launches use its cached dashboard and stored credential. Reconnection tries saved endpoints, with five-second connect deadlines/backoff, and can rediscover a moved host only when its fingerprint matches the saved pin. USB setup also saves the PC's LAN address when available. Backgrounding disconnects and stops expensive updates; resuming reconnects. Forget host removes the credential locally; revoke on the PC to invalidate it there too.

Phone Settings controls the app window's brightness, keeping the screen awake, Legacy/Balanced/Performance profile and accelerometer sharing. These features do not require system settings access, location, microphone or storage permission. Sensor sharing also needs per-device permission on the PC. Notes/tasks in Productivity Station are edited on the phone and stored locally.

Dashboard controls fit the phone's density-independent screen width automatically. Narrow phones show full-width cards; wider/landscape screens show more columns. Swipe vertically for additional widgets; scrolling does not activate the tile under your finger. The connection bar stays visible and accommodates system bars, camera cutouts and longer status messages. To retain an exact custom canvas composition, turn off **Fit dashboard controls to this screen** in Phone Settings. The original Designer document is unchanged.

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
