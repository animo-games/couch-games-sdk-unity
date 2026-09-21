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
}
