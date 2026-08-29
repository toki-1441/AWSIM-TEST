using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Planning;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class ReferencePlanningPolicyTests
    {
        [Test]
        public void Choose_UsesLookAheadOffsetAndMostConservativeSpeed()
        {
            var waypoints = new[]
            {
                new RelativeCourseWaypoint(Vector3.zero, 0f, 20f),
                new RelativeCourseWaypoint(new Vector3(1f, 0f, 5f), 0.1f, 8f)
            };

            var command = ReferencePlanningPolicy.Choose(waypoints, 10f, 2f);

            Assert.That(command.DesiredSpeed, Is.EqualTo(8f));
            Assert.That(command.DesiredLateralOffsetNormalized, Is.EqualTo(0.5f));
        }
    }
}
