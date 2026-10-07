using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace KingdomRuler.Systems.Save
{
    /// <summary>
    /// Saves game state as a JSON file in Application.persistentDataPath.
    /// Uses Newtonsoft.Json (not JsonUtility) per project convention.
    /// IL2CPP caveat: ensure link.xml preserves Newtonsoft.Json assembly.
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
        /// Save into an explicit directory.
        /// </summary>
        /// <remarks>
        /// This overload exists so the file handling — atomic replace, corrupt-file
        /// recovery, temp cleanup — can be tested against a real directory. Those paths are
        /// the whole point of this class, and pinning the location to
        /// <c>persistentDataPath</c> left them completely uncovered.
        /// </remarks>
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

        /// <summary>
        /// Write the save file, replacing any previous one atomically.
        /// </summary>
        /// <remarks>
        /// Write-to-temp then swap. The swap uses <see cref="File.Replace(string,string,string)"/>
        /// rather than delete-then-move: the latter leaves a window in which the real save
        /// has been deleted and the replacement not yet renamed, so a crash or a kill (which
        /// on mobile is routine, not exceptional) during that window loses the game outright —
        /// the exact failure the temp file was there to prevent.
        ///
        /// Returns false rather than throwing: a save that fails is bad, but taking down the
        /// caller mid-shutdown is worse, and there is nothing useful for a caller to do about
        /// a full disk beyond logging it.
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
                    "Deleting corrupt save and starting fresh.");
                Delete();
                return null;
            }
        }

        public void Delete()
        {
            if (File.Exists(_savePath))
                File.Delete(_savePath);
        }
    }
}
