using System;
using Animo.CouchGames;
using UnityEngine;

public sealed class LobbyQuickStart : MonoBehaviour
{
    private async void Start()
    {
        CouchGamesSdk.Lobby.PlayersChanged += OnPlayersChanged;
        CouchGamesSdk.Lobby.EventReceived += OnEventReceived;
        await CouchGamesSdk.InitializeAsync();
    }

    private static void OnPlayersChanged(System.Collections.Generic.IReadOnlyList<CouchLobbyPlayer> players)
    {
        Debug.Log($"Couch Games lobby now has {players.Count} player(s).");
    }

    private static void OnEventReceived(CouchLobbyEvent message)
    {
        Debug.Log($"Received '{message.EventName}' from {message.SenderUserId}: {message.DataJson}");
    }

    [Serializable]
    private sealed class Ping
    {
        public int sequence;
    }
}
