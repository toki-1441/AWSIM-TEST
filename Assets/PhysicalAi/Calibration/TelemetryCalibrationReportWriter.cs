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
        [Serializable] sealed class Report
        {
            public float frictionCoefficient;
            public string runId;
            public int rawSampleCount;
            public int sampleCount;
            public int rejectedLowAccelerationSampleCount;
            public float lateralAccelerationRmse;
        }
        void OnEnable() { if (_applier != null) _applier.CalibrationApplied += Write; }
        void OnDisable() { if (_applier != null) _applier.CalibrationApplied -= Write; }
        public void Write(FrictionCalibrationResult result)
        {
            var directory = Path.Combine(Application.persistentDataPath, "PhysicalAi");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, _fileName), Serialize(result, _applier.LastRunId, _applier.LastRawSampleCount));
        }
        public static string Serialize(FrictionCalibrationResult result, string runId = null, int rawSampleCount = 0) => JsonUtility.ToJson(new Report
        {
            frictionCoefficient = result.FrictionCoefficient,
            runId = runId,
            rawSampleCount = rawSampleCount,
            sampleCount = result.SampleCount,
            rejectedLowAccelerationSampleCount = result.RejectedLowAccelerationSampleCount,
            lateralAccelerationRmse = result.LateralAccelerationRmse
        }, true);
    }
}
