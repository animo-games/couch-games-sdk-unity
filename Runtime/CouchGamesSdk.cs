using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Animo.CouchGames
{
    public static class CouchGamesSdk
    {
        private static Task _initialization;
        private static int _experienceDataRequest;

        public static bool IsInitialized { get; private set; }
        public static bool IsAvailable => CouchGamesRuntime.Instance.IsAvailable;
        public static bool IsMock => CouchGamesRuntime.Instance.IsMock;
        public static string ExperienceDataJson { get; private set; } = "{}";
        public static CouchGamesLobby Lobby { get; } = new CouchGamesLobby();

        /// <summary>
        /// Files on the current experience, addressed by basename. See
        /// <see cref="CouchGamesExperience"/>.
        /// </summary>
        public static CouchGamesExperience Experience { get; } = new CouchGamesExperience();
        public static CouchLobbyPlayer LocalPlayer { get; internal set; }

        /// <summary>WebRTC signaling and TURN/STUN servers. See <see cref="CouchGamesWebRtc"/>.</summary>
        public static CouchGamesWebRtc WebRtc { get; } = new CouchGamesWebRtc();

        /// <summary>
        /// Raised with <c>(mode, code)</c> when a session's play mode is chosen or
        /// changes: "1-device" (everyone on this screen) or "2-devices" (each
        /// player on their own device). The code can be empty at first and filled
        /// later, so wait for one before connecting. It can fire during
        /// <see cref="InitializeAsync"/>, before you subscribe: read
        /// <see cref="PlayMode"/> and <see cref="ShareCode"/> after initializing
        /// as well.
        /// </summary>
        public static event Action<string, string> PlayModeSelected;

        /// <summary>"1-device", "2-devices", or null for a plain single-player launch.</summary>
        public static string PlayMode => NullIfEmpty(CouchGamesRuntime.Instance.GetPlayState().mode);

        /// <summary>The session's code; every client of a remote session gets the same one. Null when there is none.</summary>
        public static string ShareCode => NullIfEmpty(CouchGamesRuntime.Instance.GetPlayState().code);

        /// <summary>True for a guest (a client that joined someone else's session), false for the host.</summary>
        public static bool IsJoining => CouchGamesRuntime.Instance.GetPlayState().joining;

        public static Task InitializeAsync()
        {
            return _initialization ??= InitializeInternalAsync();
        }

        /// <summary>
        /// Writes <paramref name="saveData"/> to the platform. Branch on
        /// <see cref="CouchGamesResponse.Persisted"/>, NOT
        /// <see cref="CouchGamesResponse.Success"/> -- a refusal arrives, by
        /// default, as <c>Success = true, Persisted = false</c>.
        ///
        /// <paramref name="expectedRevision"/> asserts what state you are
        /// replacing (typically <see cref="CouchGamesSaveLoadResult.Revision"/>
        /// from a prior <see cref="LoadSaveResultAsync"/>). <paramref name="onConflict"/>
        /// only makes sense with a BOUNDED retry loop -- see
        /// <see cref="CouchGamesSaveConflictMode"/>.
        /// </summary>
        public static Task<CouchGamesResponse> SaveGameAsync<T>(
            T saveData,
            float progress = 0f,
            long? expectedRevision = null,
            CouchGamesSaveConflictMode onConflict = CouchGamesSaveConflictMode.Default)
        {
            return SaveGameJsonAsync(JsonUtility.ToJson(saveData), progress, expectedRevision, onConflict);
        }

        /// <summary>
        /// See <see cref="SaveGameAsync{T}"/>.
        /// </summary>
        public static Task<CouchGamesResponse> SaveGameJsonAsync(
            string saveJson,
            float progress = 0f,
            long? expectedRevision = null,
            CouchGamesSaveConflictMode onConflict = CouchGamesSaveConflictMode.Default)
        {
            var runtime = CouchGamesRuntime.Instance;
            if (runtime.IsMock)
                return Task.FromResult(runtime.SaveMock(saveJson, progress, expectedRevision, onConflict));

            var progressArg = progress.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (expectedRevision == null && onConflict == CouchGamesSaveConflictMode.Default)
            {
                // The two-argument call, byte for byte as before. A game that
                // has not opted in must not send an options object to a
                // platform build that predates one.
                return runtime.InvokeAsync(
                    "saveGame",
                    JsonArray(JsonString(saveJson ?? "{}"), progressArg));
            }

            var optionProperties = new System.Collections.Generic.List<string>();
            if (expectedRevision != null)
            {
                optionProperties.Add(
                    "\"expectedRevision\":" +
                    expectedRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (onConflict == CouchGamesSaveConflictMode.Error)
                optionProperties.Add("\"onConflict\":\"error\"");
            var optionsJson = "{" + string.Join(",", optionProperties) + "}";

            return runtime.InvokeAsync(
                "saveGame",
                JsonArray(JsonString(saveJson ?? "{}"), progressArg, optionsJson));
        }

        /// <summary>
        /// Reads the save the platform handed this session at startup.
        ///
        /// An empty/null result here is AMBIGUOUS: this player may genuinely
        /// have no save, the session may not have finished starting, or they
        /// may have joined someone else's session with their own save withheld
        /// on purpose. Use this only to re-read state you have already
        /// established. To decide whether a player is new, use
        /// <see cref="LoadSaveResultAsync"/>.
        /// </summary>
        public static Task<CouchGamesResponse> LoadLatestSaveAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("loadLatestSave", "[]");
        }

        /// <summary>
        /// The awaitable, honest counterpart to <see cref="LoadLatestSaveAsync"/>.
        /// Only <see cref="CouchGamesSaveLoadResult.IsSafeToStartFresh"/> means
        /// this is a new player. <see cref="CouchGamesSaveLoadResult.IsUnavailable"/>
        /// means a save may well exist -- keep writes off. When
        /// <see cref="CouchGamesSaveLoadResult.HostAuthoritative"/> is true the
        /// payload is your own save but the host owns the shared board -- use it
        /// as a merge base rather than live state.
        /// </summary>
        public static Task<CouchGamesSaveLoadResult> LoadSaveResultAsync()
        {
            var runtime = CouchGamesRuntime.Instance;
            if (runtime.IsMock)
                return Task.FromResult(runtime.LoadSaveResultMock());
            return LoadSaveResultBridgeAsync(runtime);
        }

        private static async Task<CouchGamesSaveLoadResult> LoadSaveResultBridgeAsync(CouchGamesRuntime runtime)
        {
            var bridge = await runtime.InvokeBridgeAsync("loadSaveResult", "[]");
            return CouchGamesSaveLoadResult.FromBridge(bridge);
        }

        /// <summary>
        /// Sends saves already made without waiting for the platform's next
        /// batch (at most one every 5 seconds). Await it before a moment that
        /// matters, such as the end of a level. Branch on
        /// <see cref="CouchGamesResponse.Persisted"/>;
        /// <see cref="CouchGamesResponse.Flushed"/> is how many saves it sent.
        /// </summary>
        public static Task<CouchGamesResponse> FlushSavesAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("flushSaves", "[]");
        }

        public static Task<CouchGamesResponse> GameplayStartAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("gameplayStart", "[]");
        }

        public static Task<CouchGamesResponse> GameplayEndAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("gameplayEnd", "[]");
        }

        /// <summary>
        /// Records a completion. <see cref="CouchGamesResponse.Streak"/> carries
        /// the player's streak with this completion already counted.
        /// </summary>
        public static Task<CouchGamesResponse> GameplayCompleteAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("gameplayComplete", "[]");
        }

        /// <summary>
        /// For games with a campaign and challenges: call once, when the player
        /// finishes the campaign (its last level or its ending), not per level.
        /// Recorded once per player; a repeat succeeds with
        /// <see cref="CouchGamesResponse.AlreadyCompleted"/>. Only a campaign
        /// session can record it ("Not a campaign session" otherwise).
        /// </summary>
        public static Task<CouchGamesResponse> CompleteCampaignAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("completeCampaign", "[]");
        }

        /// <summary>
        /// The current experience's details. In the payload, <c>number</c> is
        /// the player-facing "No. N" (null when unnumbered) and
        /// <c>experienceIndex</c> the stored index.
        /// </summary>
        public static Task<CouchGamesResponse> GetExperienceDataAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("getExperienceData", "[]");
        }

        /// <summary>
        /// Lists openable challenges, oldest first, with this player's state and
        /// each lobby member's in <c>players</c>. <see cref="CouchGamesResponse.Streak"/>
        /// carries the player's streak.
        /// Call when opening the archive, not every frame: this is a server round trip.
        /// </summary>
        public static Task<CouchGamesResponse> ListExperiencesAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("listExperiences", "[]");
        }

        /// <summary>
        /// Switches to a listed experience. Make no SDK calls until
        /// <see cref="CouchGamesLobby.ExperienceChangedEventName"/> arrives or
        /// this call returns Success = false. On success the event has already run.
        /// </summary>
        public static Task<CouchGamesResponse> OpenExperienceAsync(string experienceId)
        {
            return CouchGamesRuntime.Instance.InvokeAsync(
                "openExperience", JsonArray(JsonString(experienceId)));
        }

        public static Task<CouchGamesResponse> GetExperienceDateAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("getExperienceDate", "[]");
        }

        public static Task<CouchGamesResponse> GetGameMetadataAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("getGameMetadata", "[]");
        }

        public static Task<CouchGamesResponse> SetGameMetadataAsync(
            string category,
            string key,
            string value)
        {
            var runtime = CouchGamesRuntime.Instance;
            if (runtime.IsMock)
                return Task.FromResult(runtime.SetMockMetadata(category, key, value));
            return runtime.InvokeAsync(
                "setGameMetadata",
                JsonArray(JsonString(category), JsonString(key), JsonString(value)));
        }

        /// <summary>
        /// Removes one key. A category left with no keys disappears.
        /// <see cref="CouchGamesResponse.PayloadJson"/> is the metadata after the delete.
        /// </summary>
        public static Task<CouchGamesResponse> DeleteGameMetadataAsync(string category, string key)
        {
            var runtime = CouchGamesRuntime.Instance;
            if (runtime.IsMock)
                return Task.FromResult(runtime.DeleteMockMetadata(category, key));
            return runtime.InvokeAsync(
                "deleteGameMetadata",
                JsonArray(JsonString(category), JsonString(key)));
        }

        /// <summary>Deletes all of the player's metadata for this game.</summary>
        public static Task<CouchGamesResponse> ClearGameMetadataAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("clearGameMetadata", "[]");
        }

        /// <summary>
        /// Queues an optional gameplay event for your own analysis. Never fails;
        /// the platform sends the queue every 5 seconds or at 25 events.
        /// <paramref name="propertiesJson"/> is a flat JSON object of strings,
        /// numbers, booleans and nulls (at most 32 keys), or null. Telemetry
        /// doesn't replace the gameplay calls and doesn't save anything.
        /// </summary>
        public static void Track(string eventName, string propertiesJson = null)
        {
            var runtime = CouchGamesRuntime.Instance;
            if (runtime.IsMock)
            {
                runtime.TrackMock(eventName, propertiesJson);
                return;
            }
            _ = runtime.InvokeAsync(
                "track",
                JsonArray(JsonString(eventName), string.IsNullOrEmpty(propertiesJson) ? "null" : propertiesJson));
        }

        /// <summary>
        /// Sends queued events now instead of at the next automatic send. It
        /// reports nothing back: success doesn't mean the events were stored.
        /// </summary>
        public static Task<CouchGamesResponse> FlushTelemetryAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("flush", "[]");
        }

        /// <summary>
        /// Where this launch's shared game assets live, or null when it has none
        /// (and always in the mock). Use it to check; never build a file's URL
        /// from it: use <see cref="GetSharedFileUrlAsync"/>.
        /// </summary>
        public static async Task<string> GetSharedRootAsync()
        {
            var response = await CouchGamesRuntime.Instance.InvokeAsync("game.getSharedRoot", "[]");
            return response.Success ? ReadJsonString(response.PayloadJson) : null;
        }

        /// <summary>
        /// A shared file's URL, absolute and already encoded, by its plain path
        /// ("levels/week 1/cafe.pck"). Null when the launch has no shared assets,
        /// the path is invalid or missing, or the page navigated meanwhile.
        /// </summary>
        public static async Task<string> GetSharedFileUrlAsync(string path)
        {
            var response = await CouchGamesRuntime.Instance.InvokeAsync(
                "game.getSharedFileURL", JsonArray(JsonString(path)));
            if (!response.Success)
            {
                Debug.LogError($"Couch Games: shared file '{path}' unavailable: {response.Error}");
                return null;
            }
            return ReadJsonString(response.PayloadJson);
        }

        /// <summary>
        /// A shared file's URL, SHA-256 and size, as JSON in
        /// <see cref="CouchGamesResponse.PayloadJson"/>: <c>{ url, sha256, size }</c>.
        /// </summary>
        public static Task<CouchGamesResponse> GetSharedFileInfoAsync(string path)
        {
            return CouchGamesRuntime.Instance.InvokeAsync(
                "game.getSharedFileInfo", JsonArray(JsonString(path)));
        }

        internal static void RaisePlayModeSelected(string mode, string code)
        {
            PlayModeSelected?.Invoke(mode ?? string.Empty, code ?? string.Empty);
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>A JSON string literal (<c>"https://…"</c>) as a string; null for anything else.</summary>
        internal static string ReadJsonString(string json)
        {
            if (string.IsNullOrEmpty(json) || json[0] != '"')
                return null;
            return JsonUtility.FromJson<JsonStringBox>("{\"value\":" + json + "}")?.value;
        }

        [Serializable]
        private sealed class JsonStringBox
        {
            public string value = null;
        }

        public static Task<CouchGamesResponse> UnlockAchievementAsync(string key)
        {
            var runtime = CouchGamesRuntime.Instance;
            if (runtime.IsMock)
                return Task.FromResult(runtime.UnlockMockAchievement(key));
            return runtime.InvokeAsync(
                "unlockAchievement",
                JsonArray(JsonString(key)));
        }

        public static Task<CouchGamesResponse> GetAchievementsAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("getAchievements", "[]");
        }

        public static Task<CouchGamesResponse> GetSessionStatsAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("getSessionStats", "[]");
        }

        /// <summary>
        /// Reads the platform's synchronous save cache. Prefer
        /// <see cref="LoadLatestSaveAsync"/> in new code.
        /// </summary>
        public static string LoadLatestSaveSync()
        {
            return CouchGamesRuntime.Instance.LoadLatestSaveSync();
        }

        public static string GetExperienceDateSync()
        {
            return CouchGamesRuntime.Instance.GetExperienceDateSync();
        }

        internal static void SendLobbyEvent(
            string eventName,
            string dataJson,
            CouchLobbyTarget target)
        {
            CouchGamesRuntime.Instance.SendLobbyEvent(eventName, dataJson, target);
        }

        internal static string AddMockGuest(string username)
        {
            return CouchGamesRuntime.Instance.AddMockGuest(username);
        }

        internal static void RemoveMockPlayer(string userId)
        {
            CouchGamesRuntime.Instance.RemoveMockPlayer(userId);
        }

        internal static void SimulateMockEvent(
            string eventName,
            string dataJson,
            string senderUserId,
            CouchLobbyTarget target)
        {
            CouchGamesRuntime.Instance.SimulateMockEvent(eventName, dataJson, senderUserId, target);
        }

        // The Editor assembly is not covered by an InternalsVisibleTo from
        // Animo.CouchGames, so the mock window's Saves section reads these
        // through public members rather than internal ones.

        /// <summary>The mock backend's stored save revision, or 0 when nothing is stored.</summary>
        public static long MockStoredRevision => CouchGamesRuntime.Instance.MockStoredRevision;

        /// <summary>True when the mock backend has a stored save.</summary>
        public static bool MockHasStoredSave => CouchGamesRuntime.Instance.MockHasSave;

        private static async Task InitializeInternalAsync()
        {
            await CouchGamesRuntime.Instance.InitializeAsync();
            await RefreshExperienceDataAsync(++_experienceDataRequest);
            IsInitialized = true;
        }

        // Called on couchgames:experience-changed, before EventReceived
        // handlers run. The mock answers synchronously, so its handlers already
        // see the new data; on the platform the cache catches up when the
        // request answers. Only the latest request may write the cache.
        internal static void RefreshExperienceData()
        {
            _ = RefreshExperienceDataAsync(++_experienceDataRequest);
        }

        private static async Task RefreshExperienceDataAsync(int request)
        {
            var response = await GetExperienceDataAsync();
            if (request == _experienceDataRequest && response.Success && response.PayloadJson != "null")
                ExperienceDataJson = response.PayloadJson;
        }

        internal static string JsonString(string value)
        {
            if (value == null)
                return "null";
            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\b", "\\b")
                .Replace("\f", "\\f")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t") + "\"";
        }

        internal static string JsonArray(params string[] values)
        {
            return "[" + string.Join(",", values) + "]";
        }
    }
}
