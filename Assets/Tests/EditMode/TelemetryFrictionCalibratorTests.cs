using System.Collections.Generic;
using NUnit.Framework;
using PhysicalAi.Calibration;

namespace PhysicalAi.Tests.EditMode
{
    public class TelemetryFrictionCalibratorTests
    {
        [Test]
        public void Estimate_SteadyStateTelemetry_RecoversFrictionCoefficient()
        {
            const float friction = 0.8f;
            const float gravity = 9.80665f;
            var samples = new List<CorneringTelemetrySample>
            {
                new CorneringTelemetrySample { Speed = 10f, YawRateRadiansPerSecond = friction * gravity / 10f },
                new CorneringTelemetrySample { Speed = 8f, YawRateRadiansPerSecond = friction * gravity / 8f },
                new CorneringTelemetrySample { Speed = 12f, YawRateRadiansPerSecond = friction * gravity / 12f }
            };

            var result = TelemetryFrictionCalibrator.Estimate(samples);

            Assert.That(result.FrictionCoefficient, Is.EqualTo(friction).Within(0.001f));
            Assert.That(result.SampleCount, Is.EqualTo(3));
            Assert.That(result.LateralAccelerationRmse, Is.LessThan(0.001f));
        }
    }
}
