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

## Local testing

Open **Window > Couch Games > Mock Lobby** to add fake guests and inject lobby
events. Classic calls persist beneath Unity's `Application.persistentDataPath`.
The **Saves** section shows the stored save's revision and exposes the
`CouchGamesMock` knobs above (simulate an unavailable load, simulate joining
as a guest, force the next save to be refused, or clear all mock data) as
toggles and buttons.

## Supported platform API

- saves
- gameplay lifecycle
- experience data/date
- game metadata
- achievements
- session statistics
- lobby roster, identity, current game, and tunneled events
