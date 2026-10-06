# 📱 Connectfon — Phone ↔ Computer / Head-Unit Bridge

> A Windows + Android project exploring a practical phone-to-computer Bluetooth experience.

[![Windows](https://img.shields.io/badge/Target-Windows-0078D4?logo=windows&logoColor=white)](https://www.microsoft.com/windows/) [![Android](https://img.shields.io/badge/Target-Android-3DDC84?logo=android&logoColor=white)](https://developer.android.com/) [![C%23](https://img.shields.io/badge/Windows-C%23-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/) 

**Two targets, one goal:** make a phone usable from a computer or car-style head unit while clearly separating what public APIs can actually support from what requires real hardware testing.

---

## 🧭 Project map

| Target | Focus |
|---|---|
| 🖥️ **Windows** | Bluetooth integration, calls, contacts, recent calls, diagnostics and head-unit UI |
| 🤖 **Android** | Native UI, permissions, paired devices and Bluetooth/profile diagnostics |
| 🧪 **Validation** | Real-device testing for HFP/PBAP capabilities |

---

# Connectfon

Connectfon is a Bluetooth phone/head-unit application with two targets:

- **Windows** — the primary desktop/head-unit build with real Windows Bluetooth integration, HFP/PBAP implementation attempts, dial pad, contacts, recent calls, diagnostics and a car-style UI.
- **Android** — an Android adaptation with a native mobile UI, Bluetooth permissions, paired-device discovery and device/profile diagnostics.

## Windows

The Windows build is published as a self-contained single-file x64 executable through GitHub Actions.

Build with: `dotnet build Connectfon.sln -c Release`

## Android

The Android project is under `android/` and builds a debug APK with Android SDK/Gradle.

Build with: `cd android && gradle :app:assembleDebug`

APK output: `android/app/build/outputs/apk/debug/app-debug.apk`

CI also verifies that the APK exists and uploads it as the `Connectfon-android-debug` artifact.

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
