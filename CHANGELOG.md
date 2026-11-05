# Changelog

## Unreleased

- Added the platform calls the package didn't reach: `FlushSavesAsync()`,
  `CompleteCampaignAsync()`, `DeleteGameMetadataAsync()`,
  `ClearGameMetadataAsync()`, `Track()` and `FlushTelemetryAsync()`; play
  modes (`PlayMode`, `ShareCode`, `IsJoining`, `PlayModeSelected`); WebRTC
  signaling (`CouchGamesSdk.WebRtc`); and shared game assets
  (`GetSharedRootAsync()`, `GetSharedFileUrlAsync()`, `GetSharedFileInfoAsync()`).
- Added `CouchGamesResponse.AlreadyCompleted`, `AlreadyUnlocked` and `Flushed`.
- The WebGL bridge resolves dotted calls (`webrtc.connectSignaling`) and
  forwards play-mode and signaling events.
- The mock answers the new calls: `CouchGamesMock.SelectPlayMode`,
  `CouchGamesMock.TrackedEvents`; it has no shared assets and doesn't simulate
  signaling.
- README: platform behavior now links the developer guide; progress is a
  whole percent (the examples passed `0.25f`, which rounds to 0%).

- Added `CouchGamesSdk.ListExperiencesAsync()` and `OpenExperienceAsync(id)`
  through the existing generic WebGL bridge.
- Added `CouchGamesLobby.ExperienceChangedEventName` for the reserved
  `couchgames:experience-changed` event delivered through `EventReceived`,
  including solo games.
- Added `CouchGamesMock.Experiences`, a configurable mock archive whose
  synchronous switch raises the reserved event before answering success.
- Added `CouchGamesResponse.Streak`, the player's streak answered by
  `listExperiences` and `gameplayComplete`. Documented the archive rows'
  `players` map and experience data's `number`; the mock fakes all three
  (`CouchGamesMock.Streak`, `CouchGamesMockExperience.PlayerStates`).
- `CouchGamesSdk.ExperienceDataJson` refreshes on
  `couchgames:experience-changed`, so it describes the new experience after a
  switch.
- Documented the archive payload, co-op switching, the refusals, event
  handling and the rule to wait for the event or a failure before making
  further SDK calls.

## 0.3.0

- Added `CouchGamesSdk.Experience` for reading the current experience's files
  by basename: `ListFilesAsync`, `HasFile`, `GetFileAsync`, `GetFileTextAsync`,
  and `GetFileJsonAsync<T>`.
- The WebGL bridge reads files through `window.CouchGames.experience`, copying
  bytes over the wasm heap and accepting buffers from the parent page's realm.
- Added `CouchGamesMock.ExperienceFilesDirectory` so a local folder (default
  `CouchGamesExperience` at the project root) stands in for experience files
  in the Editor and standalone builds.
- Added an Experience Files section to the mock lobby window.

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
