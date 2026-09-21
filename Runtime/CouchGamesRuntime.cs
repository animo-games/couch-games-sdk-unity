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

        private readonly Dictionary<int, TaskCompletionSource<BridgeResponse>> _requests =
            new Dictionary<int, TaskCompletionSource<BridgeResponse>>();
        private readonly List<CouchLobbyPlayer> _mockPlayers = new List<CouchLobbyPlayer>();
        private MockStore _mockStore;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int _requestId;
#endif
        private int _nextGuest = 1;
        private long _gameplayStartedAt = -1;

        // The stored revision this simulated session has seen. Set to the
        // stored revision at startup, the way the platform hands a session its
        // save, so the editor's normal save-without-loading flow keeps working
        // unchanged.
        private long _mockSessionKnownRevision;

        /// <summary>
        /// Makes <see cref="LoadSaveResultMock"/> report Unavailable -- a save
        /// may exist and could not be read, so callers must not treat the
        /// player as new.
        /// </summary>
        internal bool MockSimulateLoadUnavailable { get; set; }

        /// <summary>
        /// Makes <see cref="LoadSaveResultMock"/> report HostAuthoritative --
        /// this player joined someone else's session, so the payload is their
        /// own save and the host owns the shared board.
        /// </summary>
        internal bool MockSimulateHostAuthoritative { get; set; }

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

        internal async Task<CouchGamesResponse> InvokeAsync(string method, string argumentsJson)
        {
            if (IsMock)
                return InvokeMock(method);

            var bridge = await InvokeBridgeAsync(method, argumentsJson);
            return new CouchGamesResponse(
                bridge.success,
                bridge.error,
                bridge.payloadJson,
                bridge.rawJson,
                bridge.persisted,
                bridge.conflict,
                bridge.hasCurrentRevision ? bridge.currentRevision : (long?)null);
        }

        internal Task<BridgeResponse> InvokeBridgeAsync(string method, string argumentsJson)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var id = ++_requestId;
            var completion = new TaskCompletionSource<BridgeResponse>();
            _requests[id] = completion;
            CGU_Invoke(method, argumentsJson, gameObject.name, nameof(OnCouchGamesResponse), id);
            return completion.Task;
#else
            return Task.FromResult(new BridgeResponse { success = false, error = "Couch Games is unavailable." });
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

        internal CouchGamesResponse SaveMock(
            string saveJson,
            float progress,
            long? expectedRevision,
            CouchGamesSaveConflictMode onConflict)
        {
            var stored = StoredMockRevision();
            if (!AdmitsMockSaveWrite(stored, expectedRevision))
            {
                // The platform's shape for a refusal, both modes. Only
                // `Success` differs between them; `Persisted` is accurate in
                // both, which is why it is the field callers should branch on.
                return new CouchGamesResponse(
                    onConflict != CouchGamesSaveConflictMode.Error,
                    "Save skipped: this session has not loaded the stored save it would replace",
                    "null",
                    "null",
                    persisted: false,
                    conflict: true,
                    // A refusal implies something is stored: AdmitsMockSaveWrite
                    // always admits at revision 0.
                    currentRevision: stored);
            }

            var next = stored + 1;
            _mockStore.saveJson = string.IsNullOrEmpty(saveJson) ? "{}" : saveJson;
            _mockStore.progress = progress;
            _mockStore.savedAt = DateTime.UtcNow.ToString("O");
            _mockStore.revision = next;
            // A session that just wrote knows what is stored: itself. Without
            // this a new player's SECOND save would be refused as a blind
            // overwrite of their own.
            _mockSessionKnownRevision = next;
            PersistMock();
            return new CouchGamesResponse(true, "", "{}", "{}", persisted: true, conflict: false, currentRevision: next);
        }

        /// <summary>
        /// The stored save's revision, or 0 when nothing is stored. Saves
        /// written by the 0.1.0 mock have no revision and count as 1.
        /// </summary>
        private long StoredMockRevision()
        {
            if (string.IsNullOrEmpty(_mockStore.saveJson))
                return 0;
            return Math.Max(1, _mockStore.revision);
        }

        /// <summary>
        /// Mirrors the platform's no-clobber guard: a write lands unless it
        /// would replace a stored save this session has never seen.
        /// </summary>
        private bool AdmitsMockSaveWrite(long stored, long? expected)
        {
            if (stored == 0)
                return true; // Nothing stored, so nothing to destroy.
            if (_mockSessionKnownRevision == stored)
                return true;
            if (expected.HasValue && expected.Value == stored)
                return true; // The caller asserted what it replaces, and is right.
            return false;
        }

        /// <summary>
        /// The awaitable, honest counterpart to <see cref="LoadLatestSaveSync"/>
        /// for the mock backend. See <see cref="CouchGamesSaveLoadResult"/>.
        /// </summary>
        internal CouchGamesSaveLoadResult LoadSaveResultMock()
        {
            if (MockSimulateLoadUnavailable)
            {
                // Note the deliberate asymmetry with the platform: an
                // unavailable read does NOT mark the save as read, so a save
                // that follows one is still refused. That is what makes
                // "unavailable means keep writes off" testable in the editor.
                return CouchGamesSaveLoadResult.Unavailable(
                    "Simulated: save could not be loaded", MockSimulateHostAuthoritative);
            }

            // Both authoritative answers sync the session, exactly as the
            // platform does: the game has now seen what is stored, or
            // confirmed nothing is, so its next write is intentional rather
            // than blind. This is what makes the documented recovery -- load,
            // merge, write -- work against the mock.
            _mockSessionKnownRevision = StoredMockRevision();

            if (string.IsNullOrEmpty(_mockStore.saveJson))
            {
                return new CouchGamesSaveLoadResult(
                    CouchGamesSaveStatus.NotFound,
                    "No save found",
                    "null",
                    BuildMetadataJson(),
                    null,
                    MockSimulateHostAuthoritative);
            }

            return new CouchGamesSaveLoadResult(
                CouchGamesSaveStatus.Found,
                "",
                _mockStore.saveJson,
                BuildMetadataJson(),
                StoredMockRevision(),
                MockSimulateHostAuthoritative);
        }

        /// <summary>
        /// Pretend this session never read the stored save, so the next
        /// whole-document save is refused the way the platform refuses a
        /// joined guest's blind write. No-op when nothing is stored -- the
        /// platform always admits a new player's first save, and so does the
        /// mock.
        ///
        /// Recovery works as documented: call
        /// <see cref="CouchGamesSdk.LoadSaveResultAsync"/>, merge into what it
        /// returns, and the next write is admitted again.
        /// </summary>
        internal void SimulateMockUnreadSave()
        {
            RequireMock();
            _mockSessionKnownRevision = -1;
        }

        internal void ClearMockData()
        {
            RequireMock();
            _mockStore = new MockStore();
            _mockSessionKnownRevision = 0;
            try
            {
                if (File.Exists(MockPath))
                    File.Delete(MockPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Couch Games mock data could not be cleared: {exception.Message}");
            }
        }

        internal long MockStoredRevision => StoredMockRevision();

        internal bool MockHasSave => !string.IsNullOrEmpty(_mockStore?.saveJson);

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
            completion.TrySetResult(message);
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
            // The platform hands a session its save at startup, so the editor's
            // normal save-without-loading flow keeps working.
            _mockSessionKnownRevision = StoredMockRevision();
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
            var categoryProperties = _mockStore.metadata
                .GroupBy(entry => entry.category ?? string.Empty)
                .Select(group =>
                {
                    var values = group.Select(entry =>
                        CouchGamesSdk.JsonString(entry.key) + ":" +
                        CouchGamesSdk.JsonString(entry.value));
                    return CouchGamesSdk.JsonString(group.Key) + ":{" + string.Join(",", values) + "}";
                });
            var rootProperties = _mockStore.metadata
                .GroupBy(entry => entry.key ?? string.Empty)
                .Select(group =>
                {
                    var entry = group.Last();
                    return CouchGamesSdk.JsonString(entry.key) + ":" +
                           CouchGamesSdk.JsonString(entry.value);
                });
            return "{" + string.Join(",", categoryProperties.Concat(rootProperties)) + "}";
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
            public long revision;
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
