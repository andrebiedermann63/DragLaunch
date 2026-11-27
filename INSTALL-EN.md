# DragLaunch – Installation and updates

DragLaunch is built from source. The file `build.bat` does this with a double-click. The same file is used for updates: it closes a running DragLaunch, builds the new version and starts it again.

## Two ways

- **Ready-built version (easy):** under **Releases** download `DragLaunch-<version>-Windows.zip`. It only needs the smaller **.NET 10 Desktop Runtime**; nothing is built. Instructions are in the included `LIESMICH-README.txt`; in short: extract, start `DragLaunch.exe`. Update: extract the new ZIP over the folder.
- **From source (for your own changes):** with `build.bat` as described below.

## Requirements

- Windows 11, 64-bit
- **.NET 10 SDK** (one-time, free from Microsoft):
  https://dotnet.microsoft.com/download/dotnet/10.0
  Or in a command prompt: `winget install Microsoft.DotNet.SDK.10`

The SDK contains everything needed to build and run DragLaunch. If it is missing, `build.bat` says so and shows the download link.

## First installation

1. On the GitHub page choose **Code → Download ZIP** (or download the ZIP file under **Releases**).
2. Before extracting: right-click the ZIP file → **Properties** → tick **Unblock** at the bottom → OK. Otherwise Windows shows a warning when you start `build.bat`.
3. Extract the ZIP to a fixed place where you can write, e.g. `C:\Tools\DragLaunch`.
   Not under `C:\Program Files`: DragLaunch stores its settings in its own folder.
4. Double-click `build.bat`. After about a minute the window shows “Done”.
5. Start `App\DragLaunch.exe`.
6. In the menu (☰ top left) turn on **Start with Windows** if DragLaunch should start automatically.

That's it. Just drag programs, folders and files into the window.

## Updating to a new version

1. Download the new version as above and tick **Unblock**.
2. Extract the ZIP **into the same folder** and let it overwrite existing files.
3. Double-click `build.bat`. DragLaunch is closed, rebuilt and started again.

Your data is kept. It lives in the `App` folder, which is not part of the ZIP:

| In the `App` folder | Contents |
|---|---|
| `DragLaunch.ini` | All settings, tabs and tiles |
| `Backup\` | Your backups |
| `Icons\` | Custom icons and favicons |
| `Sound\`, `Languages\` | Sounds and language files (your own are kept) |

**Never delete the `App` folder.** To be completely safe, create a backup from the menu before updating.

### With Git

If you cloned the repository with Git, update with `git pull` and then run `build.bat`.

## Your own changes

The source code is in the `src` folder (C# / WPF). After a change just run `build.bat`. Notes on the structure are in `HANDOFF.md` (German).
Own languages: copy `App\Languages\English.ini` and translate it. Own sounds: put `.wav` files into `App\Sound`.

## For the maintainer: creating the ready-built ZIP

`make_release.bat` builds DragLaunch into its own empty `Release` folder (without your settings, backups and icons) and creates `Release\DragLaunch-<version>-Windows.zip`. Attach this file on GitHub under **Releases → Draft a new release**.

## Uninstalling

1. Turn off **Start with Windows** in the menu.
2. Close DragLaunch and delete the whole folder. Nothing is written to the registry.

## Problems?

- **“The .NET 10 SDK is missing”:** install the SDK (link above) and run `build.bat` again.
- **Windows warns when starting `build.bat`:** “More info” → “Run anyway”, or tick **Unblock** before extracting (see above).
- **Build failed:** report the messages in the window as an issue on GitHub.
- **DragLaunch is slow or hangs:** the file `App\DragLaunch.log` helps with troubleshooting.
