# ChudLink 0.1.0 Alpha — build report

Built on Windows on **27 November 2026** with .NET SDK **10.0.404**, .NET Android workload **36.1.2**, Android API **36** build/target tools and a repository-local JDK. The Windows desktop, host and worker distributions are self-contained x64 builds with .NET 10.0.15 included.

## Immediately usable files

| Output | Repository location |
| --- | --- |
| Windows desktop | artifacts/windows/ChudLink.Desktop.exe |
| Standalone Windows host | artifacts/host/ChudLink.Host.exe |
| Universal test APK | artifacts/android/ChudLink-0.1.0-Alpha.apk |
| C# SDK packages | artifacts/sdk/ChudLink.SDK.0.1.0-alpha.nupkg and ChudLink.Protocol.0.1.0-alpha.nupkg |
| Nine example extensions | artifacts/extensions/*.chudext |
| Remade logo | assets/branding/chudlink-logo-v2.png |
| Automated results | artifacts/test-results.json |

Keep each Windows folder together: the EXE needs its included runtime, libraries, assets and worker folder. The source solutions, build scripts and complete documentation are in the repository. [Installation steps](INSTALL.md) explain how to copy the APK onto an Android 14 phone and pair it without root or developer options.

APK SHA-256: `C70EEACFDA1E47E69215772E62C10020DA899AE06096227605FC08EAFAB43630`.

## Verified results

- Windows solution Release build: succeeded, 0 errors and 0 warnings.
- Android universal Release build: succeeded, 0 errors and 0 warnings.
- Automated behavioral/integration suite: **22/22 passed**. This includes real TLS, exact certificate pinning, pairing approval, independent device credentials, multiple WebSocket clients, telemetry, touch actions, device mesh, Roslyn compilation/diagnostics, a separately executed C# worker, revocation and malicious ZIP path rejection.
- Nine example sources compiled and exported as .chudext packages. SDK and protocol NuGet packages were produced.
- The self-contained WPF app was launched and visually inspected. Overview branding, dark controls, Designer widget creation/duplication/undo, C# editor highlighting/line numbers, successful Studio compilation and navigation were exercised.
- The APK's compiled manifest was inspected with Android aapt: **minimum SDK 26**, **target SDK 36**, package ai.chud.chudlink, version 0.1.0 Alpha. Native libraries contain both **armeabi-v7a** and **arm64-v8a**.
- Android apksigner verification passed (v2 and v3 signatures). zipalign 16 KB-page alignment verification passed. Signing uses the local development key, suitable for this testing APK.

## Native Android testing

These are **x64 emulator development checks**, separate from the ARM universal APK inspection and from simulated phone-transport tests. Startup means installation, successful native activity launch and a running connection screen, not a comprehensive regression on that API.

| API | Android | Image | Observed result |
| --- | --- | --- | --- |
| 26 | 8.0 Oreo | Google APIs x64 | Startup; pinned pairing; assigned dashboards; actual host telemetry; shared-counter touch; cached dashboard and reconnect/resume |
| 27 | 8.1 Oreo | AOSP x64 | Install and native connection-screen startup |
| 29 | 10 | AOSP x64 | Install and native activity startup |
| 30 | 11 | AOSP x64 | Install and native activity startup |
| 31 | 12 | AOSP x64 | Install and native connection-screen startup; screenshot inspected |
| 33 | 13 | AOSP x64 | Install and native connection-screen startup; screenshot inspected; no crash entries |
| 34 | 14 | Google APIs x64 | Startup; pinned pairing; real telemetry/graphs; shared-counter touch; reconnect; Hello World C# extension button and incremental text update |

API 26 and API 34 were connected simultaneously. A touch on each phone produced a shared counter value of 2 on the host and phones. On API 34, tapping SAY HELLO executed the approved C# extension worker action and changed the phone text to **Hello from your PC!** without replacing the APK.

No physical phone was connected, and no ARM32/ARM64 application execution was performed. Physical-device compatibility, battery life, sustained thermal behavior, GPU telemetry and gaming anti-cheat interoperability have not been established. The app does not inject into games. Emulator memory samples are not representative ARM performance measurements.

## What remains

The working vertical slice, editor, C# development flow, preset starters, package lifecycle and local multi-device platform are present. The entire original product specification is **not complete**. Notable remaining work includes OBS integration, microphone/per-app audio/output routing, additional hardware/FPS providers, richer weather/calendar/AI integrations, responsive breakpoints/nested layouts, font-file loading, full project/language-server tooling, advanced custom renderers, accessible canvas virtual nodes and signed extension distribution. [STATUS.md](STATUS.md) gives the exact feature and limitation list.

Extension processes have enforced resource limits and permission-checked host IPC, but **are not a security sandbox**. Enable only trusted executable C# code. Test auto-approval exists solely in the explicit emulator-test mode of ChudLink.Tests and is absent from the production host/desktop.
