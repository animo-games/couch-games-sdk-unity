using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Animo.CouchGames
{
    /// <summary>
    /// Files on the current experience, exposed as
    /// <see cref="CouchGamesSdk.Experience"/>.
    ///
    /// An experience is a dated content drop for a game -- a level pack, a
    /// room, a puzzle set -- uploaded on the platform and addressed by
    /// BASENAME, never by URL. The URL embeds an experience id that rotates,
    /// so any game keying off it breaks the next day.
    ///
    /// Delivery is race-free: the platform retains the bytes, so
    /// <see cref="GetFileAsync"/> resolves whether the download finished
    /// before or after you asked.
    ///
    /// In the Editor and standalone builds the mock serves files from a local
    /// folder instead; see <see cref="CouchGamesMock.ExperienceFilesDirectory"/>.
    /// </summary>
    public sealed class CouchGamesExperience
    {
        internal CouchGamesExperience()
        {
        }

        /// <summary>
        /// Basenames of every file on the current experience, in the order the
        /// platform lists them (upload order). Empty when the platform has no
        /// experience files, or off-platform when the mock folder is missing.
        /// </summary>
        public Task<IReadOnlyList<string>> ListFilesAsync()
        {
            return Task.FromResult(ListFiles());
        }

        /// <summary>True when <paramref name="fileName"/> is on the current experience.</summary>
        public bool HasFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;
            foreach (var name in ListFiles())
            {
                if (string.Equals(name, fileName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Bytes for one file, by basename. Null on failure, with the reason
        /// logged as an error -- an unknown name, a failed download, or an
        /// experience rotation mid-flight. A zero-byte file returns an empty
        /// array, not null.
        /// </summary>
        public async Task<byte[]> GetFileAsync(string fileName)
        {
            var result = await CouchGamesRuntime.Instance.ExperienceGetFileAsync(fileName);
            if (!result.Success)
            {
                Debug.LogError($"CouchGames: experience file '{fileName}': " +
                               (string.IsNullOrEmpty(result.Error) ? "unknown error" : result.Error));
                return null;
            }
            return result.Bytes ?? Array.Empty<byte>();
        }

        /// <summary>
        /// The file decoded as UTF-8, with any byte-order mark removed. Null on
        /// failure (see <see cref="GetFileAsync"/>).
        /// </summary>
        public async Task<string> GetFileTextAsync(string fileName)
        {
            var bytes = await GetFileAsync(fileName);
            if (bytes == null)
                return null;
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        /// <summary>
        /// The file parsed with <see cref="JsonUtility.FromJson{T}(string)"/>.
        /// Returns <c>default</c> if the file is missing, empty, or not valid
        /// JSON for <typeparamref name="T"/>, with the reason logged as an error.
        ///
        /// JsonUtility only reads a JSON OBJECT at the top level. For a file
        /// whose root is an array, use <see cref="GetFileTextAsync"/> and a
        /// parser of your choice, or wrap the array in an object.
        /// </summary>
        public async Task<T> GetFileJsonAsync<T>(string fileName)
        {
            var text = await GetFileTextAsync(fileName);
            if (text == null)
                return default;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogError($"CouchGames: experience file '{fileName}' is empty");
                return default;
            }
            try
            {
                return JsonUtility.FromJson<T>(text);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"CouchGames: experience file '{fileName}' is not valid JSON: {exception.Message}");
                return default;
            }
        }

        private static IReadOnlyList<string> ListFiles()
        {
            return CouchGamesRuntime.Instance.ExperienceListFiles();
        }
    }

    internal readonly struct ExperienceFileResult
    {
        public readonly bool Success;
        public readonly string Error;
        public readonly byte[] Bytes;

        private ExperienceFileResult(bool success, string error, byte[] bytes)
        {
            Success = success;
            Error = error;
            Bytes = bytes;
        }

        public static ExperienceFileResult Ok(byte[] bytes) => new ExperienceFileResult(true, "", bytes);
        public static ExperienceFileResult Failed(string error) => new ExperienceFileResult(false, error, null);
    }
}
