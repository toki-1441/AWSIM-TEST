using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PhysicalAi.Configuration;
using PhysicalAi.Course;
using PhysicalAi.Evaluation;
using PhysicalAi.Sensors;
using PhysicalAi.Randomization;
using PhysicalAi.Planning;
using PhysicalAi.VehicleDynamics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PhysicalAi.Tests.EditMode
{
    public class OperatorWorkflowTests
    {
        [Test]
        public void HighLevelCliSelectionsRegenerateConfigDerivedSwitches()
        {
            var path = Path.Combine(_directory, "rl-config.json");
            new SimulatorLaunchSettings { mode = "rl", scenario = "head-to-head", mapMode = "fixed", mapPath = "saved-map.json" }.Save(path);
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiConfig", path, "-physicalAiMode", "manual",
                "-physicalAiDemonstrationScenario", "nominal", "-physicalAiMapMode", "endless" });
            Assert.That(PhysicalAiLaunchArguments.HasFlag("-physicalAiRlPort"), Is.False);
            Assert.That(PhysicalAiLaunchArguments.HasFlag("-physicalAiRlManaged"), Is.False);
            Assert.That(PhysicalAiLaunchArguments.HasFlag("-physicalAiEvaluation"), Is.False);
            Assert.That(PhysicalAiLaunchArguments.HasFlag("-physicalAiHeadToHead"), Is.False);
            Assert.That(PhysicalAiLaunchArguments.GetFloat("-physicalAiHazardProbability", 1), Is.Zero);
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiMapPath"), Is.Null);
            SimulatorLaunchSettings.ValidateCurrentArguments();
        }

        [Test]
        public void StandaloneModeSelectionIncludesTheSameDefaultsAsGui()
        {
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiMode", "rl" });
            var expected = new SimulatorLaunchSettings { mode = "rl" };
            Assert.That(SimulatorLaunchSettings.ValidateCurrentArguments().ToArguments(), Is.EqualTo(expected.ToArguments()));
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiRlManaged"), Is.EqualTo("true"));
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiEvaluation"), Is.EqualTo("rollout"));
        }

        [TestCase("-physicalAiCameraWidth", "15")]
        [TestCase("-physicalAiCameraHz", "61")]
        [TestCase("-physicalAiBevSize", "513")]
        [TestCase("-physicalAiSeed", "-1")]
        [TestCase("-physicalAiVehicleModel", "unknown")]
        [TestCase("-physicalAiDepth", "yes")]
        [TestCase("-physicalAiRecordOnStart", "yes")]
        public void PrelaunchValidationRejectsInvalidRawOverrides(string option, string value)
        {
            PhysicalAiLaunchArguments.Override(new[] { option, value });
            Assert.Throws<ArgumentException>(() => SimulatorLaunchSettings.ValidateCurrentArguments());
        }

        [Test]
        public void PrelaunchValidationUsesResolvedConfigValuesAndChecksCombinations()
        {
            var settings = new SimulatorLaunchSettings { mode = "rl", depth = true, bev = true, cameraHeightMeters = .22f };
            PhysicalAiLaunchArguments.Override(settings.ToArguments());
            Assert.That(SimulatorLaunchSettings.ValidateCurrentArguments().ToArguments(), Is.EqualTo(settings.ToArguments()));
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiMapMode", "endless", "-physicalAiMapPath", "map.json" });
            Assert.Throws<ArgumentException>(() => SimulatorLaunchSettings.ValidateCurrentArguments());
        }

        [Test]
        public void ExplicitFalseOverridesConfigurationSwitchAndLastSwitchWins()
        {
            var path = Path.Combine(_directory, "record-enabled.json");
            new SimulatorLaunchSettings { recordOnStart = true }.Save(path);
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiConfig", path, "-physicalAiRecordOnStart", "false" });
            Assert.That(PhysicalAiLaunchArguments.HasFlag("-physicalAiRecordOnStart"), Is.False);
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiRecordOnStart", "false", "-physicalAiRecordOnStart" });
            Assert.That(PhysicalAiLaunchArguments.HasFlag("-physicalAiRecordOnStart"), Is.True);
        }

        [Test]
        public void ExplicitInvalidNumericArgumentsNeverSilentlyUseDefaults()
        {
            foreach (var value in new[] { "bad", "NaN", "Infinity", "1e100" })
            {
                PhysicalAiLaunchArguments.Override(new[] { "-physicalAiCameraHeightMeters", value });
                Assert.Throws<ArgumentException>(() => PhysicalAiLaunchArguments.GetFloat("-physicalAiCameraHeightMeters", .15f));
            }
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiSeed", "1.5" });
            Assert.Throws<ArgumentException>(() => PhysicalAiLaunchArguments.GetInt("-physicalAiSeed", 1));
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiSeed", "7", "-physicalAiSeed" });
            Assert.Throws<ArgumentException>(() => PhysicalAiLaunchArguments.GetInt("-physicalAiSeed", 1));
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiCameraPitchDegrees", "-22" });
            Assert.That(PhysicalAiLaunchArguments.GetFloat("-physicalAiCameraPitchDegrees", 1), Is.EqualTo(-22));
            Assert.That(PhysicalAiLaunchArguments.GetInt("-missing", 42), Is.EqualTo(42));
        }

        [TestCase("OperatorEventTag", true)]
        [TestCase("OperatorMapPath", true)]
        [TestCase("Start", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlyOperatorTextFieldsSuppressDrivingHotkeys(string control, bool expected)
            => Assert.That(OperatorSessionHud.IsTextInputControl(control), Is.EqualTo(expected));

        [Test]
        public void NumericInputPreservesDecimalEditingAndRejectsUnfinishedValues()
        {
            var buffer = new NumericInputBuffer();
            var value = 0.15f;
            foreach (var text in new[] { "", "0", "0.", "0.2", "0.25" })
            {
                buffer.BeginDraw();
                value = buffer.Float("Height", text, value);
                Assert.That(buffer.Text("Height", value), Is.EqualTo(text));
                if (text == "") Assert.Throws<ArgumentException>(() => buffer.Validate());
                else buffer.Validate();
            }
            Assert.That(value, Is.EqualTo(0.25f));
            foreach (var invalid in new[] { "-", "1e", "NaN", "Infinity", "1e100", "0,2" })
            {
                buffer.BeginDraw();
                Assert.That(buffer.Float("Height", invalid, value), Is.EqualTo(value));
                Assert.Throws<ArgumentException>(() => buffer.Validate());
            }
            buffer.Clear(); // Loading another config must replace text as well as parsed values.
            Assert.That(buffer.Text("Height", 0.4f), Is.EqualTo("0.4"));
            buffer.Validate();
        }

        [Test]
        public void NumericIntegerInputRetainsEmptyTextButCannotSubmitStaleValue()
        {
            var buffer = new NumericInputBuffer();
            Assert.That(buffer.Integer("Seed", "", 7), Is.EqualTo(7));
            Assert.That(buffer.Text("Seed", 7), Is.Empty);
            Assert.Throws<ArgumentException>(() => buffer.Validate());
            Assert.That(buffer.Integer("Seed", "42", 7), Is.EqualTo(42));
            buffer.Validate();
            buffer.Integer("Port", "invalid", 5177);
            buffer.BeginDraw(); // Hidden mode-specific inputs do not block the current mode.
            buffer.Integer("Seed", "42", 42);
            buffer.Validate();
        }

        string _directory;
        GameObject _root;
        static void InvokeAwake(MonoBehaviour component) => component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
        [SetUp] public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "operator-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }
        [TearDown] public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            PhysicalAiLaunchArguments.ClearOverride();
            Directory.Delete(_directory, true); // Only this fixture's UUID-created directory.
        }

        [Test]
        public void SavedConfigAndCliResolveTheSameSettingsWithExplicitOverrides()
        {
            var settings = new SimulatorLaunchSettings { mapMode = "fixed", seed = 12, outputDirectory = Path.Combine(_directory, "data with spaces"),
                recordOnStart = true, vehicleModel = "dynamic", imageWidth = 640 };
            var path = Path.Combine(_directory, "run.json");
            settings.Save(path);
            Assert.That(SimulatorLaunchSettings.Load(path).ToArguments(), Is.EqualTo(settings.ToArguments()));
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiConfig", path, "-physicalAiSeed", "43" });
            Assert.That(PhysicalAiLaunchArguments.GetInt("-physicalAiSeed", 0), Is.EqualTo(43));
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiMapMode"), Is.EqualTo("fixed"));
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiDemonstrationDirectory"), Is.EqualTo(settings.outputDirectory));
            Assert.That(PhysicalAiLaunchArguments.GetInt("-physicalAiCameraWidth", 0), Is.EqualTo(640));
        }
        [Test]
        public void InvalidConfigCannotSilentlyLaunchSomethingElse()
        {
            Assert.Throws<ArgumentException>(() => new SimulatorLaunchSettings { mapMode = "infinite-typo" }.ToArguments());
            Assert.Throws<ArgumentException>(() => new SimulatorLaunchSettings { mapPath = "a.json" }.Validate());
            Assert.Throws<ArgumentException>(() => new SimulatorLaunchSettings { imageWidth = 0 }.Validate());
            Assert.Throws<ArgumentException>(() => RuntimeArguments.Expand(new[] { "player", "-physicalAiConfig" }));
            Assert.Throws<ArgumentException>(() => new SimulatorLaunchSettings { cameraHeightMeters = -0.1f }.Validate());
        }
        [Test]
        public void ConfiguredCameraPoseRemainsFixedAcrossEpisodeSeedsAndLegacyRandomization()
        {
            var settings = new SimulatorLaunchSettings { cameraHeightMeters = 0.22f, cameraForwardMeters = 0.12f,
                cameraLateralMeters = 0.01f, cameraPitchDegrees = 18, cameraYawDegrees = 4, cameraRollDegrees = 2 };
            var path = Path.Combine(_directory, "camera.json");
            settings.Save(path);
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiConfig", path });
            _root = new GameObject("FixedCameraVehicle");
            _root.transform.position = new Vector3(0, 0.1f, 0);
            var camera = new GameObject("FixedCamera");
            camera.transform.SetParent(_root.transform, false);
            var mount = camera.AddComponent<SensorMount>();
            mount.Configure(SensorModality.Camera, "camera_front", Vector3.zero, Quaternion.identity);
            mount.ApplyLaunchConfiguration();
            var expectedPosition = new Vector3(0.01f, 0.12f, 0.12f);
            var expectedRotation = Quaternion.Euler(18, 4, 2);
            var randomizer = _root.AddComponent<DomainRandomizer>();
            typeof(DomainRandomizer).GetField("_sensorMounts", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(randomizer, new[] { camera.transform });
            var legacy = DomainRandomizationSettings.Default;
            legacy.MaximumMountPositionOffset = 0.03f; legacy.MaximumMountRotationDegrees = 3;
            typeof(DomainRandomizer).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(randomizer, legacy);
            InvokeAwake(randomizer);
            for (var seed = 0; seed < 100; seed++)
            {
                randomizer.ApplyEpisodeSeed(seed);
                Assert.That(Vector3.Distance(camera.transform.localPosition, expectedPosition), Is.LessThan(1e-6));
                Assert.That(Quaternion.Angle(camera.transform.localRotation, expectedRotation), Is.LessThan(0.001));
            }
        }
        [Test]
        public void GuiRlSelectionReachesTheRuntimeAndDisablesTheKeyboard()
        {
            PhysicalAiLaunchArguments.Override(new SimulatorLaunchSettings { mode = "rl", rlPort = 5061 }.ToArguments());
            Assert.That(PhysicalAiLaunchArguments.GetInt("-physicalAiRlPort", 0), Is.EqualTo(5061));
            Assert.That(KeyboardTeleoperationPlanner.ShouldActivateAtStart(true, false, PhysicalAiLaunchArguments.Current), Is.False);
        }
        [Test]
        public void ManagedTrainingOptionsRoundTripAndKeepResumeSeparateFromOutput()
        {
            var settings = new SimulatorLaunchSettings { mode = "rl", rlPython = "/tmp/env with spaces/python",
                rlResumeCheckpoint = "/tmp/previous run/checkpoint.pt", rlOutputDirectory = "/tmp/new runs", rlAutoStart = false };
            PhysicalAiLaunchArguments.Override(settings.ToArguments());
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiRlManaged"), Is.EqualTo("true"));
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiRlAutoStart"), Is.EqualTo("false"));
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiRlResume"), Is.EqualTo(settings.rlResumeCheckpoint));
            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiRlOutputDirectory"), Is.EqualTo(settings.rlOutputDirectory));
            Assert.Throws<ArgumentException>(() => new SimulatorLaunchSettings { mode = "rl", rlDevice = "not-a-device" }.Validate());
        }
        [Test]
        public void FixedGeometryStaysFixedWhileEpisodeRandomizationSeedChanges()
        {
            PhysicalAiLaunchArguments.Override(new SimulatorLaunchSettings { mode = "rl", mapMode = "fixed", seed = 7 }.ToArguments());
            _root = new GameObject("FixedRlMap");
            var episode = _root.AddComponent<PhysicalAiEpisodeOrchestrator>();
            InvokeAwake(episode);
            episode.ResetEpisode(7);
            var hash = episode.MapHash;
            episode.ResetEpisode(8);
            Assert.That(episode.MapHash, Is.EqualTo(hash));
            Assert.That(episode.EpisodeSeed, Is.EqualTo(8));
            Assert.That(episode.ActivePolicyLabel, Is.EqualTo("rl-disconnected"));
        }
        [Test]
        public void OperatorRlResumeContinuesTheSameSeedSequenceWithoutTrainingOnEvaluationSeeds()
        {
            var all = HoldoutCourseSuite.OperatorRolloutSeeds(100, 0, 5);
            var resumed = HoldoutCourseSuite.OperatorRolloutSeeds(100, 2, 3);
            Assert.That(all[0], Is.EqualTo(100));
            Assert.That(all.Contains(101), Is.False);
            Assert.That(resumed, Is.EqualTo(all.Skip(2).ToArray()));
            Assert.Throws<ArgumentException>(() => HoldoutCourseSuite.OperatorRolloutSeeds(1009, 0, 1));
        }
        [Test]
        public void TrainerProcessArgumentsPreserveSpacesQuotesAndShellMetacharactersLiterally()
        {
            if (!File.Exists("/usr/bin/python3")) Assert.Ignore("Linux Python process parser check");
            var literal = "spaces and \"quotes\" and \\backslash ; $HOME";
            var args = OperatorRlSession.QuoteArguments(new[] { "-c", "import sys; print(sys.argv[1], end='')", literal });
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = "/usr/bin/python3", Arguments = args, UseShellExecute = false, RedirectStandardOutput = true });
            Assert.That(process.StandardOutput.ReadToEnd(), Is.EqualTo(literal));
            Assert.That(process.WaitForExit(2000), Is.True);
            Assert.That(process.ExitCode, Is.Zero);
        }
        [TestCase("fixed")]
        [TestCase("endless")]
        public void ConfiguredMapModeAndSeedAreActuallyApplied(string mode)
        {
            PhysicalAiLaunchArguments.Override(new SimulatorLaunchSettings { mapMode = mode, seed = 17 }.ToArguments());
            _root = new GameObject("ConfiguredMap");
            var episode = _root.AddComponent<PhysicalAiEpisodeOrchestrator>();
            InvokeAwake(episode);
            episode.ResetEpisode(episode.EpisodeSeed);
            Assert.That(episode.MapMode, Is.EqualTo(mode));
            Assert.That(episode.EpisodeSeed, Is.EqualTo(17));
            Assert.That(episode.Course.IsClosedLoop, Is.EqualTo(mode == "fixed"));
            Assert.That(episode.Course.Centerline.Count, Is.GreaterThan(10));
            Assert.That(episode.MapHash, Has.Length.EqualTo(64));
        }
        [Test]
        public void FixedGeometryRoundTripsWithoutSeedRegenerationOrInfinityInJson()
        {
            var origin = new Vector3(4, 0, 6);
            var course = ClosedHoldoutCourseGenerator.Generate(7, CourseGenerationSettings.Default, origin, 30);
            var map = CourseMapDocument.Capture(course, origin, 30, 0.6f);
            var path = Path.Combine(_directory, "map.json");
            map.SaveNew(path);
            Assert.That(File.ReadAllText(path), Does.Not.Contain("Infinity"));
            Assert.Throws<IOException>(() => map.SaveNew(path));
            var loaded = CourseMapDocument.Load(path);
            Assert.That(loaded.ContentHash, Is.EqualTo(map.ContentHash));
            var restored = loaded.Instantiate(origin, 30);
            Assert.That(restored.Centerline.Count, Is.EqualTo(course.Centerline.Count));
            for (var i = 0; i < course.Centerline.Count; i++)
            {
                Assert.That(Vector3.Distance(restored.Centerline[i], course.Centerline[i]), Is.LessThan(0.0001f));
                Assert.That(restored.MaximumSafeSpeed[i], Is.EqualTo(course.MaximumSafeSpeed[i]));
            }
        }
        [Test]
        public void ConfiguredMapFileLoadsInsteadOfGeneratingFromTheRequestedSeed()
        {
            var course = ClosedHoldoutCourseGenerator.Generate(2, CourseGenerationSettings.Default, Vector3.zero, 0);
            var path = Path.Combine(_directory, "map.json");
            CourseMapDocument.Capture(course, Vector3.zero, 0, 0.6f).SaveNew(path);
            PhysicalAiLaunchArguments.Override(new SimulatorLaunchSettings { mapMode = "fixed", mapPath = path, seed = 999 }.ToArguments());
            _root = new GameObject("LoadedMap");
            var episode = _root.AddComponent<PhysicalAiEpisodeOrchestrator>();
            InvokeAwake(episode);
            episode.ResetEpisode(999);
            Assert.That(episode.Course.Centerline, Is.EqualTo(course.Centerline));
        }

        [Test]
        public void OffsetSnapshotLoadsUnchangedAndRespawnsOnItsFirstRetainedRoad()
        {
            var map = new CourseMapDocument { samples = new[] {
                new CourseMapDocument.Sample { position = new Vector3(120, 0, -35), maximumSafeSpeed = 3 },
                new CourseMapDocument.Sample { position = new Vector3(125, 0, -35), maximumSafeSpeed = 3 },
                new CourseMapDocument.Sample { position = new Vector3(130, 0, -35), maximumSafeSpeed = 3 }
            }};
            var path = Path.Combine(_directory, "trimmed-map.json");
            map.SaveNew(path);
            PhysicalAiLaunchArguments.Override(new SimulatorLaunchSettings { mapMode = "fixed", mapPath = path }.ToArguments());
            _root = new GameObject("OffsetMapVehicle");
            _root.transform.SetPositionAndRotation(new Vector3(4, .11f, 8), Quaternion.Euler(0, 30, 0));
            var vehicle = _root.AddComponent<CombinedSlipVehicle>();
            vehicle.Configure(VehicleSpecification.Default);
            var episode = _root.AddComponent<PhysicalAiEpisodeOrchestrator>();
            typeof(PhysicalAiEpisodeOrchestrator).GetField("_vehicle", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(episode, vehicle);
            InvokeAwake(episode);
            var body = vehicle.GetComponent<Rigidbody>();
            foreach (var seed in new[] { 7, 8 })
            {
                episode.ResetEpisode(seed);
                Assert.That(Vector3.Distance(body.position, new Vector3(120, .11f, -35)), Is.LessThan(.00001f));
                Assert.That(Vector3.Distance(body.rotation * Vector3.forward, Vector3.right), Is.LessThan(.00001f));
                Assert.That(episode.Course.Centerline, Is.EqualTo(map.samples.Select(s => s.position).ToArray()));
                Assert.That(episode.CaptureMap().ContentHash, Is.EqualTo(map.ContentHash));
                body.position = new Vector3(-500, .11f, 200);
                body.rotation = Quaternion.identity;
            }
        }
        [Test]
        public void RecordingStartsMidEpisodeAndAssistedDataKeepsModelActionsAndTags()
        {
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiDemonstrationDirectory", _directory });
            _root = new GameObject("Recorder");
            var vehicle = _root.AddComponent<CombinedSlipVehicle>();
            vehicle.Configure(VehicleSpecification.Default);
            vehicle.NoSlipAssistance = true;
            vehicle.Throttle = 0.7f; vehicle.Steering = -0.2f;
            var recorder = _root.AddComponent<PlanningDemonstrationRecorder>();
            InvokeAwake(recorder);
            recorder.BeginEpisode(21);
            Assert.That(recorder.CurrentPath, Is.Null);
            recorder.SetRecordingEnabled(true);
            var first = recorder.CurrentPath;
            Assert.That(File.Exists(first), Is.True);
            recorder.Record(new PlanningObservation(new[] { 1f, 2f }), default, 0.5f);
            recorder.AddAnnotation("near miss");
            recorder.SetRecordingEnabled(false);
            var frame = JsonUtility.FromJson<PlanningDemonstrationFrame>(File.ReadAllLines(first)[0]);
            Assert.That(frame.VehicleModel, Is.EqualTo("planar-no-slip-assist"));
            Assert.That(frame.DirectActuation, Is.EqualTo(new Vector3(0.7f, 0, -0.2f)));
            Assert.That(frame.CameraFrameIndex, Is.EqualTo(-1));
            var manifest = JsonUtility.FromJson<DemonstrationSessionManifest>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(first), "manifest.json")));
            Assert.That(manifest.vehicleModel, Is.EqualTo(frame.VehicleModel));
            Assert.That(manifest.seed, Is.EqualTo(21));
            Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(first), "events.jsonl")), Does.Contain("near miss"));
            recorder.SetRecordingEnabled(true);
            Assert.That(recorder.CurrentPath, Is.Not.EqualTo(first));
            recorder.Record(new PlanningObservation(new[] { 3f }), default, 1);
            recorder.EndEpisode(new PlanningObservation(Array.Empty<float>()), default, -1);
            var last = JsonUtility.FromJson<PlanningDemonstrationFrame>(File.ReadAllLines(recorder.CurrentPath).Last());
            Assert.That(last.Terminal, Is.True);
            Assert.That(File.Exists(first), Is.True);
        }
        [Test]
        public void RecordOnStartWaitsForEpisodeThenResetStartsANewSegment()
        {
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiDemonstrationDirectory", _directory, "-physicalAiRecordOnStart" });
            _root = new GameObject("Recorder");
            var recorder = _root.AddComponent<PlanningDemonstrationRecorder>();
            InvokeAwake(recorder);
            Assert.That(recorder.CurrentPath, Is.Null);
            recorder.BeginEpisode(1);
            var first = recorder.CurrentPath;
            recorder.BeginEpisode(2);
            Assert.That(recorder.CurrentPath, Is.Not.EqualTo(first));
            Assert.That(recorder.LastError, Is.Null);
        }
    }
}
