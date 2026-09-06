using System;
using System.Linq;
using NUnit.Framework;
using PhysicalAi.Planning;

namespace PhysicalAi.Tests.EditMode
{
    public class PhysicalAiModeCatalogTests
    {
        [TearDown]
        public void ClearLaunchOverride() => PhysicalAiLaunchArguments.ClearOverride();

        [Test]
        public void Catalog_OffersManualDrivingWithEveryDemonstrationScenario()
        {
            var manual = PhysicalAiModeCatalog.Find("manual-drive");

            Assert.That(manual, Is.Not.Null);
            Assert.That(manual.Variants.Select(variant => variant.Id),
                Is.EquivalentTo(new[] { "nominal", "blind-corner", "head-to-head" }));
        }

        [Test]
        public void Resolve_ConcatenatesModeAndVariantArguments()
        {
            var evaluation = PhysicalAiModeCatalog.Find("evaluation");
            var holdout = evaluation.Variants.Single(variant => variant.Id == "holdout");

            Assert.That(PhysicalAiModeCatalog.Resolve(evaluation, holdout),
                Is.EqualTo(new[] { "-physicalAiEvaluation", "holdout" }));
        }

        [Test]
        public void Resolve_RejectsAMissingOrForeignVariant()
        {
            var evaluation = PhysicalAiModeCatalog.Find("evaluation");
            var manualVariant = PhysicalAiModeCatalog.Find("manual-drive").Variants[0];

            Assert.Throws<ArgumentException>(() => PhysicalAiModeCatalog.Resolve(evaluation, null));
            Assert.Throws<ArgumentException>(() => PhysicalAiModeCatalog.Resolve(evaluation, manualVariant));
        }

        [Test]
        public void DescribeCommandLine_ReproducesTheSelection()
        {
            var rollout = PhysicalAiModeCatalog.Find("rl-rollout");

            Assert.That(PhysicalAiModeCatalog.DescribeCommandLine(rollout, null),
                Is.EqualTo("Build/AWSIM.x86_64 -physicalAiEvaluation rollout"));
        }

        [Test]
        public void CommandLineSelectsAMode_OnlyForARecognisedSuiteOrTheDemo()
        {
            Assert.That(PhysicalAiModeCatalog.CommandLineSelectsAMode(
                new[] { "player", "-physicalAiEvaluation", "holdout" }), Is.True);
            Assert.That(PhysicalAiModeCatalog.CommandLineSelectsAMode(
                new[] { "player", "-physicalAiAutowareDemo" }), Is.True);
            // A scripted run configures itself and must not be interrupted by a menu,
            // even when its arguments name no mode.
            Assert.That(PhysicalAiModeCatalog.CommandLineSelectsAMode(
                new[] { "player", "-physicalAiHazardProbability", "0" }), Is.True);
            Assert.That(PhysicalAiModeCatalog.CommandLineSelectsAMode(
                new[] { "player", "-physicalAiRecordDynamicsTelemetry" }), Is.True);
            // Unrelated engine arguments are not a Physical AI decision.
            Assert.That(PhysicalAiModeCatalog.CommandLineSelectsAMode(
                new[] { "player", "-screen-fullscreen", "0" }), Is.False);
            Assert.That(PhysicalAiModeCatalog.CommandLineSelectsAMode(Array.Empty<string>()), Is.False);
        }

        [Test]
        public void LaunchArguments_PreferAnOverrideAndRestoreTheCommandLine()
        {
            var manual = PhysicalAiModeCatalog.Find("manual-drive");
            var blindCorner = manual.Variants.Single(variant => variant.Id == "blind-corner");

            PhysicalAiLaunchArguments.Override(PhysicalAiModeCatalog.Resolve(manual, blindCorner));

            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiDemonstrationScenario"),
                Is.EqualTo("blind-corner"));
            Assert.That(PhysicalAiLaunchArguments.HasFlag("-physicalAiDemonstrationScenario"), Is.True);

            PhysicalAiLaunchArguments.ClearOverride();

            Assert.That(PhysicalAiLaunchArguments.GetValue("-physicalAiDemonstrationScenario"), Is.Null);
        }

        [Test]
        public void LaunchArguments_ParseTypedValuesWithFallbacks()
        {
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiEpisodeCount", "12", "-physicalAiHazardProbability", "0.25" });

            Assert.That(PhysicalAiLaunchArguments.GetInt("-physicalAiEpisodeCount", 8), Is.EqualTo(12));
            Assert.That(PhysicalAiLaunchArguments.GetFloat("-physicalAiHazardProbability", 1f), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(PhysicalAiLaunchArguments.GetInt("-physicalAiMissing", 7), Is.EqualTo(7));
        }
    }
}
