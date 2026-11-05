using System.IO;
using NUnit.Framework;
using Newtonsoft.Json;
using KingdomRuler.Systems.Save;

namespace KingdomRuler.Tests.EditMode.Systems
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
        public void GameStateDto_SchemaVersionDefaultsToCurrent()
        {
            var dto = new GameStateDto();
            Assert.AreEqual(GameStateDto.CurrentSchemaVersion, dto.SchemaVersion);
        }

        [Test]
        public void GameStateDto_RoundTrip_PreservesSchemaVersion()
        {
            var dto = new GameStateDto { SchemaVersion = 1 };

            var json = JsonConvert.SerializeObject(dto);
            var loaded = JsonConvert.DeserializeObject<GameStateDto>(json);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.SchemaVersion,
                "An older save's version must survive the round trip so a migration can key off it.");
        }

        /// <summary>
        /// v2 dropped CharacteristicStateDto.permanentFloor. A v1 save still carries that
        /// key, so loading one must not throw — the value is simply ignored, and the floor
        /// it described is recoverable from `level` (they were always equal).
        /// </summary>
        [Test]
        public void CharacteristicStateDto_LoadsV1Json_IgnoringRemovedPermanentFloor()
        {
            var json = "{\"level\": 4, \"pointsIntoCurrentLevel\": 25.0, \"permanentFloor\": 4}";
            CharacteristicStateDto loaded = null;

            Assert.DoesNotThrow(() =>
            {
                loaded = JsonConvert.DeserializeObject<CharacteristicStateDto>(json);
            });

            Assert.IsNotNull(loaded);
            Assert.AreEqual(4, loaded.Level);
            Assert.AreEqual(25, loaded.PointsIntoCurrentLevel, "A whole number saved as 25.0 must load as an int.");
        }

        /// <summary>Points are whole numbers, but a save may hold them as decimals; it must still load.</summary>
        [TestCase("870.0", 870)]
        [TestCase("86.6",  87)]
        [TestCase("86.5",  87)]
        [TestCase("64",    64)]
        public void CharacteristicPoints_StoredAsADecimal_LoadAsTheNearestWholeNumber(string stored, int expected)
        {
            var json   = "{\"level\": 3, \"pointsIntoCurrentLevel\": " + stored + "}";
            var loaded = JsonConvert.DeserializeObject<CharacteristicStateDto>(json);

            Assert.AreEqual(expected, loaded.PointsIntoCurrentLevel);
        }

        [Test]
        public void CharacteristicPoints_AreWrittenAsWholeNumbers()
        {
            var json = JsonConvert.SerializeObject(new CharacteristicStateDto { Level = 2, PointsIntoCurrentLevel = 86 });
            StringAssert.Contains("\"pointsIntoCurrentLevel\":86", json.Replace(" ", ""));
            StringAssert.DoesNotContain("86.0", json);
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
