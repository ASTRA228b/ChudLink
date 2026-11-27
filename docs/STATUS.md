# ChudLink 0.1.0 Alpha status

This release implements a working programmable companion foundation. It does **not** implement every feature in the original product specification. The concrete scope and limitations below are part of the alpha, not hidden behind placeholder controls.

## Implemented

- Shared C# .NET 10 solution with all eleven requested platform projects and nine example extension projects. Separate Windows and Android solutions support independent development.
- Self-contained Windows x64 desktop and standalone host; native Android application with minimum SDK 26, target SDK 36 and ARM32/ARM64 universal APK.
- Selected private-interface HTTPS/WSS host, manual fallback, UDP discovery, verified SHA-256 certificate pin, one-use two-minute pairing codes, explicit local approval, independent persistent credentials, revocation and protocol negotiation.
- Multiple connected devices, rename, permissions, groups, battery/orientation/capability/version reports, measured RTT, heartbeats, bounded payloads/rates, connect/send deadlines and reconnect backoff.
- Native full-screen Android canvas, portrait/landscape fitting, dashboard caching, local notes/tasks, timer/stopwatch/clock, touch feedback, brightness, keep-awake option, sensor opt-in and profile-dependent telemetry. API 29+ severe thermal warnings reduce update rate and brightness and allow screen sleep; thermal APIs are isolated from API 26 code paths.
- Actual Windows CPU, RAM, system-drive usage, network interface throughput, host process memory/uptime and master audio volume. Unsupported GPU, VRAM, temperature and FPS fields are omitted.
- Eleven editable station starters with working built-in widgets and host-configured action/data bindings. Soundboard and Developer Station are deliberately configurable workspaces: configure approved WAV/build actions locally before binding them.
- Designer drag/drop, drag/resize handles, snap/free positioning, layers, basic containers, left/top/center alignment, undo/redo, duplicate/copy/paste, pages, theme colors, gradients, font-family names, embedded images, rescaled resolution presets, JSON import/export, cloning and live publish to assigned phones.
- Standard widgets for button/toggle/slider/dial/progress/text/image/clock/graph/gauge/terminal/status/timer/counter/container/navigation/notification, plus extension-registered declarative fallbacks. A custom widget can use `Properties["type"]` to name a registered fallback; it ships to Android as standard portable widget data.
- C# SDK builders, attributes, custom actions/providers/widgets/presets, text/value/color updates, device snapshots, touch/sensor events, notifications, per-device assignment, shared variables and approved PC actions.
- Developer Studio with AvalonEdit syntax highlighting/line numbers, nine templates, Roslyn compilation, diagnostics, source import, compiled source/package export, Run/Stop/Reload and access to live phone publishing. Edits invalidate the last build. Worker logs appear in Diagnostics.
- .chudext manifests/assemblies/source/assets/settings schemas; review-only installation, explicit trusted enable, disable, uninstall and manual update by reinstall. Configuration is editable as a string dictionary.
- Worker Job Objects: 256 MB memory, 20% CPU, one active process, kill-on-close and a four-hour host-enforced lifetime. IPC commands are permission-checked. **These are not a secure sandbox.**
- Host-routed mesh switching/shared counter, independently assigned dashboards and optional foreground-process context rules with a 15-minute manual override.
- Opt-in Personal integration templates without personal endpoints or secrets. Original ChudGPT branding plus the remade ChudLink logo and generated decorative artwork.

## Incomplete or intentionally limited

- No OBS adapter, approved multi-step macro editor, microphone controls, per-application audio sessions, output-device selection, compressed sound import, custom sound-device routing or game FPS adapter.
- GPU/VRAM/temperature providers are not bundled. Weather, calendar, external model request/inference statistics and richer ChudAI adapters need configured integrations or future providers.
- Responsive design scales one coordinate canvas into each orientation and can letterbox. Separate breakpoint layouts, nested container layout constraints, automatic distribution, native font-file import, background-image tooling and a complete visual event graph are unfinished. The alpha has a direct event/action selector instead.
- Designer desktop previews are schematic. Live publish previews on the actual phone; there is no phone framebuffer mirror. Blank/custom stations are not automatically populated with machine-specific paths or services.
- Studio imports one C# source file, not arbitrary multi-project solutions. Visual Studio/Rider/VS Code remain available for full projects. There is no full language server, dedicated debugger, integrated NuGet manager or embedded native phone preview.
- Custom widgets use standard declarative fallbacks. Arbitrary Android native plugins, a host-native custom renderer and advanced phone functionality require future protocol/runtime work.
- Configuration schemas travel with packages; automatic schema-generated settings forms, package signatures and automatic extension updates are unfinished. Extensions never auto-enable at startup. Updates are explicit uninstall/reinstall.
- Shared variables are in-memory, context rules match process names and build output currently exposes the most recent line to phone widgets (full bounded output is in host logs). There is no cloud sync or task scheduler.
- The canvas does not yet expose individual widgets as Android accessibility virtual nodes. Phone labels/touch targets should be customized to the actual display size.
- IPv4 private LAN operation is supported. IPv6, mDNS, USB transport and internet relay are not implemented. Discovery needs a reachable local broadcast domain; manual entry remains available.

## Verification

The automated executable test harness exercises real loopback TLS/WebSocket connections using the exact pure-C# phone transport source. It checks presets/portable JSON, invalid inputs, history, credentials, certificate pinning, pairing approval, independent devices, live telemetry, actions, device mesh, Roslyn diagnostics, worker events, trust gating, revocation, ZIP traversal and custom-widget fallback materialization. Results are written to artifacts/test-results.json.

Native emulator observations are kept distinct from transport simulation. Android 8.0/API 26 and Android 14/API 34 have been paired to the same host, received real telemetry and updated the shared counter through touch. Android 14 also ran the Hello World C# extension end to end. Additional API-level installation/startup checks are recorded in the build report. These are x64 emulator tests, not ARM hardware tests.

ARM32 and ARM64 are packaged and inspected in the universal APK. No physical Android phone was attached during development, so physical-device, ARM runtime, battery-life and long-duration thermal compatibility are **not claimed**. The APK uses development signing for installation testing; production signing/distribution remains a release task.

Runtime checks caught and fixed the omitted target SDK, JNI marshal-method startup failure, certificate lifetime during Kestrel startup, Designer click/drag threshold, dark theme contrast and stale Studio build caching. LLVM Android marshal methods are explicitly disabled for this untrimmed alpha; normal managed JNI registration is used.
