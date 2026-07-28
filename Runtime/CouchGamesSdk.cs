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

        public static Task<CouchGamesResponse> SaveGameAsync<T>(T saveData, float progress = 0f)
        {
            return SaveGameJsonAsync(JsonUtility.ToJson(saveData), progress);
        }

        public static Task<CouchGamesResponse> SaveGameJsonAsync(string saveJson, float progress = 0f)
        {
            var runtime = CouchGamesRuntime.Instance;
            if (runtime.IsMock)
                return Task.FromResult(runtime.SaveMock(saveJson, progress));
            return runtime.InvokeAsync(
                "saveGame",
                JsonArray(JsonString(saveJson ?? "{}"), progress.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        public static Task<CouchGamesResponse> LoadLatestSaveAsync()
        {
            return CouchGamesRuntime.Instance.InvokeAsync("loadLatestSave", "[]");
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
