# ChudLink — By ChudAI

![ChudLink companion concept artwork](assets/branding/chudlink-companions.png)

**0.1.0 Alpha** · Native C# PC companions · Windows 10/11 · Android API 26+

ChudLink connects Android phones to a Windows PC over an authenticated, certificate-pinned local connection. The native WPF desktop manages devices, dashboards, visual editing, C# development and explicitly enabled extension workers. The native .NET Android app draws declarative dashboards directly onto an Android canvas; dashboard changes do not require another APK.

This is a working alpha with a deliberately limited feature set. Read [feature status](docs/STATUS.md) for implemented behavior and remaining work. Decorative artwork is generated concept art, not a screenshot.

## Test immediately

1. Open `artifacts/windows/ChudLink.Desktop.exe` from this build. Keep the entire directory together. The self-contained distribution includes .NET; it needs no runtime install.
2. In **Overview**, select your private LAN IPv4 address and choose **Start host**. The app binds only that address. If Windows Firewall prompts, allow the app on your **private** network. An optional administrator-run `scripts/firewall.ps1` creates rules scoped to this app and the local subnet.
3. Copy `artifacts/android/ChudLink-0.1.0-Alpha.apk` to the phone and open it. Allow APK installation from the file manager used to open it. No root, developer options or USB debugging is required for normal installation.
4. Put the PC and phone on the same trusted Wi-Fi/LAN. In **Devices**, generate a pairing code. On the phone, use discovery or enter the displayed HTTPS address and complete SHA-256 fingerprint. Compare **every character** against the desktop. Enter the expiring code.
5. Select the pending phone on the desktop and approve it. The default Hardware Station appears with real memory, CPU, storage and network samples.
6. Assign Control Deck and tap **Shared counter** to confirm a button round trip. Media controls work when media is playing. For C# actions, enable **Approved PC actions** on that phone; then build and run the Hello World template in Developer Studio and assign its dashboard.

Discovery is untrusted convenience. It never replaces fingerprint verification. Pairing codes expire after two minutes, are one use, and require explicit host approval. Credentials can be revoked in Devices. No account or cloud service is required.

## Source build

```powershell
./scripts/bootstrap.ps1 -Android
./scripts/build.ps1 -SelfContained
```

`ChudLink.slnx` contains all eleven platform projects and nine example projects. `ChudLink.Windows.slnx` excludes Android so Windows-only development does not require an Android workload; `ChudLink.Android.slnx` contains the native phone app and shared protocol. Use .NET 10 SDK 10.0.404 or a compatible .NET 10 feature band. The bootstrap script installs tools into ignored `.tools/` and installs Android dependencies from their upstream sources. SDK license acceptance is explicit in that script.

Build Windows only with `./scripts/build.ps1 -SkipAndroid -SelfContained`. Run automated tests with `.tools/dotnet/dotnet.exe run --project tests/ChudLink.Tests -c Release` after building the worker. Tests use temporary data, loopback TLS and two simulated clients; they are not physical Android tests. `artifacts/test-results.json` records the result.

## Layout

| Project | Responsibility |
| --- | --- |
| ChudLink.Protocol | Versioned dashboards, messages and widget definitions |
| ChudLink.Core | Device/settings models, private-interface checks, credentials, editor history |
| ChudLink.Host | ASP.NET Core HTTPS/WebSocket host, discovery, device mesh, telemetry, approved actions |
| ChudLink.Desktop | Native WPF management, Designer, Roslyn Developer Studio, tray |
| ChudLink.Android | Native Android connection UI and efficient canvas renderer |
| ChudLink.SDK | Approachable builders, attributes and extension context API |
| ChudLink.Presets | Eleven editable dashboard starters |
| ChudLink.Extensions | Compiler and reviewed package installation |
| ChudLink.ExtensionHost | Separate executable for trusted extensions |
| ChudLink.Personal | Opt-in integration templates without credentials or assumed endpoints |
| ChudLink.Tests | Automated behavior and local integration tests |

## Developer docs

- [Install, connect and troubleshoot](docs/INSTALL.md)
- [SDK and examples](docs/sdk/README.md)
- [Architecture and protocol](docs/ARCHITECTURE.md)
- [Security and trust](docs/SECURITY.md)
- [Feature and compatibility status](docs/STATUS.md)
- [Build outputs and verified test matrix](docs/BUILD-REPORT.md)
- [Branding and image-generation prompts](docs/BRANDING.md)

Host data is under `%LOCALAPPDATA%/ChudAI/ChudLink`. Do not publish that folder: it contains the host's private certificate, paired-device credential hashes, personal endpoints, dashboards and settings. Public release files are built only from source and platform dependencies.
