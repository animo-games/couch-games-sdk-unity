using UnityEditor;
using UnityEngine;

namespace Animo.CouchGames.Editor
{
    public sealed class CouchGamesMockWindow : EditorWindow
    {
        private string _guestName = "";
        private string _eventName = "handshake/input";
        private string _eventData = "{}";
        private string _senderUserId = "";

        [MenuItem("Window/Couch Games/Mock Lobby")]
        private static void Open()
        {
            GetWindow<CouchGamesMockWindow>("Couch Games Mock");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use the mock lobby.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Backend", CouchGamesSdk.IsMock ? "Mock" : "Web");
            EditorGUILayout.LabelField("Local player", CouchGamesSdk.Lobby.GetMe()?.Username ?? "(none)");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Players", EditorStyles.boldLabel);
            foreach (var player in CouchGamesSdk.Lobby.Players)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"{player.Username} ({player.Role}, slot {player.ControllerSlot})");
                    if (!player.IsHost && GUILayout.Button("Remove", GUILayout.Width(70)))
                        CouchGamesSdk.Lobby.RemoveMockPlayer(player.UserId);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _guestName = EditorGUILayout.TextField(_guestName);
                if (GUILayout.Button("Add Guest", GUILayout.Width(90)))
                {
                    _senderUserId = CouchGamesSdk.Lobby.AddMockGuest(_guestName);
                    _guestName = "";
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Inject Event", EditorStyles.boldLabel);
            _senderUserId = EditorGUILayout.TextField("Sender user ID", _senderUserId);
            _eventName = EditorGUILayout.TextField("Event", _eventName);
            EditorGUILayout.LabelField("Data JSON");
            _eventData = EditorGUILayout.TextArea(_eventData, GUILayout.MinHeight(60));

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_senderUserId) ||
                                               string.IsNullOrWhiteSpace(_eventName)))
            {
                if (GUILayout.Button("Deliver to Local Player"))
                {
                    CouchGamesSdk.Lobby.SimulateMockEvent(
                        _eventName,
                        string.IsNullOrWhiteSpace(_eventData) ? "null" : _eventData,
                        _senderUserId);
                }
            }
        }
    }
}
