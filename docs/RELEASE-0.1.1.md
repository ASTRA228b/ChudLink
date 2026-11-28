# ChudLink 0.1.1 Alpha

This release reduces background work in the Windows app and includes the Android connection and screen-fitting updates. It remains an alpha, with physical Android/ARM and long-duration testing still pending.

## Downloads

- **Windows x64 ZIP:** extract the complete folder and run `ChudLink.Desktop.exe`. The .NET runtime, extension worker, examples, and USB tools are included. Quit an older running copy before updating. This is a portable, unsigned Windows build.
- **Android APK:** install on Android 8.0/API 26 or later. It contains ARM32 and ARM64 libraries, target SDK 36, version code 2, and uses the same local development signing key as earlier test builds. It can update a matching earlier install. It is a test APK, not a Play Store or production-signed release.
- **SHA256SUMS.txt:** checksums for the uploaded build files.

## Windows performance changes

- Hidden/minimized windows and static pages stop their UI refresh timer. Visible live pages refresh every two seconds and avoid resetting unchanged text and device lists.
- The host samples only metrics used by dashboards due for an update, plus CPU/RAM when the visible Overview requests them. No connected phones means no telemetry probes; idle housekeeping waits for activity.
- Disk snapshots and network-adapter discovery are cached. Process readers are reused. Unused audio probes are skipped; COM resources are released on both success and failure.
- USB device discovery uses a bounded localhost ADB request instead of repeatedly spawning `adb.exe`. Stable transport checks are cached, while replugging, disconnect recovery, explicit retry, and shutdown cleanup remain supported.
- Slow HTTP integrations run outside the dashboard update loop, with limited concurrency, bounded responses and whole-request timeouts.
- Extension IPC reads buffered blocks instead of issuing one asynchronous read per character, while preserving the message-size limit.
- The standalone host uses workstation garbage collection for this small local workload.

Performance measurements and their limits are documented in `docs/PERFORMANCE.md`. They are host/protocol microbenchmarks, not a guarantee about gaming FPS or every PC workload.

## Phone updates

The PC starts its local host automatically on an active network. Phone setup discovers PCs, hides advanced manual fields, and shows numbered Wi-Fi/USB guides. USB mode requires a data cable, Android USB debugging, and first-time PC authorization/approval; later cable connections reconnect automatically. Wi-Fi does not require developer options.

Android controls now fit the actual screen width with readable cards, adaptive columns and scrolling. The connection bar uses density-aware sizing, larger text, and safe areas for system bars/cutouts. The original Designer canvas is available in Phone Settings.

## Linux and macOS

Separate development drafts are prepared for Linux x64/ARM64 and macOS Intel/Apple Silicon. They remain in a **GitHub draft release**, not this public Windows/Android release. They have been cross-published on Windows and have not been executed or visually verified on their target operating systems. macOS bundles are unsigned and unnotarized. See `docs/PORTABLE-DRAFTS.md` for included features and missing platform integrations.

## Validation and known limits

Windows solution and Android Release builds are checked, along with automated behavior/integration/performance regressions. The APK is inspected for its version/ABI/SDK metadata, v2/v3 signatures and 16KB ZIP alignment. Detailed current results are recorded in `docs/PERFORMANCE.md` and `artifacts/test-results.json` during the source build.

Native Android screen/connection observations from API 26–34 remain development/emulator evidence. This performance release does not claim a new complete native-device regression. No physical ARM phone was available. C# extensions are trusted executable code; worker limits are not a secure sandbox. Windows-only audio, OBS, advanced editor and provider limitations remain as described in `docs/STATUS.md`.
