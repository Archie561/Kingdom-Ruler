using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using KingdomRuler.Shared.Services;

namespace KingdomRuler.Tests.EditMode.Shared
{
    /// <summary>
    /// Exercises the actual file handling against a real directory — the atomic replace,
    /// corrupt-file recovery and temp-file cleanup that make up save integrity
    /// (CLAUDE.md §1, priority #2).
    /// </summary>
    [TestFixture]
    public sealed class LocalJsonSaveServiceTests
    {
        private string _dir;
        private LocalJsonSaveService _service;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "KingdomRulerSaveTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
            _service = new LocalJsonSaveService(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static GameStateDto StateWithGold(long gold) => new GameStateDto
        {
            Ledger = new LedgerStateDto { Gold = gold }
        };

        [Test]
        public void Load_WithNoSaveFile_ReturnsNull()
        {
            Assert.IsNull(_service.Load());
        }

        [Test]
        public void Save_ThenLoad_RoundTripsState()
        {
            Assert.IsTrue(_service.Save(StateWithGold(4242)));

            var loaded = _service.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(GameStateDto.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(4242, loaded.Ledger.Gold);
        }

        /// <summary>
        /// The second save takes the File.Replace branch — the one the old
        /// delete-then-move code got wrong, and the one that actually runs in a real
        /// session, since the first save only happens once ever.
        /// </summary>
        [Test]
        public void Save_OverExistingFile_ReplacesItWithTheNewState()
        {
            _service.Save(StateWithGold(100));
            _service.Save(StateWithGold(200));

            Assert.AreEqual(200, _service.Load().Ledger.Gold);
        }

        [Test]
        public void Save_Repeatedly_LeavesNoTempFileBehind()
        {
            for (int i = 0; i < 3; i++) _service.Save(StateWithGold(i));

            var strays = Directory.GetFiles(_dir, "*.tmp");
            CollectionAssert.IsEmpty(strays, "A leftover .tmp means a swap did not complete.");
        }

        [Test]
        public void Save_LeavesExactlyOneSaveFile()
        {
            _service.Save(StateWithGold(1));
            _service.Save(StateWithGold(2));

            Assert.AreEqual(1, Directory.GetFiles(_dir).Length);
            Assert.IsTrue(File.Exists(_service.SavePath));
        }

        [Test]
        public void Load_WithCorruptJson_DiscardsTheFileAndReturnsNull()
        {
            File.WriteAllText(_service.SavePath, "{ this is not valid json ");

            Assert.IsNull(_service.Load(), "An unreadable save must not surface as a broken state.");
            Assert.IsFalse(File.Exists(_service.SavePath),
                "The corrupt file must be removed so the next save starts clean (ARCHITECTURE.md §5).");
        }

        [Test]
        public void Load_WithTruncatedJson_DiscardsTheFileAndReturnsNull()
        {
            // What a crash mid-write used to be able to leave behind.
            _service.Save(StateWithGold(500));
            var full = File.ReadAllText(_service.SavePath);
            File.WriteAllText(_service.SavePath, full.Substring(0, full.Length / 2));

            Assert.IsNull(_service.Load());
            Assert.IsFalse(File.Exists(_service.SavePath));
        }

        [Test]
        public void Load_AfterCorruptFileDiscarded_CanSaveAndLoadAgain()
        {
            File.WriteAllText(_service.SavePath, "garbage");
            _service.Load(); // discards it

            Assert.IsTrue(_service.Save(StateWithGold(7)));
            Assert.AreEqual(7, _service.Load().Ledger.Gold);
        }

        [Test]
        public void Delete_RemovesTheSaveFile()
        {
            _service.Save(StateWithGold(1));
            _service.Delete();

            Assert.IsFalse(File.Exists(_service.SavePath));
            Assert.IsNull(_service.Load());
        }

        [Test]
        public void Delete_WithNoSaveFile_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.Delete());
        }

        [Test]
        public void Save_WhenTheTargetDirectoryIsGone_FailsWithoutThrowing()
        {
            Directory.Delete(_dir, true);

            // The failure is expected to be logged loudly — declare it so the test runner
            // does not treat the LogError as an unhandled message.
            LogAssert.Expect(LogType.Error, new Regex(@"\[LocalJsonSaveService\] Save failed"));

            bool result = true;
            Assert.DoesNotThrow(() => result = _service.Save(StateWithGold(1)),
                "Save runs at shutdown and focus-loss; throwing there has nowhere to go.");
            Assert.IsFalse(result, "A failed save must report failure rather than claim success.");
        }
    }
}
