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

## Supported platform API

- saves
- gameplay lifecycle
- experience data/date
- game metadata
- achievements
- session statistics
- lobby roster, identity, current game, and tunneled events
