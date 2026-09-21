using System;
using UnityEngine;

namespace Animo.CouchGames
{
    /// <summary>
    /// How <see cref="CouchGamesSdk.SaveGameAsync{T}"/> should report a refused
    /// write.
    /// </summary>
    public enum CouchGamesSaveConflictMode
    {
        /// <summary>
        /// The platform default. A refusal arrives as
        /// <c>Success = true, Persisted = false, Conflict = true</c>. Games that
        /// are already published branch on <see cref="CouchGamesResponse.Success"/>
        /// alone and often retry unboundedly; an honest failure would spin them
        /// for a whole session.
        /// </summary>
        Default,

        /// <summary>
        /// A refusal arrives as <c>Success = false</c> instead. Only use this
        /// with a BOUNDED retry loop -- the SDK never sets it for you, because
        /// only the caller knows whether its retry is capped.
        /// </summary>
        Error
    }

    public sealed class CouchGamesResponse
    {
        public bool Success { get; }
        public string Error { get; }
        public string PayloadJson { get; }
        public string RawJson { get; }

        /// <summary>
        /// Save writes only. True only when the platform confirmed the blob was
        /// stored.
        ///
        /// This is the field to branch on after <see cref="CouchGamesSdk.SaveGameAsync{T}"/>,
        /// NOT <see cref="Success"/>. The platform reports a refused write as
        /// <c>Success = true, Persisted = false</c> by default, so code that
        /// trusts <see cref="Success"/> alone will believe a save landed when it
        /// did not. A response that omits the field (an older platform build, or
        /// a call the platform accepted as a no-op) leaves this false --
        /// "did not land" is the fail-safe reading.
        ///
        /// Meaningless on any verb other than a save; it is false there because
        /// those responses say nothing about a save.
        /// </summary>
        public bool Persisted { get; }

        /// <summary>
        /// True when a save was refused because this session has never read the
        /// stored save it would have replaced -- a joined guest that has not
        /// called <see cref="CouchGamesSdk.LoadSaveResultAsync"/>, or a game that
        /// booted on an empty save cache that filled moments later.
        ///
        /// The way out is the same in both cases: call
        /// <see cref="CouchGamesSdk.LoadSaveResultAsync"/>, merge into what it
        /// returns, and write that back.
        /// </summary>
        public bool Conflict { get; }

        /// <summary>
        /// The stored save's revision, or null when the platform did not report
        /// one.
        ///
        /// Present on a successful save (the revision the write produced, ready
        /// to pass as <c>expectedRevision</c> on the next one) and on a refusal
        /// the SERVER made. A refusal the platform made client-side cannot know a
        /// revision, so this stays null there -- never require it to recover
        /// from a conflict.
        /// </summary>
        public long? CurrentRevision { get; }

        internal CouchGamesResponse(
            bool success,
            string error,
            string payloadJson,
            string rawJson,
            bool persisted = false,
            bool conflict = false,
            long? currentRevision = null)
        {
            Success = success;
            Error = error ?? string.Empty;
            PayloadJson = NormalizeJson(payloadJson);
            RawJson = NormalizeJson(rawJson);
            Persisted = persisted;
            Conflict = conflict;
            CurrentRevision = currentRevision;
        }

        public T GetPayload<T>()
        {
            if (string.IsNullOrEmpty(PayloadJson) || PayloadJson == "null")
                return default;
            return JsonUtility.FromJson<T>(PayloadJson);
        }

        public static CouchGamesResponse Ok(string payloadJson = "{}")
        {
            return new CouchGamesResponse(true, string.Empty, payloadJson, payloadJson);
        }

        public static CouchGamesResponse Failed(string error)
        {
            return new CouchGamesResponse(false, error, "null", "null");
        }

        private static string NormalizeJson(string json)
        {
            return string.IsNullOrEmpty(json) ? "null" : json;
        }
    }

    [Serializable]
    internal sealed class BridgeResponse
    {
        public int requestId = 0;
        public bool success = false;
        public string error = "";
        public string payloadJson = "null";
        public string rawJson = "null";
        public bool persisted = false;
        public bool conflict = false;
        public bool hasCurrentRevision = false;
        public long currentRevision = 0;
        public string status = "";
        public string message = "";
        public string metadataJson = "null";
        public bool hasRevision = false;
        public long revision = 0;
        public bool hostAuthoritative = false;
    }
}
