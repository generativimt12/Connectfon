# Connectfon

Connectfon is a Bluetooth phone/head-unit application with two targets:

- **Windows** — the primary desktop/head-unit build with real Windows Bluetooth integration, HFP/PBAP implementation attempts, dial pad, contacts, recent calls, diagnostics and a car-style UI.
- **Android** — an Android adaptation with a native mobile UI, Bluetooth permissions, paired-device discovery and device/profile diagnostics.

## Windows

The Windows build is published as a self-contained single-file x64 executable through GitHub Actions.

Build with: `dotnet build Connectfon.sln -c Release`

## Android

The Android project is under `android/` and builds an APK with the Android SDK/Gradle.

Build with: `cd android && gradle :app:assembleDebug`

APK output: `android/app/build/outputs/apk/debug/app-debug.apk`

The Android build intentionally uses public Android Bluetooth APIs. It does **not** fake HFP/PBAP functionality. Android exposes less of the Bluetooth Hands-Free/PBAP client stack to ordinary third-party applications than Windows, so features that require hidden/system Bluetooth APIs must be validated on real hardware before being advertised as supported.

### Android currently provides

- Bluetooth enabled/disabled state
- runtime Bluetooth permissions
- paired-device list
- device name/address/bond state
- exposed Bluetooth UUID/profile diagnostics
- clean handling of unsupported/unavailable information

### Android roadmap

The Android target can be expanded where the public Android API permits it, while preserving the same Connectfon UI/architecture. HFP call control, PBAP contacts and call history are treated as hardware/API-dependent capabilities rather than simulated features.

## Build status

Both targets have separate build paths. A successful CI build proves compilation and packaging; it does not by itself prove compatibility with every phone or Bluetooth chipset. Physical HFP/PBAP testing is required for production-level compatibility claims.
