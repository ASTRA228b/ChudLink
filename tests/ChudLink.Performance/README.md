# Host workload benchmark

This is a small real TLS/WebSocket host benchmark, not a WPF rendering or gaming-FPS test. It runs idle and one paired Balanced-profile client on Control Deck, Hardware Station and Audio Station. Each case warms up for 2.5 seconds before a timed measurement. It records CPU time, logical-CPU-normalized CPU percentage, managed allocations, working set/private memory, GC counts and outbound bytes. Startup, certificate creation and pairing are outside the measured window. Results depend on the PC and other running workloads.

Build the Host first, then build this project against its binaries. `PerfBinaries` may point to a captured older build to run the same source against a baseline. Capture the entire dependency folder and run each version in a separate process. Do not include temporary host data/certificates in public artifacts.

```powershell
.tools/dotnet/dotnet.exe build src/ChudLink.Host -c Release
.tools/dotnet/dotnet.exe build tests/ChudLink.Performance -c Release -o artifacts/perf-run
.tools/dotnet/dotnet.exe artifacts/perf-run/ChudLink.Performance.dll 15 artifacts/performance.json
```

The performance harness is separate from the Windows solution and the deterministic regression suite. See `docs/PERFORMANCE.md` for the captured comparison and limits.
