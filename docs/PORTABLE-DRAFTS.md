# Linux and macOS drafts

These are development drafts, separate from the supported Windows alpha. They
have been compiled and cross-published on Windows; they have **not been run or
visually checked on Linux or macOS**. The macOS app is unsigned and unnotarized.
Native OS validation and packaging remain release requirements.

## What exists

`src/ChudLink.Portable` is an Avalonia native desktop UI, backed by
`ChudLink.Portable.Host`. The portable host links the same HostRuntime and Store
source used by Windows, with explicit portable adapters and an isolated data
folder named `ChudAI/ChudLink-Portable-Draft` under the platform's local app-data
directory. The exact directory is displayed in the guide tab.

The draft provides private LAN host startup, pinned TLS/WebSocket pairing,
expiring codes, explicit PC approval, persistent/revoked credentials, phone
renaming, dashboard assignment, JSON import/export, logs, supported presets,
shared counters, and host-routed mesh. A native phone can use the existing
Android app. Real process memory, process uptime, network throughput and
filesystem usage are available; unsupported machine-wide CPU/RAM/audio values
are omitted, rather than generated or shown as working measurements.

Unsupported native actions raise PlatformNotSupportedException. There is no
portable extension worker security model yet. Visual Designer, C# Studio,
extension workers, media/audio/global shortcuts, USB auto-connect, tray service,
OS startup registration, automatic firewall setup and native installers are not
included. The draft's controls and presets do not advertise these features as
available. Custom imported dashboards may still contain unsupported actions;
those are rejected and recorded in diagnostics.
Imports that use a reserved starter-preset ID receive a new `portable.import.*`
ID so they survive restarting the draft instead of replacing a built-in board.

## Packages

`scripts/build-portable.ps1` creates self-contained drafts for `linux-x64`,
`linux-arm64`, `osx-x64`, and `osx-arm64`. Linux packages contain an executable
folder. macOS packages contain `ChudLink.app`, its Info.plist, and the complete
runtime under Contents/MacOS. Each archive includes dependency licenses/notices,
this guide and a machine-readable `DRAFT-STATUS.json`. ZIP entries preserve Unix
executable modes. macOS signing and notarization are deliberately absent.

```powershell
./scripts/build-portable.ps1
```

The build needs the repository's .NET 10 SDK and Python 3 for ZIP metadata that
preserves Unix executable permissions when packaging from Windows. Avalonia is
pinned to 11.3.23. Outputs are under `artifacts/portable-drafts`.

For local source builds on the target OS with an installed .NET 10 SDK:

```sh
dotnet run --project src/ChudLink.Portable -c Release
dotnet run --project src/ChudLink.Portable -c Release -- --self-test
```

For an extracted Linux package, launch `./ChudLink.Portable`. If the extracting
tool discards executable mode, use `chmod +x ./ChudLink.Portable` first. Linux
requires a graphical desktop and the native .NET/Avalonia libraries appropriate
to the distribution, including X11/fontconfig/ICU/OpenSSL dependencies. This is
not yet a .deb, .rpm, AppImage or native Wayland validation.

For macOS, the draft bundle is `ChudLink.app`. It is not a signed distribution;
macOS may prevent it from launching. Native development builds, signing,
notarization, local-network permissions and supported OS versions must be
validated on a Mac before a public release. No security-policy bypass is part of
the draft instructions.

## Connect a phone

1. Start the draft host on the PC's private LAN address.
2. Put the Android phone on the same reachable network and open ChudLink.
3. Select the host, enter its six-digit code and compare its entire fingerprint.
4. Approve the exact phone in the desktop draft.
5. Assign Portable Host Station, Counter and Clock, or another supported board.

Configure the host OS firewall for the intended private local network, TCP 47831
and UDP 47832. If discovery is unavailable, use Manual connection on Android.
Guest/client-isolated Wi-Fi can prevent local connections.

## Verification

The host self-test executes without opening the native GUI. On the Windows build
machine it exercises real loopback certificate-pinned HTTPS, pairing with local
approval, authenticated WebSocket dashboard delivery, real portable telemetry,
unsupported-action rejection and credential revocation. A cross-publish is not
native execution; native rendering, platform dependencies, ARM execution,
network permissions and OS integration remain **untested** for all four target
packages.

Primary references: [Avalonia code-only UI](https://docs.avaloniaui.net/docs/fundamentals/coded-ui),
[Linux deployment](https://docs.avaloniaui.net/docs/deployment/linux),
[Avalonia license](https://github.com/AvaloniaUI/Avalonia/blob/11.3.23/licence.md).
