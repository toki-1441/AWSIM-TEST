using NUnit.Framework;
using PhysicalAi.Planning;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class PlanningDemonstrationRecorderTests
    {
        [Test]
        public void JsonFormat_PreservesObservationActionAndTerminalState()
        {
            var frame = new PlanningDemonstrationFrame
            {
                EpisodeId = 3,
                Step = 8,
                Observation = new[] { 1f, 2f },
                Command = new PlanningCommand { DesiredSpeed = 4f, DesiredLateralOffsetNormalized = -0.5f },
                Reward = 1.5f,
                ScenarioLabel = "human-head-to-head-overtake",
                Terminal = true
            };

            var loaded = JsonUtility.FromJson<PlanningDemonstrationFrame>(JsonUtility.ToJson(frame));

            Assert.That(loaded.EpisodeId, Is.EqualTo(3));
            Assert.That(loaded.Observation, Is.EqualTo(new[] { 1f, 2f }));
            Assert.That(loaded.Command.DesiredSpeed, Is.EqualTo(4f));
            Assert.That(loaded.ScenarioLabel, Is.EqualTo("human-head-to-head-overtake"));
            Assert.That(loaded.Terminal, Is.True);
        }
    }
}
