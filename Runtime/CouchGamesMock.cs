using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Animo.CouchGames
{
    /// <summary>
    /// Controls for the mock backend used in the Editor and non-platform
    /// builds. The mock simulates the platform's save no-clobber guard, so
    /// every path documented under "Saves" in the README is reachable without
    /// the real platform.
    /// </summary>
    public static class CouchGamesMock
    {
        /// <summary>
        /// Name of the default experience folder: a <c>CouchGamesExperience</c>
        /// folder at the Unity project root in the Editor, or next to the
        /// executable's data folder in a standalone build.
        /// </summary>
        public const string DefaultExperienceFilesFolderName = "CouchGamesExperience";

        private static string _experienceFilesDirectory;

        /// <summary>
        /// The challenges the mock archive lists, oldest first. The mock starts
        /// on "mock-experience"; an empty list stands in for the campaign-first
        /// lock. Lives in memory only: configure it each run.
        /// </summary>
        public static IList<CouchGamesMockExperience> Experiences { get; } =
            new List<CouchGamesMockExperience>
            {
                new CouchGamesMockExperience
                {
                    Id = "mock-experience",
                    Title = "Mock Experience",
                    ActivatedAt = "2026-01-01T00:00:00.000Z",
                    Live = true
                }
            };

        /// <summary>
        /// The streak the mock answers as <see cref="CouchGamesResponse.Streak"/>
        /// on <see cref="CouchGamesSdk.ListExperiencesAsync"/> and
        /// <see cref="CouchGamesSdk.GameplayCompleteAsync"/>. The mock does not
        /// count completions: set the value you want to show. In memory only.
        /// </summary>
        public static int Streak { get; set; }

        /// <summary>
        /// The local folder that stands in for the current experience's files.
        /// Every file directly inside it is served by basename through
        /// <see cref="CouchGamesSdk.Experience"/>; subfolders, dotfiles, and
        /// Unity <c>.meta</c> files are ignored.
        ///
        /// A relative path resolves against the project root in the Editor.
        /// Set to null to restore the default
        /// (<see cref="DefaultExperienceFilesFolderName"/>). Read on every call,
        /// so files added or removed while playing show up immediately.
        /// </summary>
        public static string ExperienceFilesDirectory
        {
            get
            {
                var root = Path.GetDirectoryName(Application.dataPath) ?? ".";
                if (string.IsNullOrEmpty(_experienceFilesDirectory))
                    return Path.Combine(root, DefaultExperienceFilesFolderName);
                return Path.IsPathRooted(_experienceFilesDirectory)
                    ? _experienceFilesDirectory
                    : Path.GetFullPath(Path.Combine(root, _experienceFilesDirectory));
            }
            set => _experienceFilesDirectory = value;
        }

        /// <summary>
        /// Pretend this session never read the stored save, so the next
        /// whole-document <see cref="CouchGamesSdk.SaveGameAsync{T}"/> is
        /// refused the way the platform refuses a joined guest's blind write.
        /// No-op when nothing is stored.
        ///
        /// Recovery works as documented: call
        /// <see cref="CouchGamesSdk.LoadSaveResultAsync"/>, merge into what it
        /// returns, and the next write is admitted again.
        /// </summary>
        public static void SimulateUnreadSave()
        {
            CouchGamesRuntime.Instance.SimulateMockUnreadSave();
        }

        /// <summary>
        /// Makes <see cref="CouchGamesSdk.LoadSaveResultAsync"/> report
        /// Unavailable -- a save may exist and could not be read, so callers
        /// must not treat the player as new.
        /// </summary>
        public static bool SimulateLoadUnavailable
        {
            get => CouchGamesRuntime.Instance.MockSimulateLoadUnavailable;
            set => CouchGamesRuntime.Instance.MockSimulateLoadUnavailable = value;
        }

        /// <summary>
        /// Makes <see cref="CouchGamesSdk.LoadSaveResultAsync"/> report
        /// HostAuthoritative -- pretend this player joined someone else's
        /// session, so the payload is their own save but the host owns the
        /// shared board.
        /// </summary>
        public static bool SimulateHostAuthoritative
        {
            get => CouchGamesRuntime.Instance.MockSimulateHostAuthoritative;
            set => CouchGamesRuntime.Instance.MockSimulateHostAuthoritative = value;
        }

        /// <summary>Deletes all persisted mock data (saves, stats, metadata, achievements).</summary>
        public static void ClearData()
        {
            CouchGamesRuntime.Instance.ClearMockData();
        }
    }

    /// <summary>
    /// An entry in <see cref="CouchGamesMock.Experiences"/>. Nullable values
    /// are written explicitly because JsonUtility does not support nullable integers.
    /// <see cref="State"/> is the local player's. The row's <c>players</c> map
    /// lists every mock lobby member: the local player at <see cref="State"/>,
    /// the others at their <see cref="PlayerStates"/> entry, or "none".
    /// </summary>
    public sealed class CouchGamesMockExperience
    {
        public string Id { get; set; }
        public int? ExperienceIndex { get; set; }
        public int? Number { get; set; }
        public string Title { get; set; }
        public string ActivatedAt { get; set; }
        public bool Live { get; set; }
        public string State { get; set; } = "none";

        /// <summary>Other mock lobby members' states, by userId.</summary>
        public IDictionary<string, string> PlayerStates { get; } = new Dictionary<string, string>();

        internal string ToJson(IEnumerable<CouchLobbyPlayer> members, string localUserId)
        {
            var players = (members ?? Enumerable.Empty<CouchLobbyPlayer>())
                .Select(member => member?.UserId)
                .Where(userId => !string.IsNullOrEmpty(userId))
                .Select(userId => CouchGamesSdk.JsonString(userId) + ":" +
                                  CouchGamesSdk.JsonString(StateOf(userId, localUserId)));
            return "{\"id\":" + CouchGamesSdk.JsonString(Id) +
                   ",\"experienceIndex\":" + IndexJson +
                   ",\"number\":" + NumberJson +
                   ",\"title\":" + CouchGamesSdk.JsonString(Title) +
                   ",\"activatedAt\":" + CouchGamesSdk.JsonString(ActivatedAt) +
                   ",\"live\":" + (Live ? "true" : "false") +
                   ",\"state\":" + CouchGamesSdk.JsonString(State) +
                   ",\"players\":{" + string.Join(",", players) + "}}";
        }

        private string StateOf(string userId, string localUserId)
        {
            if (userId == localUserId)
                return State;
            return PlayerStates.TryGetValue(userId, out var state) && state != null ? state : "none";
        }

        internal string IndexJson => ExperienceIndex?.ToString(CultureInfo.InvariantCulture) ?? "null";
        internal string NumberJson => Number?.ToString(CultureInfo.InvariantCulture) ?? "null";
    }
}
