using NUnit.Framework;
using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class PhysicsConstrainedCourseTests
    {
        [Test]
        public void Generate_AllCornerSamplesAreTraversableAtDesignSpeed()
        {
            var settings = CourseGenerationSettings.Default;
            var course = PhysicsConstrainedCourseGenerator.Generate(42, settings, Vector3.zero, 0f);

            for (var i = 0; i < course.Centerline.Count; i++)
                Assert.That(CoursePhysicsConstraints.IsTraversable(settings.DesignSpeed, course.Curvature[i], settings.FrictionCoefficient, settings.MaximumBankDegrees), Is.True);
        }

        [Test]
        public void Encode_TranslationOfCourseAndEgoDoesNotChangeObservation()
        {
            var course = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var baseObservation = RelativeCourseObservation.Encode(course, Vector3.zero, Quaternion.identity, 0, 4);
            var translation = new Vector3(100f, 0f, -50f);
            var translatedCourse = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, translation, 0f);
            var translatedObservation = RelativeCourseObservation.Encode(translatedCourse, translation, Quaternion.identity, 0, 4);

            for (var i = 0; i < baseObservation.Length; i++)
            {
                Assert.That(Vector3.Distance(translatedObservation[i].LocalPosition, baseObservation[i].LocalPosition), Is.LessThan(0.0001f));
                Assert.That(translatedObservation[i].Curvature, Is.EqualTo(baseObservation[i].Curvature));
            }
        }
    }
}
