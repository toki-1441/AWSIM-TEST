// Copyright 2026
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using UnityEngine;
namespace PhysicalAi.Calibration
{
    public sealed class TelemetryCalibrationReportWriter : MonoBehaviour
    {
        [SerializeField] TelemetryCalibrationApplier _applier;
        [SerializeField] string _fileName = "physical-ai-calibration-report.json";
        [Serializable] sealed class Report { public float frictionCoefficient; public int sampleCount; public float lateralAccelerationRmse; }
        void OnEnable() { if (_applier != null) _applier.CalibrationApplied += Write; }
        void OnDisable() { if (_applier != null) _applier.CalibrationApplied -= Write; }
        public void Write(FrictionCalibrationResult result)
        {
            var directory = Path.Combine(Application.persistentDataPath, "PhysicalAi");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, _fileName), Serialize(result));
        }
        public static string Serialize(FrictionCalibrationResult result) => JsonUtility.ToJson(new Report { frictionCoefficient = result.FrictionCoefficient, sampleCount = result.SampleCount, lateralAccelerationRmse = result.LateralAccelerationRmse }, true);
    }
}
