# ChudLink 0.1.0 Alpha — historical build report

For the current 0.1.1 performance release, see [release notes](RELEASE-0.1.1.md) and [current checks and measurements](PERFORMANCE.md). The matrix below records earlier emulator development observations; it is not a full native-device regression for 0.1.1.

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

Keep each Windows folder together: the EXE needs its included runtime, libraries, assets and worker folder. The source solutions, build scripts and complete documentation are in the repository. [Installation steps](INSTALL.md) explain installation, Wi-Fi pairing without developer options, and optional USB setup with authorized USB debugging.

APK SHA-256: `09AB23A2D2A734BA960B82F3113DBC39D5D83CBCCA31BE22A3538DF7520F2857`.

## Verified results

- Windows solution Release build: succeeded, 0 errors and 0 warnings.
- Android universal Release build: succeeded, 0 errors and 0 warnings.
- Automated behavioral/integration suite: **31/31 passed**. This includes real TLS, exact certificate pinning, pairing approval, independent credentials, multiple WebSocket clients, telemetry, touch actions, device mesh, Roslyn compilation/diagnostics, worker execution, revocation and malicious ZIP rejection. Connection checks cover one-use USB bootstrap/PC approval, LAN rejection of USB secrets, the simultaneous loopback listener, tunnel repair/replug/cleanup, unauthorized/missing-APK/wireless-ADB handling, and pinned saved-endpoint fallback. Four display checks cover 320–1200dp widths, readable card/touch geometry, scrolling hit tests, unchanged Designer documents, no overlapping cards, landscape, large font scale, blank views, and original layer-aware layout.
- Nine example sources compiled and exported as .chudext packages. SDK and protocol NuGet packages were produced.
- The self-contained WPF app was launched and visually inspected. Overview branding, dark controls, Designer widget creation/duplication/undo, C# editor highlighting/line numbers, successful Studio compilation and navigation were exercised.
- The APK's compiled manifest was inspected with Android aapt: **minimum SDK 26**, **target SDK 36**, package ai.chud.chudlink, version 0.1.0 Alpha. Native libraries contain both **armeabi-v7a** and **arm64-v8a**.
- Android apksigner verification passed (v2 and v3 signatures). zipalign 16 KB-page alignment verification passed. Signing uses the local development key, suitable for this testing APK.

## Native Android testing

Screen-fitting update (27 November): Android 14 x64 was checked at 1080×2400 with density 440 for portrait and 800×360 with density 160 for landscape. Full-width/narrow-screen cards, two-column landscape, visible connection text, scroll indicators and page footer were visually inspected. A 360×800 run used Android font scale 1.3 and the enabled tall-cutout emulator overlay. Scrolling Control Deck did not trigger its counter; tapping the counter after scrolling produced the host value 1. No ChudLink AndroidRuntime exception was recorded in this run. Resizing the emulator once triggered a System UI ANR, which recovered before the completed checks. Captures are in artifacts/phone-layout-*.png; this remains emulator evidence rather than physical-phone validation.

Connection update (27 November): the new Android 14 x64 build was installed, paired through an actual ADB reverse tunnel to a test host, and showed **CONNECTED · USB** with live Hardware Station telemetry. Removing the reverse mapping exercised automatic tunnel repair and saved-token reconnection. Test-only code explicitly includes the emulator and auto-approves only SDK emulator identities; production code does neither. Lifecycle testing exposed queued callbacks on a disposed native dashboard peer; explicit disposal during screen changes was removed. Final desktop/phone guide interaction checks were stopped when the user pressed Escape; subsequent work was limited to builds and automated checks. Physical USB hardware and the Windows elevation prompt remain untested. The latest full desktop build is also available in `artifacts/windows-easy-connect` so it can be opened after quitting any running older copy.

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
