# Architecture and protocol

The desktop embeds HostRuntime. The standalone console embeds the same implementation. A per-data-directory file lock prevents both from operating on the same state simultaneously. Store persists settings/devices atomically, dashboards as .chudlink JSON and a stable self-signed RSA certificate. Closing WPF hides to tray; quitting disposes the host and extension workers.

The Android companion uses native Android views for setup/settings and a C# Canvas view for dashboards, with no WebView or MAUI dependency. By default, phone dashboards arrange readable cards using the available density-independent width: one column on narrow phones, additional columns on wider screens, and vertical scrolling rather than shrinking every widget. The same placements drive drawing, scrolling and touch hit testing. Android font scale is respected; the connection bar uses dp sizing with a wrapping 14sp label and a 48dp settings target. A safe-area frame accounts for system bars and display cutouts. Phone Settings can disable automatic fitting to retain the original uniformly scaled Designer canvas, containers and layers. Automatic fitting never changes the stored dashboard. Independent authored breakpoint compositions remain future work.

## Wire protocol v1

HTTPS GET /health returns only public host/version data. POST /pair accepts protocol, code and persistent device identity. A single-use code lives for two minutes and creates a pending request. A random 256-bit ticket authorizes GET /pair/status via the Authorization header. The desktop directly approves or denies through in-process methods; no remote approval endpoint exists. Approval issues a random 256-bit device token once. The host stores only its SHA-256 hash.

WSS /connect requires Bearer token and X-ChudLink-Protocol: 1. Every text message is an envelope with type and JSON payload. Device message size is at most 16 KB; host dashboard messages at most 1 MB. Connections are serialized by a send gate, time-limited on send, heartbeat-monitored and rate-limited. Protocol mismatch fails before socket upgrade. Unknown messages close the device connection.

| Direction | Types |
| --- | --- |
| Phone → host | hello, heartbeat, pong, touch, sensor |
| Host → phone | dashboard, patch, heartbeat, ping, notification |

The initial dashboard is declarative. Subsequent widget patches identify only changed properties; graph samples are also sent when values repeat. Legacy samples at 0.2 Hz, Balanced at 0.5 Hz and Performance at 1 Hz. Local clocks/timers draw at 1 Hz while the view is attached. No per-frame rendering loop exists. CPU, memory, system-drive and interface throughput are sourced from Windows APIs. Unavailable values are omitted.

Discovery sends CHUDLINK_DISCOVER_V1 to UDP 47832; a private-subnet host replies with name, HTTPS address, SHA-256 pin and protocol. Replies are not authenticated; first pairing explicitly depends on comparing the complete fingerprint on both screens. Discovery failure has no effect on manual connection.

## Extensibility

USB mode uses authorized Android Platform Tools rather than a second dashboard protocol. The optional Windows watcher accepts physical USB transports only (test code can explicitly include emulators), creates `adb reverse tcp:47831 tcp:<host-port>`, and launches the phone's stable `ai.chud.chudlink/.MainActivity` with a fingerprint, LAN fallback and short-lived bootstrap secret. The phone uses the same pinned HTTPS/WSS client at loopback. Bootstrap secrets are one-use, 256-bit, expire after two minutes, and are rejected on LAN requests; first-time approval remains local to the PC. An existing pinned credential reconnects without pairing again. The watcher restores missing tunnels, launches once per attachment/repair, reports unauthorized/offline/missing-APK conditions, ignores wireless ADB, and removes only its own mappings when disabled. The PC app restores its existing window when launched again instead of making the user find its tray icon.

Phone setup searches automatically. Discovered endpoints are validated and displayed as choices; manual fields are collapsed. First-time identity confirmation remains explicit. Reconnect may try a saved LAN fallback or discover a changed address only for the already-pinned certificate. Desktop automatic startup prefers a usable private network over a stale loopback setting. Numbered guides and a user-initiated, administrator-approved private-subnet firewall helper are part of the connection UI.

The SDK is independent of transport. Builders create portable dashboards; IChudContext emits dashboard registrations, patches, actions, providers, notifications and mesh commands through bounded JSON lines. Custom widget registration supplies a standard declarative fallback; this alpha does not load arbitrary native phone renderers. There is no Android assembly loading or game injection.

Extensions run in ChudLink.ExtensionHost workers. Before StartAsync, the worker receives a settings dictionary and current connected-device snapshot. The host checks requested capabilities for each IPC command, dashboard/action namespaces, widget limits and message rates. Windows Job Objects cap a worker at 256 MB, 20% CPU and one active process, and terminate it when its job handle closes. Host checks enforce a four-hour worker lifetime. These are resource controls, not a secure sandbox.

All device-to-device traffic routes through the host. Built-in mesh actions require explicit mesh permission and a matching nonempty group. Trusted SDK extensions with mesh permission may address any paired device. Shared variables are namespaced for extensions. Device telemetry/settings remain independent. Foreground process rules are local, optional and paused for 15 minutes after manual assignment.

## Build separation

The Windows solution builds without Android. The Android solution uses the same Protocol source and separate native application. Android depends only on the protocol assembly and runtime. SDK NuGet packages include the protocol package as a dependency. Public and Personal use one codebase; Personal enables descriptive integration templates without embedding personal configuration.
