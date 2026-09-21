using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Animo.CouchGames
{
    public static class CouchGamesSdk
    {
        private static Task _initialization;

        public static bool IsInitialized { get; private set; }
        public static bool IsAvailable => CouchGamesRuntime.Instance.IsAvailable;
        public static bool IsMock => CouchGamesRuntime.Instance.IsMock;
        public static string ExperienceDataJson { get; private set; } = "{}";
        public static CouchGamesLobby Lobby { get; } = new CouchGamesLobby();
        public static CouchLobbyPlayer LocalPlayer { get; internal set; }

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

        public static Task<CouchGamesResponse> GameplayStartAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("gameplayStart", "[]");
        }

        public static Task<CouchGamesResponse> GameplayEndAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("gameplayEnd", "[]");
        }

        public static Task<CouchGamesResponse> GameplayCompleteAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("gameplayComplete", "[]");
        }

        public static Task<CouchGamesResponse> GetExperienceDataAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("getExperienceData", "[]");
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
            var response = await GetExperienceDataAsync();
            if (response.Success && response.PayloadJson != "null")
                ExperienceDataJson = response.PayloadJson;
            IsInitialized = true;
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
