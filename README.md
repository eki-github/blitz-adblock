# Blitz Adblock Patch

Blitz Adblock Patch adds ad filtering to the Blitz.gg desktop app on Windows. Once installed, use your ordinary Blitz shortcut. If you already start Blitz at sign-in, the patch also integrates with that entry.

## Install

1. Download **BlitzAdblock.exe** from the [latest release](https://github.com/eki-github/blitz-adblock/releases/latest).
2. Open it and confirm the displayed destination. The installer detects Blitz or lets you select its executable. It stores the patch in **BlitzAdblockPatch** beside **Blitz.exe**. You can then delete the downloaded file.
3. Exit Blitz from the system tray once, then open **Blitz** from the Start menu.

Setup recognizes existing per-user Start menu and desktop shortcuts named **Blitz.lnk** that point directly to Blitz. It preserves the original sign-in behavior, including a disabled or absent autostart entry. Windows may request administrator approval for the existing startup value. The destination folder must be writable. If an update recreates a shortcut or startup entry, rerun the installer to reconnect it.

## Uninstall

Exit Blitz from the system tray. Open **Uninstall Blitz Adblock Patch** in the Start menu and confirm. It restores entries still managed by the patch, removes its versioned files and deletes the uninstall shortcut. The per-user recovery record is kept in `%LOCALAPPDATA%\BlitzAdblock\integration` until restoration succeeds.

## How it works

Patched shortcuts start a small helper from the Blitz installation directory. It launches the original Blitz executable with Chromium hostname rules for known ad providers. During that session, the helper applies a stylesheet to recognized Blitz desktop pages over private, inherited pipes. This hides ad placements and allows the surrounding layout to reclaim the space. Blitz continues to provide the game data, builds and overlays.

The original Blitz executable and application archive stay intact. Opening Blitz.exe directly starts a standard session. Changes to Blitz domains or page layouts can require updated filter rules.

## Compatibility

Tested with Blitz **3.0.25**.

## Build and test

The project uses the .NET Framework compiler included with Windows 10 and 11.

```powershell
powershell -NoProfile -File .\build.ps1
powershell -NoProfile -File .\tests\run.ps1
```

The output is `dist\BlitzAdblock.exe`. Host rules are in `src/Launcher.cs` and layout rules in `src/filter.css`.

## Command line

```powershell
.\BlitzAdblock.exe install
.\BlitzAdblock.exe install --dir "D:\Games\Blitz"
.\BlitzAdblock.exe uninstall
.\BlitzAdblock.exe run --dir "D:\Games\Blitz"
```

Installation and removal ask for confirmation. The `apply` and `restore` commands manage the optional **Blitz (Adblock)** desktop shortcut.

The patch is distributed under the [MIT license](LICENSE). Blitz.gg is a separate product.
