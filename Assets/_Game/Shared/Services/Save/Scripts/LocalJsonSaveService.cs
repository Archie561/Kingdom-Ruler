using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace KingdomRuler.Shared.Services
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

        public LocalJsonSaveService()
        {
            _savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            _settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include
            };
        }

        public void Save(GameStateDto state)
        {
            var json = JsonConvert.SerializeObject(state, _settings);
            var tempPath = _savePath + ".tmp";

            // Write to temp file first, then move — atomic-ish on most
            // file systems, avoids a half-written save on crash.
            File.WriteAllText(tempPath, json);
            if (File.Exists(_savePath))
                File.Delete(_savePath);
            File.Move(tempPath, _savePath);
        }

        public GameStateDto Load()
        {
            if (!File.Exists(_savePath))
                return null;

            var json = File.ReadAllText(_savePath);
            return JsonConvert.DeserializeObject<GameStateDto>(json, _settings);
        }

        public void Delete()
        {
            if (File.Exists(_savePath))
                File.Delete(_savePath);
        }
    }
}
