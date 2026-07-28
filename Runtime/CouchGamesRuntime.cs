using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Animo.CouchGames
{
    [DefaultExecutionOrder(-10000)]
    internal sealed class CouchGamesRuntime : MonoBehaviour
    {
        private const string RuntimeObjectName = "[Couch Games SDK]";
        private const string MockDirectoryName = "couch_games_mock";
        private static CouchGamesRuntime _instance;

        private readonly Dictionary<int, TaskCompletionSource<CouchGamesResponse>> _requests =
            new Dictionary<int, TaskCompletionSource<CouchGamesResponse>>();
        private readonly List<CouchLobbyPlayer> _mockPlayers = new List<CouchLobbyPlayer>();
        private MockStore _mockStore;
        private int _requestId;
        private int _nextGuest = 1;
        private long _gameplayStartedAt = -1;

        internal static CouchGamesRuntime Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var gameObject = new GameObject(RuntimeObjectName);
                DontDestroyOnLoad(gameObject);
                _instance = gameObject.AddComponent<CouchGamesRuntime>();
                return _instance;
            }
        }

        internal bool IsAvailable { get; private set; }
        internal bool IsMock { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _ = Instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

#if UNITY_WEBGL && !UNITY_EDITOR
            IsAvailable = CGU_IsAvailable() != 0;
            IsMock = !IsAvailable;
#else
            IsAvailable = true;
            IsMock = true;
#endif

            if (IsMock)
                InitializeMock();
        }

        internal Task InitializeAsync()
        {
            if (IsMock)
                return Task.CompletedTask;

#if UNITY_WEBGL && !UNITY_EDITOR
            var lobbyAvailable = CGU_InitializeLobby(
                gameObject.name,
                nameof(OnCouchGamesLobbyEvent),
                nameof(OnCouchGamesPlayersChanged),
                nameof(OnCouchGamesIdentity),
                nameof(OnCouchGamesCurrentGame)) != 0;
            CouchGamesSdk.Lobby.IsAvailable = lobbyAvailable;
#endif
            return Task.CompletedTask;
        }

        internal Task<CouchGamesResponse> InvokeAsync(string method, string argumentsJson)
        {
            if (IsMock)
                return Task.FromResult(InvokeMock(method));

#if UNITY_WEBGL && !UNITY_EDITOR
            var id = ++_requestId;
            var completion = new TaskCompletionSource<CouchGamesResponse>();
            _requests[id] = completion;
            CGU_Invoke(method, argumentsJson, gameObject.name, nameof(OnCouchGamesResponse), id);
            return completion.Task;
#else
            return Task.FromResult(CouchGamesResponse.Failed("Couch Games is unavailable."));
#endif
        }

        internal string LoadLatestSaveSync()
        {
            if (IsMock)
                return _mockStore.saveJson;

#if UNITY_WEBGL && !UNITY_EDITOR
            return PointerToStringAndFree(CGU_LoadLatestSaveSync());
#else
            return null;
#endif
        }

        internal string GetExperienceDateSync()
        {
            if (IsMock)
                return _mockStore.experienceDate;

#if UNITY_WEBGL && !UNITY_EDITOR
            return PointerToStringAndFree(CGU_GetExperienceDateSync());
#else
            return null;
#endif
        }

        internal CouchGamesResponse SaveMock(string saveJson, float progress)
        {
            _mockStore.saveJson = string.IsNullOrEmpty(saveJson) ? "{}" : saveJson;
            _mockStore.progress = progress;
            _mockStore.savedAt = DateTime.UtcNow.ToString("O");
            PersistMock();
            return CouchGamesResponse.Ok();
        }

        internal CouchGamesResponse SetMockMetadata(string category, string key, string value)
        {
            var entry = _mockStore.metadata.FirstOrDefault(item =>
                item.category == category && item.key == key);
            if (entry == null)
            {
                entry = new MockMetadataEntry { category = category, key = key };
                _mockStore.metadata.Add(entry);
            }

            entry.value = value ?? string.Empty;
            PersistMock();
            return CouchGamesResponse.Ok();
        }

        internal CouchGamesResponse UnlockMockAchievement(string key)
        {
            if (!_mockStore.achievements.Any(item => item.key == key))
            {
                _mockStore.achievements.Add(new MockAchievement
                {
                    key = key,
                    unlockedAt = DateTime.UtcNow.ToString("O")
                });
                PersistMock();
            }

            return CouchGamesResponse.Ok();
        }

        internal void SendLobbyEvent(string eventName, string dataJson, CouchLobbyTarget target)
        {
            if (IsMock)
            {
                // Platform semantics: a sender never receives its own event.
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            CGU_LobbySendEvent(
                eventName,
                string.IsNullOrEmpty(dataJson) ? "null" : dataJson,
                target == null ? "{}" : JsonUtility.ToJson(target));
#endif
        }

        internal string AddMockGuest(string username)
        {
            RequireMock();
            var userId = $"mock-guest-{_nextGuest}";
            var guest = new CouchLobbyPlayer
            {
                userId = userId,
                username = string.IsNullOrWhiteSpace(username) ? $"Guest {_nextGuest}" : username,
                role = "guest",
                status = "lobby",
                controllerSlot = LowestFreeSlot(),
                ping = 38
            };
            _nextGuest++;
            _mockPlayers.Add(guest);
            PublishMockPlayers();
            return userId;
        }

        internal void RemoveMockPlayer(string userId)
        {
            RequireMock();
            if (userId == CouchGamesSdk.LocalPlayer?.UserId)
                return;
            _mockPlayers.RemoveAll(player => player.UserId == userId);
            PublishMockPlayers();
        }

        internal void SimulateMockEvent(
            string eventName,
            string dataJson,
            string senderUserId,
            CouchLobbyTarget target)
        {
            RequireMock();
            var local = CouchGamesSdk.LocalPlayer;
            if (local == null || local.UserId == senderUserId || !TargetMatches(local, target))
                return;
            CouchGamesSdk.Lobby.Receive(eventName, dataJson, senderUserId);
        }

        // Called by the WebGL bridge through SendMessage.
        public void OnCouchGamesResponse(string json)
        {
            BridgeResponse message;
            try
            {
                message = JsonUtility.FromJson<BridgeResponse>(json);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return;
            }

            if (message == null || !_requests.TryGetValue(message.requestId, out var completion))
                return;

            _requests.Remove(message.requestId);
            completion.TrySetResult(new CouchGamesResponse(
                message.success,
                message.error,
                message.payloadJson,
                message.rawJson));
        }

        public void OnCouchGamesLobbyEvent(string json)
        {
            var message = JsonUtility.FromJson<LobbyEventEnvelope>(json);
            if (message != null)
                CouchGamesSdk.Lobby.Receive(message.eventName, message.dataJson, message.senderUserId);
        }

        public void OnCouchGamesPlayersChanged(string json)
        {
            var envelope = JsonUtility.FromJson<LobbyPlayersEnvelope>(json);
            CouchGamesSdk.Lobby.ReplacePlayers(envelope?.players);
        }

        public void OnCouchGamesIdentity(string json)
        {
            CouchGamesSdk.LocalPlayer = string.IsNullOrEmpty(json) || json == "null"
                ? null
                : JsonUtility.FromJson<CouchLobbyPlayer>(json);
        }

        public void OnCouchGamesCurrentGame(string json)
        {
            CouchGamesSdk.Lobby.CurrentGame = string.IsNullOrEmpty(json) || json == "null"
                ? null
                : JsonUtility.FromJson<CouchLobbyCurrentGame>(json);
        }

        private void InitializeMock()
        {
            _mockStore = LoadMock();
            var host = new CouchLobbyPlayer
            {
                userId = "mock-local-host",
                username = "Player 1",
                role = "host",
                status = "lobby",
                controllerSlot = 0,
                ping = 12
            };
            _mockPlayers.Add(host);
            CouchGamesSdk.LocalPlayer = host;
            CouchGamesSdk.Lobby.IsAvailable = true;
            CouchGamesSdk.Lobby.CurrentGame = new CouchLobbyCurrentGame
            {
                gameId = "mock-game",
                experienceId = "mock-experience"
            };
            PublishMockPlayers();
        }

        private CouchGamesResponse InvokeMock(string method)
        {
            switch (method)
            {
                case "loadLatestSave":
                    return CouchGamesResponse.Ok(string.IsNullOrEmpty(_mockStore.saveJson)
                        ? "{}"
                        : _mockStore.saveJson);
                case "gameplayStart":
                    _gameplayStartedAt = Stopwatch.GetTimestamp();
                    return CouchGamesResponse.Ok();
                case "gameplayEnd":
                    FoldGameplayTime();
                    PersistMock();
                    return CouchGamesResponse.Ok();
                case "gameplayComplete":
                    FoldGameplayTime();
                    _mockStore.gameplayCompleted = true;
                    PersistMock();
                    return CouchGamesResponse.Ok();
                case "getExperienceData":
                    return CouchGamesResponse.Ok(
                        "{\"experienceName\":\"Mock Experience\",\"experienceUrl\":\"https://couch.games/mock\",\"files\":[]}");
                case "getExperienceDate":
                    return CouchGamesResponse.Ok(CouchGamesSdk.JsonString(_mockStore.experienceDate));
                case "getGameMetadata":
                    return CouchGamesResponse.Ok(BuildMetadataJson());
                case "getAchievements":
                    return CouchGamesResponse.Ok(JsonUtility.ToJson(
                        new MockAchievementsEnvelope { achievements = _mockStore.achievements.ToArray() }));
                case "getSessionStats":
                    return CouchGamesResponse.Ok(JsonUtility.ToJson(new MockSessionStats
                    {
                        cumulativeGameplayTimeMs = _mockStore.cumulativeGameplayTimeMs,
                        gameplayCompleted = _mockStore.gameplayCompleted
                    }));
                default:
                    return CouchGamesResponse.Failed($"Mock call '{method}' requires typed arguments or is unsupported.");
            }
        }

        private string BuildMetadataJson()
        {
            var categories = _mockStore.metadata
                .GroupBy(entry => entry.category ?? string.Empty)
                .Select(group =>
                {
                    var values = group.Select(entry =>
                        CouchGamesSdk.JsonString(entry.key) + ":" +
                        CouchGamesSdk.JsonString(entry.value));
                    return CouchGamesSdk.JsonString(group.Key) + ":{" + string.Join(",", values) + "}";
                });
            return "{" + string.Join(",", categories) + "}";
        }

        private void FoldGameplayTime()
        {
            if (_gameplayStartedAt < 0)
                return;
            var elapsed = (Stopwatch.GetTimestamp() - _gameplayStartedAt) * 1000.0 / Stopwatch.Frequency;
            _mockStore.cumulativeGameplayTimeMs += elapsed;
            _gameplayStartedAt = -1;
        }

        private void PublishMockPlayers()
        {
            CouchGamesSdk.Lobby.ReplacePlayers(_mockPlayers.ToArray());
        }

        private int LowestFreeSlot()
        {
            var occupied = new HashSet<int>(_mockPlayers
                .Where(player => player.ControllerSlot >= 0)
                .Select(player => player.ControllerSlot));
            var slot = 0;
            while (occupied.Contains(slot))
                slot++;
            return slot;
        }

        private static bool TargetMatches(CouchLobbyPlayer player, CouchLobbyTarget target)
        {
            if (target == null)
                return true;
            if (!string.IsNullOrEmpty(target.UserId) && target.UserId != player.UserId)
                return false;
            return string.IsNullOrEmpty(target.Role) ||
                   string.Equals(target.Role, player.Role, StringComparison.OrdinalIgnoreCase);
        }

        private void RequireMock()
        {
            if (!IsMock)
                throw new InvalidOperationException("Mock controls are only available with the mock backend.");
        }

        private static string MockPath =>
            Path.Combine(Application.persistentDataPath, MockDirectoryName, "state.json");

        private static MockStore LoadMock()
        {
            try
            {
                if (File.Exists(MockPath))
                    return JsonUtility.FromJson<MockStore>(File.ReadAllText(MockPath)) ?? new MockStore();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Couch Games mock data could not be loaded: {exception.Message}");
            }

            return new MockStore();
        }

        private void PersistMock()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MockPath) ?? Application.persistentDataPath);
                File.WriteAllText(MockPath, JsonUtility.ToJson(_mockStore, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Couch Games mock data could not be saved: {exception.Message}");
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static string PointerToStringAndFree(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
                return null;
            var value = Marshal.PtrToStringUTF8(pointer);
            CGU_Free(pointer);
            return value;
        }

        [DllImport("__Internal")] private static extern int CGU_IsAvailable();
        [DllImport("__Internal")] private static extern void CGU_Invoke(
            string method, string argsJson, string gameObjectName, string callbackMethod, int requestId);
        [DllImport("__Internal")] private static extern int CGU_InitializeLobby(
            string gameObjectName,
            string eventCallback,
            string playersCallback,
            string identityCallback,
            string currentGameCallback);
        [DllImport("__Internal")] private static extern void CGU_LobbySendEvent(
            string eventName, string dataJson, string targetJson);
        [DllImport("__Internal")] private static extern IntPtr CGU_LoadLatestSaveSync();
        [DllImport("__Internal")] private static extern IntPtr CGU_GetExperienceDateSync();
        [DllImport("__Internal")] private static extern void CGU_Free(IntPtr pointer);
#endif

        [Serializable]
        private sealed class MockStore
        {
            public string saveJson = "";
            public float progress;
            public string savedAt = "";
            public string experienceDate = "2026-01-01T00:00:00.000Z";
            public double cumulativeGameplayTimeMs;
            public bool gameplayCompleted;
            public List<MockMetadataEntry> metadata = new List<MockMetadataEntry>();
            public List<MockAchievement> achievements = new List<MockAchievement>();
        }

        [Serializable]
        private sealed class MockMetadataEntry
        {
            public string category;
            public string key;
            public string value;
        }

        [Serializable]
        private sealed class MockAchievement
        {
            public string key;
            public string unlockedAt;
        }

        [Serializable]
        private sealed class MockAchievementsEnvelope
        {
            public MockAchievement[] achievements;
        }

        [Serializable]
        private sealed class MockSessionStats
        {
            public double cumulativeGameplayTimeMs;
            public bool gameplayCompleted;
        }
    }
}
