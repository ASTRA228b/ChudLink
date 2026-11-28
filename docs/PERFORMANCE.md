# Performance checks for ChudLink 0.1.1 Alpha

Measured on 27 November 2026 using the same host/protocol harness against captured previous binaries and optimized binaries. Each scenario warmed up for 2.5 seconds, then ran for 15 seconds in a separate process per version. One paired client used the Balanced profile. CPU percentages divide process CPU time by elapsed time and 16 logical CPUs. This is a short, single-machine host benchmark, not a WPF rendering, gaming-FPS, physical-phone, or long-duration test.

| Workload | CPU ms before | CPU ms after | Managed bytes before | Managed bytes after | Working set MiB before / after |
| --- | ---: | ---: | ---: | ---: | ---: |
| idle | 343.8 | 250.0 | 48,912 | 8,200 | 54.3 / 53.9 |
| control | 687.5 | 312.5 | 1,753,104 | 40,728 | 75.0 / 69.3 |
| hardware | 734.4 | 546.9 | 1,715,952 | 455,408 | 89.6 / 73.6 |
| audio | 671.9 | 156.2 | 1,876,424 | 67,144 | 82.8 / 85.9 |

Hardware Station used approximately 26% less process CPU time and 73% fewer managed allocation bytes in this run. Control Deck and Audio Station skipped most unused probes. Absolute CPU use was low in both runs (under 0.31% when normalized across all 16 logical CPUs). The broad whole-PC lag report was not reproduced by this small workload. These measurements support reduced background work, not a guarantee that every source of PC lag is resolved. Working set varies; the audio case was slightly higher after optimization, so this release does not claim uniformly lower resident RAM.

The benchmark excludes certificate generation, pairing, and initial startup from timed measurements. Both harness processes used workstation GC. This comparison therefore does not measure the separate standalone-host change from server to workstation GC. Temporary host data is not included in releases. Reproduction instructions are in tests/ChudLink.Performance/README.md.

## Deterministic checks

- 37/37 behavior/integration/regression tests passed, including demand-only telemetry, cached probes, bounded buffered IPC, slow-service isolation, smart-socket framing, stable USB command suppression, and rapid stop/start/disposal.
- Ten stable background USB scans produced zero additional device-path, reverse-list, or activity-launch commands in the injected-command test; scheduled tunnel repair, unplug/replug and manual retry still passed. Stable production USB polling uses local TCP, with an adb reverse-list health command at most every 15 seconds per phone.
- Source-level UI scheduling: hidden/minimized/static pages have no refresh timer; visible Overview/Devices/Diagnostics refresh every two seconds and skip unchanged content.
- Windows Release solution and self-contained packaging succeeded. The packaged WPF executable was launched in smoke-test mode, remained responding, and reported the expected ChudLink window. Cold startup working set was about 197 MiB after five seconds; no Windows before/after rendering claim is made.
- Android Release succeeded with zero warnings/errors. APK versionCode 2 / versionName 0.1.1 Alpha, minimum SDK 26, target SDK 36, ARM32/ARM64 libraries, v2/v3 signing, and 16 KB alignment verified. The same development signing key is retained.
- Prior Android emulator pairing and screen-fitting checks are described in docs/BUILD-REPORT.md. No physical ARM phone was tested for this release.
- Linux/macOS drafts have a separate status and target-OS execution has not been tested. See docs/PORTABLE-DRAFTS.md.

## Implementation notes

Host telemetry follows due device profiles and dashboard data keys; CPU/RAM can additionally be requested by the visible Overview. Disk data is cached for up to 30 seconds, network-interface discovery for 10 seconds, and audio failures back off for 10 seconds. Metrics are actual OS samples; unsupported values are omitted. Slow HTTP checks use two concurrent slots, a 3-second whole-request deadline and a 64 KiB body limit, outside the main telemetry loop. The IPC reader retains a 256 KiB line bound while reading 4 KiB blocks.
