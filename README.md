# SharpEmu Updater

A small Windows companion app that tracks [SharpEmu](https://github.com/sharpemu/sharpemu) builds
on GitHub Actions and installs them for you. It is an unofficial, third-party utility created by
**llnternet** and is not affiliated with, endorsed by, or maintained by the SharpEmu project or
its contributors.

## ⚠️ Disclaimer

This is an unofficial, third-party tool. Not affiliated with, endorsed by, or connected to the
SharpEmu project or Sony Interactive Entertainment in any way. Does not bundle any PlayStation
firmware, BIOS, or game data -- it only downloads and launches build artifacts that SharpEmu's
own public GitHub Actions already produce. Provided "as is," no warranty.

## ✨ Features

**📦 Build Tracking & Installing**
- Tracks any fork/branch on GitHub -- switch anytime via "Switch Fork..." with search
- Classifies builds as Success / Regression / Still Failing / In Progress / Queued / Needs Approval / Cancelled / Skipped
- Shows which platforms (Windows/macOS/Linux) each build produced artifacts for
- Auto-checks on a timer -- never auto-downloads or installs anything without you clicking
- Upgrade or downgrade to any past successful build via "Select Build..."
- Every build you've downloaded stays on disk forever -- switching back is instant
- Right-click "Launch SharpEmu ▾" to launch any previously downloaded build
- Real-time download progress bar, changelog view, and copy SHA / View on GitHub from the list
- Remembers your library settings and the latest known build across restarts
- Cancelled/skipped builds with no actual Windows artifact are filtered out automatically
- Detects the Windows build by keyword rather than an exact filename, so it keeps working even
  if the project ever renames its build artifacts

**🔔 Notifications & Status**
- Toast notifications + tray badge when a new build is available
- Live GitHub API budget indicator, with a clear warning if you get rate-limited
- Live GitHub status indicator if GitHub itself is having an outage
- Status stays live-synced with the build list at all times

**📝 Activity Log**
- Color-coded, auto-scrolling log with copy/export options
- Only logs when something actually changes -- no repeated identical lines cluttering it up
- Timestamps match your system's date/time format

**🎨 Interface**
- Search/filter in Recent Builds and Switch Fork
- Draggable splitter, dark purple theme matching SharpEmu's own branding

**🔒 Security**
- Your GitHub token is encrypted at rest (Windows DPAPI) -- never stored as plain text
- Works with either classic or fine-grained Personal Access Tokens
- Token is sent only to `api.github.com` over HTTPS, never anywhere else, never logged

## 🐛 Recent Fixes

- Fixed a stale-connection error ("No such host is known") that could show up right after your
  PC woke from sleep during an automatic check
- Platform labels now correctly disappear once that platform's artifact expires or is removed
- Added a secondary-rate-limit indicator, separate from the existing API budget counter, so a
  GitHub abuse-detection block shows a clear message instead of looking like a random error

## What this app does and does not do

- SharpEmu Updater does **not** contain, embed, or distribute any part of the SharpEmu emulator's
  source code or compiled binaries. It contains no PlayStation firmware, BIOS, system software,
  or game data of any kind.
- All it does is call the public GitHub REST API, using **your own** GitHub personal access
  token, to check SharpEmu's (or a fork's) Actions run history and download the
  `sharpemu-win64` build artifact that SharpEmu's own "Build and Release" GitHub Actions
  workflow already produces and publishes. Every build it installs is one SharpEmu (or a
  SharpEmu contributor) built and uploaded themselves -- this app only automates fetching it.
- It does not modify, patch, decrypt, or otherwise interact with any game files, and it does not
  emulate anything itself.

## Not affiliated with Sony

This project is not affiliated with, endorsed by, or connected in any way to Sony Interactive
Entertainment, PlayStation, or any of their subsidiaries or trademarks. "PlayStation" and "PS5"
are trademarks of Sony Interactive Entertainment.

## SharpEmu's own terms apply

SharpEmu itself is licensed under **GPL-2.0** and is developed for research and educational
purposes. Per SharpEmu's own README: it does not support or condone piracy, and users are
expected to use only legally obtained copies of their own games, dumped from consoles they
personally own. Installing SharpEmu through this updater does not change or exempt you from
SharpEmu's own license terms or usage policy -- see the
[upstream repository](https://github.com/sharpemu/sharpemu) for the authoritative terms.

## Setup

See [`HOW TO SET UP YOUR TOKEN.txt`](HOW%20TO%20SET%20UP%20YOUR%20TOKEN.txt) for how to create a
GitHub personal access token (classic or fine-grained, either works) and point this app at it. The
token is yours; this app sends it only to `api.github.com` to read run/artifact data and download
artifacts on your behalf. It is never sent anywhere else. You drop it in as a plain-text
`token.txt` once, and the app encrypts it (Windows DPAPI, tied to your own Windows user account on
this machine) and deletes the plain-text copy automatically the moment it's read -- there is no
plain-text credential file left sitting on disk in normal use.

## Attribution

Created by **llnternet**. See [`NOTICE.txt`](NOTICE.txt) for the attribution terms that apply to
this project's own code (separate from, and in addition to, SharpEmu's own GPL-2.0 license for
the emulator itself).

## No warranty

This software is provided "as is", without warranty of any kind, express or implied. Use it at
your own risk.
