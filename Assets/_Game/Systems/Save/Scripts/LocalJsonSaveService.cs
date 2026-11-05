using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Saves game state as a JSON file in <see cref="Application.persistentDataPath"/>, with
    /// Newtonsoft.Json. Under IL2CPP the <c>link.xml</c> beside this file must keep preserving the
    /// Newtonsoft assembly, or loading fails on device only.
    /// </summary>
    public sealed class LocalJsonSaveService : ISaveService
    {
        private const string SaveFileName = "kingdom_ruler_save.json";
        private readonly string _savePath;
        private readonly JsonSerializerSettings _settings;

        /// <summary>Save into <see cref="Application.persistentDataPath"/>. Used by the game.</summary>
        public LocalJsonSaveService()
            : this(Application.persistentDataPath) { }

        /// <summary>
        /// Save into an explicit directory, so tests can exercise the file handling — atomic
        /// replace, corrupt-file recovery, temp cleanup — against a real directory.
        /// </summary>
        public LocalJsonSaveService(string saveDirectory)
        {
            if (string.IsNullOrWhiteSpace(saveDirectory))
                throw new ArgumentException("Save directory must be provided.", nameof(saveDirectory));

            _savePath = Path.Combine(saveDirectory, SaveFileName);
            _settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include
            };
        }

        /// <summary>Absolute path of the save file this instance reads and writes.</summary>
        public string SavePath => _savePath;

        /// <summary>Where an unreadable save is kept, so it can still be recovered by hand.</summary>
        public string CorruptCopyPath => _savePath + ".corrupt";

        /// <summary>
        /// Write the save file, replacing any previous one atomically.
        /// </summary>
        /// <remarks>
        /// <para>Write to a temp file, then swap with <see cref="File.Replace(string,string,string)"/>.
        /// Delete-then-move would leave a moment with no save on disk, and on mobile a kill at that
        /// moment is routine.</para>
        ///
        /// <para>Returns false rather than throwing: callers save at shutdown, where an exception
        /// has nowhere useful to go.</para>
        /// </remarks>
        public bool Save(GameStateDto state)
        {
            var tempPath = _savePath + ".tmp";

            try
            {
                var json = JsonConvert.SerializeObject(state, _settings);
                File.WriteAllText(tempPath, json);

                if (File.Exists(_savePath))
                    File.Replace(tempPath, _savePath, destinationBackupFileName: null);
                else
                    File.Move(tempPath, _savePath);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalJsonSaveService] Save failed ({ex.GetType().Name}: {ex.Message}). " +
                               "The previous save file is untouched.");
                TryDeleteTemp(tempPath);
                return false;
            }
        }

        private static void TryDeleteTemp(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Nothing useful to do — a stray .tmp is harmless and gets overwritten.
            }
        }

        /// <summary>
        /// Read the save, or null if there is none. An unreadable save is moved to
        /// <see cref="CorruptCopyPath"/> rather than deleted.
        /// </summary>
        /// <remarks>
        /// Kept, not deleted, because the failure may be ours rather than the file's — on device,
        /// IL2CPP stripping can break deserialization of a perfectly good save. Only the latest
        /// unreadable save is kept.
        /// </remarks>
        public GameStateDto Load()
        {
            if (!File.Exists(_savePath))
                return null;

            try
            {
                var json = File.ReadAllText(_savePath);
                return JsonConvert.DeserializeObject<GameStateDto>(json, _settings);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[LocalJsonSaveService] Save file could not be read ({ex.GetType().Name}: {ex.Message}). " +
                    $"Keeping it as '{CorruptCopyPath}' and starting fresh.");
                TryMoveAside();
                return null;
            }
        }

        // Must not throw: it runs inside Load's error path, and an exception here would stop startup.
        private void TryMoveAside()
        {
            try
            {
                if (File.Exists(CorruptCopyPath)) File.Delete(CorruptCopyPath);
                File.Move(_savePath, CorruptCopyPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalJsonSaveService] Could not move the unreadable save aside " +
                               $"({ex.GetType().Name}: {ex.Message}). It will be overwritten by the next save.");
            }
        }

        public void Delete()
        {
            if (File.Exists(_savePath))
                File.Delete(_savePath);
        }
    }
}
