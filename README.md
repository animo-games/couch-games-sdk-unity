# Couch Games SDK for Unity

Unity Package Manager integration for games hosted by Couch Games.

The WebGL runtime talks to the platform-injected `window.CouchGames` object.
The platform owns the lobby WebSocket; this package never opens a competing
connection in a WebGL build.

## Install

Add the repository as a Git submodule under your Unity project's `Packages`
folder:

```bash
git submodule add https://github.com/animo-games/couch-games-sdk-unity.git Packages/com.animo.couch-games
```

Unity discovers the package from its `package.json`; no manifest entry is
required when the package lives directly under `Packages`.

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

Initialization is idempotent. In the Editor and standalone development builds,
the SDK uses a persistent local mock. In a WebGL player it uses the real
platform when `window.CouchGames` exists.

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
    state = NewGame();                 // show them something, but do NOT save it
    savingEnabled = false;
}

var write = await CouchGamesSdk.SaveGameAsync(state, 0.25f);
if (!write.Persisted)
    Debug.LogWarning($"Save did not land: {write.Error}");
```

Two rules carry all of this: **`LoadSaveResultAsync()` decides whether a
player is new**, and **`SaveGameAsync()` is judged by `Persisted`, not
`Success`**.

### Why `LoadLatestSaveAsync`/`LoadLatestSaveSync` cannot tell you

`LoadLatestSaveAsync()` and `LoadLatestSaveSync()` read the save the platform
handed your session at startup. `LoadLatestSaveSync` costs no round-trip, but
its empty payload is ambiguous -- it means any of:

- this player genuinely has no save;
- the session had not finished starting when you asked;
- they joined **someone else's** session, so their own save was withheld on
  purpose (a co-op guest must see the host's board, not their solo game).

Only the first makes it safe to start fresh. Code that reads the other two as
"new player" initialises empty state, and its next whole-document save
destroys real progress -- which is exactly the bug this API exists to
prevent. Use `LoadLatestSaveAsync`/`LoadLatestSaveSync` only to re-read state
you have already established.

`LoadSaveResultAsync()` awaits the real answer and says which case you are in:

| | Meaning | Safe to start fresh? |
| --- | --- | --- |
| `IsNewPlayer` | Confirmed: no save exists | **Yes** -- the only status that is |
| `IsFound` | `PayloadJson` holds the stored save | No -- merge into `PayloadJson` |
| `IsUnavailable` | Could not be read; one may well exist | No -- and keep writes off |

`IsSafeToStartFresh` is `IsNewPlayer` under a name that says what the answer
is for. On `Unavailable`, put the player somewhere sensible and leave saving
**disabled** for the session rather than guessing.

It also returns `Revision` (what you read, see below), `MetadataJson`, and
`HostAuthoritative`.

### Guests: `HostAuthoritative`

When `HostAuthoritative` is true you are a player who joined someone else's
session. `PayloadJson` is **your own** save, but the host owns the shared
board. Use it as a **merge base** -- read it, merge this session's
contributions into it, write that back. Rendering it as live state would show
a guest their solo game in place of the host's.

### `Persisted`, not `Success`

The platform refuses a write that would replace a save this session has never
read, and by default it reports that refusal as:

```csharp
new CouchGamesResponse(success: true, persisted: false, conflict: true, ...)
```

`Success = true` on a write that did not happen looks wrong, and it is
deliberate: this SDK is compiled into your exported build, so games already
published cannot be updated from the platform, and many of them branch on
`Success` alone with an unbounded retry loop. An honest failure would spin
them for a whole session.

So **branch on `response.Persisted`**. It is accurate whichever mode you ask
for. `Conflict` tells you the write was refused rather than broken, and a
response that omits `Persisted` means the platform took the call as a no-op
-- treat a missing value as "did not land", which is what this SDK does.

If your retry loop is **bounded**, you can ask for an honest failure instead:

```csharp
var res = await CouchGamesSdk.SaveGameAsync(state, 0.25f, onConflict: CouchGamesSaveConflictMode.Error);
// a refusal now arrives as Success = false, Persisted = false, Conflict = true
```

The SDK never sets this for you -- only your game knows whether its retry is
capped.

### Recovering from a refusal

Always the same, and it needs nothing from `CurrentRevision`:

```csharp
if (write.Conflict)
{
    var res = await CouchGamesSdk.LoadSaveResultAsync();
    if (res.IsFound)
        await CouchGamesSdk.SaveGameAsync(MergeInto(res.GetPayload<GameState>()), progress, res.Revision);
}
```

Passing `res.Revision` as `expectedRevision` asserts which state you are
replacing, and is the reliable way to overwrite deliberately.
`CurrentRevision` on the response is the revision your write produced (or, on
a refusal the server made, the one stored) -- it is absent when the refusal
happened client-side, so never require it.

### Trying it without the platform

The mock backend simulates the same guard, so every path above is reachable
in the editor:

```csharp
CouchGamesMock.SimulateUnreadSave();               // next blind save is refused
CouchGamesMock.SimulateLoadUnavailable = true;     // LoadSaveResultAsync reports "unavailable"
CouchGamesMock.SimulateHostAuthoritative = true;   // pretend this player joined
```

`SimulateUnreadSave()` clears the way the real one does -- call
`LoadSaveResultAsync()`, and the next write is admitted again.

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

Broadcast events reach every other client. The sender does not receive its own
event back. An optional `CouchLobbyTarget` can filter by `UserId`, `Role`, or
both; both conditions must match.

## Experience files

An experience is a dated content drop for your game -- a level pack, a room,
a puzzle set -- uploaded on the platform. `CouchGamesSdk.Experience` reads its
files **by basename**, never by URL: the URL embeds an experience id that
rotates, so code that keys off it breaks the next day.

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
| `GetFileTextAsync(name)` | The file decoded as UTF-8 (byte-order mark removed), or `null` on failure |
| `GetFileJsonAsync<T>(name)` | The file parsed with `JsonUtility`, or `default` on failure |

Delivery is race-free: the platform keeps the bytes, so `GetFileAsync`
resolves whether the download finished before or after you asked. There is no
event to wait for.

Failures -- an unknown name, a failed download, an experience that rotated
mid-flight, or JSON that does not parse -- return `null`/`default` and log the
reason with `Debug.LogError`. A zero-byte file returns an empty array, not
`null`. On a platform build that predates experience files, `ListFilesAsync()`
returns an empty list.

`GetFileJsonAsync<T>` uses `JsonUtility`, which only reads an **object** at the
top level. For a file whose root is an array, wrap it in an object or read it
with `GetFileTextAsync` and a parser of your choice.

### Trying it without the platform

In the Editor and standalone builds, the mock serves a local folder as the
experience. By default it is a `CouchGamesExperience` folder at the Unity
project root (outside `Assets`, so the files are not imported or shipped). In a
standalone build the default sits next to the executable's data folder.

```
MyGame/
  Assets/
  CouchGamesExperience/
    level-01.json
    level-02.json
```

Point it elsewhere before reading files, for example at your level build
output. A relative path resolves against the project root:

```csharp
CouchGamesMock.ExperienceFilesDirectory = "Build/Levels";
CouchGamesMock.ExperienceFilesDirectory = null;   // back to the default
```

Every file directly inside the folder is served by basename. Subfolders,
dotfiles, and Unity `.meta` files are ignored. The folder is read on every
call, so files you add while playing show up immediately.

## Local testing

Open **Window > Couch Games > Mock Lobby** to add fake guests and inject lobby
events. Classic calls persist beneath Unity's `Application.persistentDataPath`.
The **Saves** section shows the stored save's revision and exposes the
`CouchGamesMock` knobs above (simulate an unavailable load, simulate joining
as a guest, force the next save to be refused, or clear all mock data) as
toggles and buttons. The **Experience Files** section shows the mock
experience folder and the files it serves.

## Supported platform API

- `ListExperiencesAsync()` and `OpenExperienceAsync(experienceId)` (see below)
- saves
- gameplay lifecycle
- experience data/date
- experience files
- game metadata
- achievements
- session statistics
- lobby roster, identity, current game, and tunneled events

### List and open experiences

`await CouchGamesSdk.ListExperiencesAsync()` returns a `CouchGamesResponse`.
On `Success`, `PayloadJson` is a JSON array of this game's openable challenges
(released live or archived experiences, excluding the campaign), oldest first.
It works before a session starts. Call it when the archive screen opens, not
every frame: each call is a server round trip. The list is empty while the
campaign-first lock holds; anonymous players have `state: "none"` everywhere.

| Payload field | Meaning |
| --- | --- |
| `id` | String id to pass to `OpenExperienceAsync` |
| `experienceIndex` | Stored index reported by `getExperienceData`, or null |
| `number` | Player-facing "No. N", or null if unnumbered |
| `title` | Title string, or null |
| `activatedAt` | ISO instant, or null; use its UTC date for a daily |
| `live` | Boolean: whether the challenge is live |
| `state` | This player's `"none"`, `"started"`, or `"completed"` state |

Use a JSON parser that preserves nullable numbers for this array.
`CouchGamesResponse.GetPayload<T>()` uses Unity's `JsonUtility`, which does not
read top-level arrays or nullable integers. There is no typed experience-data
or archive wrapper in this package.

`await CouchGamesSdk.OpenExperienceAsync(experienceId)` moves to an id from
that list. The platform flushes the current saves, starts the new session,
swaps files and updates the page URL. On success the experience-changed event
has already been delivered. On `Success == false`, `Error` carries the
platform's `message`, and no event follows; `RawJson` retains the full response.

**After calling `OpenExperienceAsync`, do nothing with the SDK until
`couchgames:experience-changed` arrives or the call answers `success: false`;
requests meanwhile are refused with `Experience switch in progress`.**
`LoadSaveResultAsync()` reports unavailable with that message. The platform
exempts tracking/flushing, save flushing, lobby calls and signaling; the safe
game-side rule is to wait. Synchronous cached getters are not a readiness
signal: the save cache is empty during handover and the date stays old until
the event.

Switching is **solo-only in the current platform**. A lobby returns
`Switching needs the lobby; coming in a later update`. Other refusals include
`Experience id required`, `Already on this experience`, `Unknown experience`,
`This experience can't be opened`, `Challenges are locked until the campaign is finished`,
`Experience switch in progress`, `No session yet`, and `Switching is not available here`.
The campaign-lock refusal starts a fresh session on the current experience.
A separate build per experience reloads the target as a fresh start and sends
no change event to the old build.

### `couchgames:experience-changed`

Subscribe through `CouchGamesSdk.Lobby.EventReceived` and compare the name with
`CouchGamesLobby.ExperienceChangedEventName`. Initialize the SDK to connect
the event bridge before opening an experience. This reserved event reaches
solo games too, with an empty `SenderUserId` (no sender).

```csharp
await CouchGamesSdk.InitializeAsync();
CouchGamesSdk.Lobby.EventReceived += message =>
{
    if (message.EventName != CouchGamesLobby.ExperienceChangedEventName)
        return;
    // Clear the board and load the new puzzle here. DataJson has the fields below.
    Debug.Log(message.DataJson);
};

var result = await CouchGamesSdk.OpenExperienceAsync(selectedExperienceId);
if (!result.Success)
    Debug.LogWarning(result.Error);
```

The event's `DataJson` contains `experienceId` (string), `experienceIndex`
(number or null), `number` (number or null), `activatedAt` (ISO instant or
null), and `initiatedByMe` (boolean). From the event onward, files, saves,
experience data/date and gameplay completion refer to the new experience.
Read the new experience from the event or `GetExperienceDataAsync()`;
`Lobby.CurrentGame` is lobby-only and null solo on the platform.

When the platform supports co-op switching, this event can also arrive because
another member switched, with `initiatedByMe: false`. Handle the event even
when your game did not initiate a switch. The current platform refuses lobby
switches, so this is forward-compatible event handling, not current group
switching support.

### Trying archive switching without the platform

Configure `CouchGamesMock.Experiences` in oldest-first order. Each entry is
openable; an empty list can stand in for the campaign lock's empty archive.
For example, add an archived challenge to the default entry:

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
    FilesDirectory = "CouchGamesExperience/Archive1"
});
```

The mock starts with a ready session on `mock-experience`. Switching updates
its data/date, uses the entry's `FilesDirectory` (or the shared
`CouchGamesMock.ExperienceFilesDirectory` when omitted), and isolates persisted
saves, metadata and statistics per experience. Opening marks an unstarted
entry as started; gameplay completion marks it completed. The configurable
list and its states live in memory; set them up each run. `ClearData()` clears
all persisted experience stores and returns to the default experience.

The reserved event is injected through the mock lobby-event path before the
successful task completes, with `initiatedByMe: true` and no sender. Missing,
current and unknown ids fail without an event. Added mock guests simulate the
lobby refusal; `SimulateHostAuthoritative` simulates the joined-guest refusal.
The switch is synchronous, so the mock does not simulate an in-flight refusal
window, network errors, release eligibility, or the campaign-lock session reset.
