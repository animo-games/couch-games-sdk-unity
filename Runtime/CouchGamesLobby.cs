using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Animo.CouchGames
{
    public sealed class CouchGamesLobby
    {
        private readonly List<CouchLobbyPlayer> _players = new List<CouchLobbyPlayer>();

        public event Action<IReadOnlyList<CouchLobbyPlayer>> PlayersChanged;
        public event Action<CouchLobbyPlayer> PlayerJoined;
        public event Action<CouchLobbyPlayer> PlayerLeft;
        public event Action<CouchLobbyEvent> EventReceived;

        public bool IsAvailable { get; internal set; }
        public IReadOnlyList<CouchLobbyPlayer> Players => _players;
        public CouchLobbyCurrentGame CurrentGame { get; internal set; }

        public CouchLobbyPlayer GetPlayer(string userId)
        {
            return _players.FirstOrDefault(player => player.UserId == userId);
        }

        public CouchLobbyPlayer GetHost()
        {
            return _players.FirstOrDefault(player => player.IsHost);
        }

        public IReadOnlyList<CouchLobbyPlayer> GetGuests()
        {
            return _players.Where(player => !player.IsHost).ToArray();
        }

        public CouchLobbyPlayer GetMe()
        {
            var me = CouchGamesSdk.LocalPlayer;
            return me == null ? null : GetPlayer(me.UserId) ?? me;
        }

        public bool IsHost()
        {
            return GetMe()?.IsHost == true;
        }

        public void SendEvent(string eventName, string dataJson = "null", CouchLobbyTarget target = null)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentException("A lobby event name is required.", nameof(eventName));

            CouchGamesSdk.SendLobbyEvent(eventName, string.IsNullOrEmpty(dataJson) ? "null" : dataJson, target);
        }

        public void SendEvent<T>(string eventName, T data, CouchLobbyTarget target = null)
        {
            SendEvent(eventName, data == null ? "null" : JsonUtility.ToJson(data), target);
        }

        public string AddMockGuest(string username = null)
        {
            return CouchGamesSdk.AddMockGuest(username);
        }

        public void RemoveMockPlayer(string userId)
        {
            CouchGamesSdk.RemoveMockPlayer(userId);
        }

        public void SimulateMockEvent(
            string eventName,
            string dataJson,
            string senderUserId,
            CouchLobbyTarget target = null)
        {
            CouchGamesSdk.SimulateMockEvent(eventName, dataJson, senderUserId, target);
        }

        internal void ReplacePlayers(CouchLobbyPlayer[] nextPlayers)
        {
            nextPlayers ??= Array.Empty<CouchLobbyPlayer>();
            var oldById = _players.ToDictionary(player => player.UserId, player => player);
            var newIds = new HashSet<string>(nextPlayers.Select(player => player.UserId));
            var joined = nextPlayers.Where(player => !oldById.ContainsKey(player.UserId)).ToArray();
            var left = _players.Where(player => !newIds.Contains(player.UserId)).ToArray();

            var changed = joined.Length > 0 || left.Length > 0;
            if (!changed)
            {
                foreach (var player in nextPlayers)
                {
                    if (!oldById.TryGetValue(player.UserId, out var old) || !player.RosterEquals(old))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            _players.Clear();
            _players.AddRange(nextPlayers);

            foreach (var player in joined)
                PlayerJoined?.Invoke(player);
            foreach (var player in left)
                PlayerLeft?.Invoke(player);
            if (changed)
                PlayersChanged?.Invoke(_players);
        }

        internal void Receive(string eventName, string dataJson, string senderUserId)
        {
            EventReceived?.Invoke(new CouchLobbyEvent(eventName, dataJson, senderUserId));
        }
    }
}
