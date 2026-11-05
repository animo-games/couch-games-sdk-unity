# Couch Games SDK for Unity

Unity Package Manager integration for games hosted by Couch Games. In a WebGL
build it calls the platform's `window.CouchGames`, and it never opens a lobby
connection of its own.

**What each call does, and the rules around it, are in the
[developer guide](https://developer.couchgames.com/docs).** This README covers
the Unity names, the Unity-specific behavior, and the local mock.

## Install

Add the repository as a Git submodule under your Unity project's `Packages`
folder:

```bash
git submodule add https://github.com/animo-games/couch-games-sdk-unity.git Packages/com.animo.couch-games
```

Unity finds the package from its `package.json`; no manifest entry is needed
when it lives directly under `Packages`.

## Initialize

```csharp
using Animo.CouchGames;
using UnityEngine;

public sealed class GameStartup : MonoBehaviour
{
    private async void Start()
    {
        await CouchGamesSdk.InitializeAsync();
        Debug.Log($"Couch Games available: {CouchGamesSdk.IsAvailable}");
    }
}
```

Initializing twice is harmless. In the Editor and standalone development builds
the SDK uses a persistent local mock; in a WebGL player it uses the platform
when `window.CouchGames` exists.

## Saves

```csharp
var res = await CouchGamesSdk.LoadSaveResultAsync();
GameState state;
bool savingEnabled = true;
if (res.IsSafeToStartFresh)
    state = NewGame();                 // confirmed: this player has no save
else if (res.IsFound)
    state = FromSave(res.GetPayload<GameState>());
else
{
    state = NewGame();                 // show them something, but don't save it
    savingEnabled = false;
}

var write = await CouchGamesSdk.SaveGameAsync(state, 25f);
if (!write.Persisted)
    Debug.LogWarning($"Save did not land: {write.Error}");
```

`progress` is a whole percent, 0 to 100 (the platform rounds it), so `25f` is a quarter of the way.

Two rules: **`LoadSaveResultAsync()` decides whether a player is new**, and
**a save is judged by `Persisted`, not `Success`**. The guide explains why:
[`loadSaveResult()`](https://developer.couchgames.com/docs/saves#load-save-result),
[`loadLatestSave()`](https://developer.couchgames.com/docs/saves#load-latest-save)
and [refused saves](https://developer.couchgames.com/docs/saving-progress#refused-saves).

| `LoadSaveResultAsync()` | Meaning | Safe to start fresh? |
| --- | --- | --- |
| `IsNewPlayer` (also `IsSafeToStartFresh`) | No save exists | **Yes**, the only one |
| `IsFound` | `PayloadJson` holds the save | No: merge into it |
| `IsUnavailable` | It couldn't be read; one may exist | No, and keep saving off |

It also returns `Revision`, `MetadataJson` and `HostAuthoritative` (true for a
player who joined someone else's session: merge into `PayloadJson` rather than
showing it).

`LoadLatestSaveAsync()` and `LoadLatestSaveSync()` read the save handed to the
session at startup. Use them only to re-read state you already have: an empty
payload doesn't mean the player is new.

### `Persisted`, not `Success`

A refused save arrives as `Success = true, Persisted = false, Conflict = true`.
Branch on `Persisted`; the SDK treats a missing `Persisted` as "did not land".
If your retries are capped, ask for an honest failure:

```csharp
var res = await CouchGamesSdk.SaveGameAsync(state, 25f, onConflict: CouchGamesSaveConflictMode.Error);
// a refusal now arrives as Success = false, Persisted = false, Conflict = true
```

The SDK never sets this for you. To recover from a refusal:

```csharp
if (write.Conflict)
{
    var res = await CouchGamesSdk.LoadSaveResultAsync();
    if (res.IsFound)
        await CouchGamesSdk.SaveGameAsync(MergeInto(res.GetPayload<GameState>()), progress, res.Revision);
}
```

Don't require `CurrentRevision`: it's absent when the refusal happened in the
browser.

### Trying it without the platform

The mock refuses saves the same way:

```csharp
CouchGamesMock.SimulateUnreadSave();               // next blind save is refused
CouchGamesMock.SimulateLoadUnavailable = true;     // LoadSaveResultAsync reports "unavailable"
CouchGamesMock.SimulateHostAuthoritative = true;   // pretend this player joined
```

After `SimulateUnreadSave()`, calling `LoadSaveResultAsync()` lets the next
save through again, as on the platform.

## Lobby

```csharp
CouchGamesSdk.Lobby.PlayersChanged += players =>
    Debug.Log($"Players: {players.Count}");

CouchGamesSdk.Lobby.EventReceived += message =>
    Debug.Log($"{message.EventName} from {message.SenderUserId}");

CouchGamesSdk.Lobby.SendEvent(
    "handshake/input",
    JsonUtility.ToJson(new MoveCommand { direction = "north" }));
```

An optional `CouchLobbyTarget` narrows an event by `UserId`, `Role` or both.
Delivery rules are in the [lobby guide](https://developer.couchgames.com/docs/multiplayer#talk-through-the-lobby), and what to do after a dropped connection in
[Recover from dropped connections](https://developer.couchgames.com/docs/multiplayer#recover-from-dropped-connections).

## Experience files

`CouchGamesSdk.Experience` reads the current experience's files by basename
([guide](https://developer.couchgames.com/docs/level-files#experience-files)).

```csharp
[Serializable]
public sealed class LevelData
{
    public string name;
    public int[] tiles;
}

var levels = new List<LevelData>();
foreach (var fileName in await CouchGamesSdk.Experience.ListFilesAsync())
{
    if (!fileName.EndsWith(".json"))
        continue;
    var level = await CouchGamesSdk.Experience.GetFileJsonAsync<LevelData>(fileName);
    if (level != null)
        levels.Add(level);
}
```

| Member | Returns |
| --- | --- |
| `ListFilesAsync()` | Every basename on the current experience, in upload order |
| `HasFile(name)` | Whether that basename is on the current experience |
| `GetFileAsync(name)` | The file's bytes, or `null` on failure |
| `GetFileTextAsync(name)` | The file as UTF-8 (byte-order mark removed), or `null` on failure |
| `GetFileJsonAsync<T>(name)` | The file parsed with `JsonUtility`, or `default` on failure |

Failures (an unknown name, a failed download, a switch mid-flight, or JSON that
doesn't parse) return `null`/`default` and log the reason with
`Debug.LogError`. A zero-byte file returns an empty array, not `null`.

`JsonUtility` reads only an **object** at the top level. For a file whose root
is an array, wrap it in an object, or read it with `GetFileTextAsync` and a
parser of your choice.

### Trying it without the platform

In the Editor and standalone builds the mock serves a local folder as the
experience: by default `CouchGamesExperience` at the project root (outside
`Assets`, so it isn't imported or shipped), or next to the data folder in a
standalone build.

```
MyGame/
  Assets/
  CouchGamesExperience/
    level-01.json
    level-02.json
```

Point it elsewhere before reading files; a relative path resolves against the
project root:

```csharp
CouchGamesMock.ExperienceFilesDirectory = "Build/Levels";
CouchGamesMock.ExperienceFilesDirectory = null;   // back to the default
```

Every file directly in the folder is served by basename. Subfolders, dotfiles
and `.meta` files are ignored. The folder is read on every call, so files you
add while playing show up at once.

## Local testing

**Window > Couch Games > Mock Lobby** adds fake guests and sends lobby events.
Mock data persists under `Application.persistentDataPath`. The **Saves**
section shows the stored revision and the `CouchGamesMock` switches above
(unavailable load, joining as a guest, refusing the next save, clearing all
mock data). The **Experience Files** section shows the mock folder and its
files.

## Supported platform API

Everything in the [guide](https://developer.couchgames.com/docs): saves
(including `FlushSavesAsync()`), gameplay and `CompleteCampaignAsync()`,
experience data and date, experience files, shared game assets, game metadata
(including `DeleteGameMetadataAsync()` and `ClearGameMetadataAsync()`),
achievements, session stats, telemetry, play modes, the lobby, WebRTC
signaling, and `ListExperiencesAsync()` / `OpenExperienceAsync(experienceId)`.

```csharp
var done = await CouchGamesSdk.CompleteCampaignAsync();   // done.AlreadyCompleted on a repeat
var sent = await CouchGamesSdk.FlushSavesAsync();         // sent.Persisted, sent.Flushed
await CouchGamesSdk.DeleteGameMetadataAsync("settings", "volume");
CouchGamesSdk.Track("level_complete", "{\"level\":3,\"success\":true}");
await CouchGamesSdk.FlushTelemetryAsync();
var url = await CouchGamesSdk.GetSharedFileUrlAsync("audio/theme.ogg");
```

The mock records `CompleteCampaignAsync()` once (it doesn't know which
experience is the campaign), keeps every `Track()` call in
`CouchGamesMock.TrackedEvents` instead of sending it, has no shared assets, and
doesn't simulate signaling.

### Play modes

```csharp
await CouchGamesSdk.InitializeAsync();
CouchGamesSdk.PlayModeSelected += OnPlayMode;            // (mode, code)
OnPlayMode(CouchGamesSdk.PlayMode, CouchGamesSdk.ShareCode); // it can fire before you subscribe
```

`PlayMode` is `"1-device"`, `"2-devices"` or null; `IsJoining` is true for a
guest ([guide](https://developer.couchgames.com/docs/multiplayer#play-modes-and-room-codes)).
In the mock, `CouchGamesMock.SelectPlayMode("2-devices", "ABCD")` raises the
event, and `CouchGamesMock.IsJoining` makes this client a guest.

### WebRTC signaling

`CouchGamesSdk.WebRtc` finds the other player: `ConnectSignalingAsync(code)`,
`SendSignal(peerId, json)`, and the `PeerExists`, `PeerJoined`, `PeerLeft` and
`SignalReceived` events ([guide](https://developer.couchgames.com/docs/multiplayer#connect-players-with-webrtc)).
Unity's WebGL player has no WebRTC of its own, so the peer connection itself
is yours: a WebRTC package, or your own JavaScript.

### Gameplay completion and the streak

`await CouchGamesSdk.GameplayCompleteAsync()` records the completion. The
response's `Streak` is the player's streak in your game
([guide](https://developer.couchgames.com/docs/gameplay#gameplay-complete)); it's `null`
when the platform didn't send one, so treat that as unknown.

### Experience data

`await CouchGamesSdk.GetExperienceDataAsync()` returns the current experience
as JSON in `PayloadJson`
([fields](https://developer.couchgames.com/docs/experiences#get-experience-data)). Read it
with a JSON parser that keeps nullable numbers: `number` is null for an
unnumbered experience.

### List and open experiences

`await CouchGamesSdk.ListExperiencesAsync()` returns the archive as a JSON
array in `PayloadJson`, and the player's streak in `Streak`
([fields](https://developer.couchgames.com/docs/experiences#list-experiences)). Parse it
with a JSON parser that reads top-level arrays, nullable numbers and
dictionaries: `CouchGamesResponse.GetPayload<T>()` uses `JsonUtility`, which
reads none of them, and this package has no typed wrapper.

`await CouchGamesSdk.OpenExperienceAsync(experienceId)` switches to an id from
that list without reloading. On success the experience-changed event has
already been delivered. On `Success == false`, `Error` carries the platform's
message ([the list](https://developer.couchgames.com/docs/experiences#open-experience)) and
`RawJson` the full response. Until the event arrives or the call fails, make no
other SDK calls.

### `couchgames:experience-changed`

Subscribe through `CouchGamesSdk.Lobby.EventReceived` and compare the name
with `CouchGamesLobby.ExperienceChangedEventName`. Initialize the SDK first, so
the event bridge is connected before you open an experience. The event reaches
solo games too, with an empty `SenderUserId`, and in a lobby it can arrive
because another member switched.

```csharp
await CouchGamesSdk.InitializeAsync();
CouchGamesSdk.Lobby.EventReceived += message =>
{
    if (message.EventName != CouchGamesLobby.ExperienceChangedEventName)
        return;
    // Clear the board and load the new puzzle. DataJson has the event's fields.
    Debug.Log(message.DataJson);
};

var result = await CouchGamesSdk.OpenExperienceAsync(selectedExperienceId);
if (!result.Success)
    Debug.LogWarning(result.Error);
```

`DataJson` holds the [event's fields](https://developer.couchgames.com/docs/experiences#reserved-event-couchgamesexperience-changed).
`Lobby.CurrentGame` is null solo, so read the new experience from the event or
`GetExperienceDataAsync()`. `CouchGamesSdk.ExperienceDataJson` also refreshes
on the event, but on the platform only after its request answers, so don't read
it in your handler.

### Trying archive switching without the platform

`CouchGamesMock.Experiences` is the list the mock archive returns, oldest
first. It starts with the mock's own `mock-experience` entry; add entries to
make them openable, or clear it to stand in for the campaign lock's empty
archive. It lives in memory, so set it up each run:

```csharp
CouchGamesMock.Experiences.Add(new CouchGamesMockExperience
{
    Id = "mock-archive-1",
    ExperienceIndex = 1,
    Number = 1,
    Title = "First daily",
    ActivatedAt = "2026-01-02T00:00:00.000Z",
    Live = false,
    State = "none",
    PlayerStates = { ["mock-guest-1"] = "completed" }
});
CouchGamesMock.Streak = 3;
```

`State` is the local player's. Each row's `players` lists every mock lobby
member (add guests in the Mock Lobby window): the local player at `State`, the
others at their `PlayerStates` entry, or `"none"`. `CouchGamesMock.Streak` is
the `Streak` the mock answers on the list and on `GameplayCompleteAsync()`; the
mock doesn't count completions.

Opening a listed id updates the mock's experience data and date and raises the
event through the mock lobby, with `initiatedByMe: true` and no sender, before
the call succeeds. Missing, current and unknown ids fail without an event. It's
a light simulation: files still come from
`CouchGamesMock.ExperienceFilesDirectory`, saves and metadata share one mock
store, and the switch is instant, so there's no in-flight refusal, no other
member switching, and no campaign-lock reset.
