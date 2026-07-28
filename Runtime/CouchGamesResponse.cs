using System;
using UnityEngine;

namespace Animo.CouchGames
{
    public sealed class CouchGamesResponse
    {
        public bool Success { get; }
        public string Error { get; }
        public string PayloadJson { get; }
        public string RawJson { get; }

        internal CouchGamesResponse(bool success, string error, string payloadJson, string rawJson)
        {
            Success = success;
            Error = error ?? string.Empty;
            PayloadJson = NormalizeJson(payloadJson);
            RawJson = NormalizeJson(rawJson);
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
        public int requestId;
        public bool success;
        public string error;
        public string payloadJson;
        public string rawJson;
    }
}
