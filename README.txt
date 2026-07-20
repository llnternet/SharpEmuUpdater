SharpEmu Updater — Build Log
============================
Created by llnternet

⚠️ Disclaimer
---------------
This is an unofficial, third-party utility and is not affiliated with, endorsed by, or
connected in any way to the SharpEmu project, its contributors, Sony Interactive Entertainment,
or any of its subsidiaries or trademarks. "PlayStation" and "PS5" are trademarks of Sony
Interactive Entertainment. This app contains no PlayStation firmware, BIOS, system software, or
game data of any kind -- it only automates fetching the public build artifacts that SharpEmu's
own GitHub Actions workflow already produces and publishes. Provided "as is," without warranty
of any kind. See the upstream SharpEmu repository for that project's own license and usage terms.

📦 Build Tracking & Installing
-------------------------------
- Tracks any fork/branch on GitHub -- switch anytime via "Switch Fork..." with search
- Classifies builds as Success / Regression / Still Failing / In Progress / Queued / Needs Approval / Cancelled / Skipped
- Auto-checks on a timer -- never auto-downloads or installs anything without you clicking
- Upgrade or downgrade to any past successful build via "Select Build..."
- Every build you've downloaded stays on disk forever -- switching back is instant
- Right-click "Launch SharpEmu ▾" to launch any previously downloaded build
- Real-time download progress bar, changelog view, and copy SHA / View on GitHub from the list
- Remembers your library settings and the latest known build across restarts
- Cancelled/skipped builds with no actual Windows artifact are filtered out automatically --
  this app is Windows-only, so a build nothing can be installed from won't clutter the list or
  get mistaken for the latest one
- Detects the Windows build by keyword rather than an exact filename, so it keeps working even
  if the project ever renames its build artifacts

🔔 Notifications & Status
---------------------------
- Toast notifications + tray badge when a new build is available
- Live GitHub API budget indicator, with a clear warning if you get rate-limited
- Live GitHub status indicator if GitHub itself is having an outage
- Clear "Loading..." indicators instead of a blank screen while data loads
- Status stays live-synced with the build list at all times -- a brand-new push is picked up
  within seconds, not just a build that was already known to be in progress

📝 Activity Log
-----------------
- Color-coded, auto-scrolling log with copy/export options
- Only logs when something actually changes -- no repeated identical lines cluttering it up
- Timestamps match your system's date/time format

🎨 Interface
--------------
- Search/filter in Recent Builds and Switch Fork
- Draggable splitter, dark purple theme matching SharpEmu's own branding

🔒 Security
-------------
- Your GitHub token is encrypted at rest (Windows DPAPI) -- never stored as plain text
- Works with either classic or fine-grained Personal Access Tokens
- Token is sent only to api.github.com over HTTPS, never anywhere else, never logged
