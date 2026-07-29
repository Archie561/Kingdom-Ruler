using System.IO;
using NUnit.Framework;
using Newtonsoft.Json;
using KingdomRuler.Shared.Services;

namespace KingdomRuler.Tests.EditMode.Shared
{
    [TestFixture]
    public sealed class SaveServiceTests
    {
        private string _tempDir;
        private string _savePath;
        private LocalJsonSaveService _service;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "KingdomRulerTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
            _savePath = Path.Combine(_tempDir, "kingdom_ruler_save.json");
            // LocalJsonSaveService uses Application.persistentDataPath,
            // so we test the DTO serialization logic directly here.
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Test]
        public void GameStateDto_SchemaVersionDefaultsToOne()
        {
            var dto = new GameStateDto();
            Assert.AreEqual(1, dto.SchemaVersion);
        }

        [Test]
        public void GameStateDto_RoundTrip_PreservesSchemaVersion()
        {
            var dto = new GameStateDto { SchemaVersion = 1 };

            var json = JsonConvert.SerializeObject(dto);
            var loaded = JsonConvert.DeserializeObject<GameStateDto>(json);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.SchemaVersion);
        }

        [Test]
        public void GameStateDto_Serialization_IncludesSchemaVersionKey()
        {
            var dto = new GameStateDto();
            var json = JsonConvert.SerializeObject(dto);

            Assert.IsTrue(json.Contains("schemaVersion"),
                "JSON should contain 'schemaVersion' key (camelCase via JsonProperty).");
        }

        [Test]
        public void GameStateDto_Deserialization_FromManualJson()
        {
            var json = "{\"schemaVersion\": 1}";
            var loaded = JsonConvert.DeserializeObject<GameStateDto>(json);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.SchemaVersion);
        }

        [Test]
        public void GameStateDto_Deserialization_UnknownFields_DoesNotThrow()
        {
            // Forward compatibility: old code loading a save with new fields
            var json = "{\"schemaVersion\": 1, \"futureField\": 42}";
            GameStateDto loaded = null;

            Assert.DoesNotThrow(() =>
            {
                loaded = JsonConvert.DeserializeObject<GameStateDto>(json);
            });

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.SchemaVersion);
        }
    }
}
