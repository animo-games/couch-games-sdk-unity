using System;

namespace Animo.CouchGames
{
    [Serializable]
    public sealed class CouchLobbyPlayer
    {
        public string userId;
        public string username;
        public string role = "guest";
        public string status = "lobby";
        public string experienceId;
        public int controllerSlot = -1;
        public int ping = -1;

        public string UserId => userId ?? string.Empty;
        public string Username => username ?? string.Empty;
        public string Role => role ?? "guest";
        public string Status => status ?? "lobby";
        public string ExperienceId => experienceId ?? string.Empty;
        public int ControllerSlot => controllerSlot;
        public int Ping => ping;
        public bool IsHost => string.Equals(Role, "host", StringComparison.OrdinalIgnoreCase);

        internal bool RosterEquals(CouchLobbyPlayer other)
        {
            return other != null
                   && UserId == other.UserId
                   && Username == other.Username
                   && Role == other.Role
                   && Status == other.Status
                   && ExperienceId == other.ExperienceId
                   && ControllerSlot == other.ControllerSlot;
        }
    }

    [Serializable]
    public sealed class CouchLobbyCurrentGame
    {
        public string gameId;
        public string experienceId;

        public string GameId => gameId ?? string.Empty;
        public string ExperienceId => experienceId ?? string.Empty;
    }

    [Serializable]
    public sealed class CouchLobbyTarget
    {
        public string userId;
        public string role;

        public string UserId
        {
            get => userId;
            set => userId = value;
        }

        public string Role
        {
            get => role;
            set => role = value;
        }
    }

    public sealed class CouchLobbyEvent
    {
        public string EventName { get; }
        public string DataJson { get; }
        public string SenderUserId { get; }

        internal CouchLobbyEvent(string eventName, string dataJson, string senderUserId)
        {
            EventName = eventName ?? string.Empty;
            DataJson = string.IsNullOrEmpty(dataJson) ? "null" : dataJson;
            SenderUserId = senderUserId ?? string.Empty;
        }

        public T GetData<T>()
        {
            return DataJson == "null" ? default : UnityEngine.JsonUtility.FromJson<T>(DataJson);
        }
    }

    [Serializable]
    internal sealed class LobbyPlayersEnvelope
    {
        public CouchLobbyPlayer[] players = Array.Empty<CouchLobbyPlayer>();
    }

    [Serializable]
    internal sealed class LobbyEventEnvelope
    {
        public string eventName = "";
        public string dataJson = "null";
        public string senderUserId = "";
    }
}
