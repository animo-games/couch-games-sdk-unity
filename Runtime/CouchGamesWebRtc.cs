using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Animo.CouchGames
{
    /// <summary>
    /// The platform's WebRTC signaling and TURN/STUN servers, for games that
    /// connect players directly. Unity's WebGL player has no WebRTC of its own,
    /// so the peer connection itself is yours (a WebRTC package or your own
    /// JavaScript); this is the part that finds the other player.
    ///
    /// The platform sends host and guests to the game at the same time, so
    /// either can connect first: handle <see cref="PeerExists"/> and
    /// <see cref="PeerJoined"/> the same way, and pick who offers by role
    /// (<see cref="CouchGamesSdk.IsJoining"/>), never by arrival order. The mock
    /// doesn't simulate signaling.
    /// </summary>
    public sealed class CouchGamesWebRtc
    {
        /// <summary>Raised with <c>(senderPeerId, dataJson)</c> for each SDP or ICE signal from another peer.</summary>
        public event Action<string, string> SignalReceived;

        /// <summary>Raised with the peer id when a peer joins the room after you.</summary>
        public event Action<string> PeerJoined;

        /// <summary>Raised with the peer id for each peer already in the room when you join.</summary>
        public event Action<string> PeerExists;

        /// <summary>Raised with the peer id when a peer leaves.</summary>
        public event Action<string> PeerLeft;

        /// <summary>Raised with the room id when a room's signaling connection closes.</summary>
        public event Action<string> SignalingClosed;

        /// <summary>Raised with everyone in the room, answering <see cref="RequestPeers"/>.</summary>
        public event Action<IReadOnlyList<string>> PeersReceived;

        /// <summary>Raised with fresh ICE servers as JSON, answering <see cref="RequestIceServers"/>.</summary>
        public event Action<string> IceServersReceived;

        /// <summary>Whether the platform's signaling API is there (false in the mock).</summary>
        public bool IsAvailable { get; internal set; }

        /// <summary>
        /// Joins a signaling room; without <paramref name="roomId"/>, the current
        /// lobby's. The room is created by whoever connects first.
        /// <see cref="CouchGamesResponse.PayloadJson"/> is
        /// <c>{ peerId, roomId, iceServers }</c>.
        /// </summary>
        public Task<CouchGamesResponse> ConnectSignalingAsync(string roomId = null)
        {
            return CouchGamesRuntime.Instance.InvokeAsync(
                "webrtc.connectSignaling",
                roomId == null ? "[]" : CouchGamesSdk.JsonArray(CouchGamesSdk.JsonString(roomId)));
        }

        /// <summary>Sends an SDP offer or answer, or an ICE candidate, as JSON to another peer in the room.</summary>
        public void SendSignal(string targetPeerId, string dataJson)
        {
            _ = CouchGamesRuntime.Instance.InvokeAsync(
                "webrtc.sendSignal",
                CouchGamesSdk.JsonArray(
                    CouchGamesSdk.JsonString(targetPeerId),
                    string.IsNullOrEmpty(dataJson) ? "null" : dataJson));
        }

        /// <summary>Leaves every signaling room.</summary>
        public void Disconnect()
        {
            _ = CouchGamesRuntime.Instance.InvokeAsync("webrtc.disconnect", "[]");
        }

        /// <summary>Asks for everyone in the room; <see cref="PeersReceived"/> answers.</summary>
        public void RequestPeers()
        {
            _ = CouchGamesRuntime.Instance.InvokeAsync("webrtc.requestPeers", "[]");
        }

        /// <summary>Asks for fresh ICE servers; <see cref="IceServersReceived"/> answers.</summary>
        public void RequestIceServers()
        {
            _ = CouchGamesRuntime.Instance.InvokeAsync("webrtc.requestIceServers", "[]");
        }

        internal void Receive(WebRtcEnvelope message)
        {
            switch (message.type)
            {
                case "signal":
                    SignalReceived?.Invoke(message.peerId, message.dataJson);
                    break;
                case "peer-joined":
                    PeerJoined?.Invoke(message.peerId);
                    break;
                case "peer-exists":
                    PeerExists?.Invoke(message.peerId);
                    break;
                case "peer-left":
                    PeerLeft?.Invoke(message.peerId);
                    break;
                case "closed":
                    SignalingClosed?.Invoke(message.roomId);
                    break;
                case "peers":
                    PeersReceived?.Invoke(message.peerIds ?? Array.Empty<string>());
                    break;
                case "ice-servers":
                    IceServersReceived?.Invoke(message.iceServersJson);
                    break;
            }
        }
    }
}
