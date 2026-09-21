# Changelog

## 0.2.0

- Added `CouchGamesSdk.LoadSaveResultAsync` and `CouchGamesSaveLoadResult`, the
  awaitable, honest counterpart to `LoadLatestSaveAsync`/`LoadLatestSaveSync`
  for deciding whether a player is new.
- Added `Persisted`, `Conflict`, and `CurrentRevision` to `CouchGamesResponse`
  so save writes can be judged by whether they landed, not just `Success`.
- Added `expectedRevision` and `onConflict` parameters to `SaveGameAsync`/
  `SaveGameJsonAsync` for deliberate overwrites and bounded-retry recovery.
- Added a no-clobber guard to the mock save backend, plus `CouchGamesMock`
  knobs (`SimulateUnreadSave`, `SimulateLoadUnavailable`,
  `SimulateHostAuthoritative`, `ClearData`) to rehearse the recovery paths in
  the Editor.
- Added Saves controls (stored revision, simulate toggles, clear/unread-save
  buttons) to the mock lobby window.

## 0.1.0

- Initial Unity Package Manager release.
- Added WebGL bridge for the Couch Games classic and lobby APIs.
- Added typed lobby roster and event abstractions.
- Added persistent Editor/standalone mock backend and mock lobby window.
