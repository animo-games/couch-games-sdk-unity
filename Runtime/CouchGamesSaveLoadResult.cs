using UnityEngine;

namespace Animo.CouchGames
{
    // The result of CouchGamesSdk.LoadSaveResultAsync() -- the awaitable,
    // honest counterpart to LoadLatestSaveAsync() / LoadLatestSaveSync().
    //
    // LoadLatestSave* reads the save the platform handed this session at
    // startup. It returns an empty payload for three different reasons -- this
    // player has no save, the session had not finished starting, or the player
    // joined someone else's session and their save was deliberately withheld --
    // and it cannot tell you which. Code that reads "empty" as "new player"
    // starts from scratch, and its next whole-document save destroys real
    // progress.
    //
    // LoadSaveResultAsync() separates those cases. Only Status == NotFound
    // means "new player"; see IsSafeToStartFresh.

    public enum CouchGamesSaveStatus
    {
        /// <summary>A stored save was found and is in <see cref="CouchGamesSaveLoadResult.PayloadJson"/>.</summary>
        Found,

        /// <summary>Confirmed by the platform: this player has no save for this experience.</summary>
        NotFound,

        /// <summary>
        /// The save could not be read. Says NOTHING about whether one exists, so
        /// callers must not treat it as a new player.
        /// </summary>
        Unavailable
    }

    public sealed class CouchGamesSaveLoadResult
    {
        /// <summary>
        /// One of the <see cref="CouchGamesSaveStatus"/> values. An unrecognised
        /// or missing status is normalised to <see cref="CouchGamesSaveStatus.Unavailable"/>
        /// -- the only reading that cannot cause a game to overwrite a save it
        /// never saw.
        /// </summary>
        public CouchGamesSaveStatus Status { get; }

        /// <summary>The platform's explanation. Empty on a clean read.</summary>
        public string Message { get; }

        /// <summary>
        /// The stored save as JSON, or "null" when there is none to serve.
        /// </summary>
        public string PayloadJson { get; }

        /// <summary>Game metadata that came back with the save, as JSON. "null" when none was returned.</summary>
        public string MetadataJson { get; }

        /// <summary>
        /// The revision <see cref="PayloadJson"/> was read at, or null when the
        /// platform did not report one. Pass it to
        /// <see cref="CouchGamesSdk.SaveGameAsync{T}"/>'s <c>expectedRevision</c>
        /// to write deliberately against the state you actually saw.
        /// </summary>
        public long? Revision { get; }

        /// <summary>
        /// True while this player is joined to someone else's session.
        ///
        /// <see cref="PayloadJson"/> is then this player's OWN save, but the
        /// HOST owns the shared board. Use it as a merge base -- read it, merge
        /// this session's contributions into it, and write that back. Rendering
        /// it as the live board would show a guest their solo game instead of
        /// the host's.
        /// </summary>
        public bool HostAuthoritative { get; }

        public bool IsFound => Status == CouchGamesSaveStatus.Found;
        public bool IsNewPlayer => Status == CouchGamesSaveStatus.NotFound;
        public bool IsUnavailable => Status == CouchGamesSaveStatus.Unavailable;

        /// <summary>
        /// True only when the platform confirmed this player has no save. This
        /// is the ONLY condition under which callers may initialise empty state
        /// and then save it -- every other status leaves open the possibility
        /// that a save exists, and writing a fresh document would destroy it.
        ///
        /// On a Found result you may also write, but only after merging into
        /// <see cref="PayloadJson"/> -- and if <see cref="HostAuthoritative"/> is
        /// true, merging is mandatory: you are a guest whose solo save is not
        /// the board on screen.
        /// </summary>
        public bool IsSafeToStartFresh => IsNewPlayer;

        internal CouchGamesSaveLoadResult(
            CouchGamesSaveStatus status,
            string message,
            string payloadJson,
            string metadataJson,
            long? revision,
            bool hostAuthoritative)
        {
            Status = status;
            Message = message ?? string.Empty;
            PayloadJson = string.IsNullOrEmpty(payloadJson) ? "null" : payloadJson;
            MetadataJson = string.IsNullOrEmpty(metadataJson) ? "null" : metadataJson;
            Revision = revision;
            HostAuthoritative = hostAuthoritative;
        }

        public T GetPayload<T>()
        {
            if (string.IsNullOrEmpty(PayloadJson) || PayloadJson == "null")
                return default;
            return JsonUtility.FromJson<T>(PayloadJson);
        }

        internal static CouchGamesSaveLoadResult Unavailable(string message, bool hostAuthoritative = false)
        {
            return new CouchGamesSaveLoadResult(
                CouchGamesSaveStatus.Unavailable,
                message,
                "null",
                "null",
                null,
                hostAuthoritative);
        }

        internal static CouchGamesSaveLoadResult FromBridge(BridgeResponse message)
        {
            // A rejected promise, or an older platform build with no
            // loadSaveResult function at all, arrives here as a bridge failure
            // rather than a platform response.
            if (message == null || !message.success)
            {
                var failureText = message != null && !string.IsNullOrEmpty(message.error)
                    ? message.error
                    : "CouchGames.loadSaveResult is unavailable";
                return Unavailable(failureText);
            }

            // Anything unrecognised -- a rejected promise's shape, a future
            // status this build predates -- degrades to "unavailable" rather
            // than reaching a game as a status it will not match.
            CouchGamesSaveStatus status;
            switch (message.status)
            {
                case "found":
                    status = CouchGamesSaveStatus.Found;
                    break;
                case "not_found":
                    status = CouchGamesSaveStatus.NotFound;
                    break;
                default:
                    status = CouchGamesSaveStatus.Unavailable;
                    break;
            }

            var resultMessage = message.message ?? string.Empty;

            // `not_found` carries an explicit null payload, and a rejected call
            // carries none at all; both must be treated as unusable rather than
            // crashing a JSON parse.
            var payloadJson = message.payloadJson;
            var payloadUsable = !string.IsNullOrEmpty(payloadJson)
                && payloadJson != "null"
                && payloadJson.TrimStart().StartsWith("{");

            // A "found" whose payload is not a usable object is the most
            // dangerous result this class can produce, and it must not be passed
            // through. Reaching "found" means the platform ALREADY synced this
            // session's revision, so both no-clobber guards are down; code that
            // follows IsFound into an empty payload would start from nothing and
            // its next whole-document save WOULD be admitted, over the save it
            // just failed to read. Degrading to unavailable keeps writes off
            // instead.
            if (status == CouchGamesSaveStatus.Found && !payloadUsable)
            {
                status = CouchGamesSaveStatus.Unavailable;
                resultMessage = "Stored save could not be read";
            }

            if (status == CouchGamesSaveStatus.Unavailable && string.IsNullOrEmpty(resultMessage))
                resultMessage = "Save could not be loaded";

            var metadataJson = string.IsNullOrEmpty(message.metadataJson) ? "null" : message.metadataJson;
            var revision = message.hasRevision ? message.revision : (long?)null;

            return new CouchGamesSaveLoadResult(
                status,
                resultMessage,
                payloadUsable ? payloadJson : "null",
                metadataJson,
                revision,
                message.hostAuthoritative);
        }
    }
}
