// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using Awsim.Common;
using PhysicalAi.Planning;
using PhysicalAi.VehicleDynamics;
using ROS2;
using UnityEngine;

namespace PhysicalAi.Ros2
{
    /// <summary>Autoware Control topic subscriber for the custom combined-slip vehicle.</summary>
    public sealed class CombinedSlipVehicleRos2Input : MonoBehaviour
    {
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField] string _controlTopic = "/control/command/control_cmd";
        [SerializeField, Min(0.01f)] float _maximumAcceleration = 3f;
        [SerializeField, Min(0.01f)] float _maximumSteeringDegrees = 35f;
        [SerializeField] QosSettings _qosSettings = new QosSettings(
            ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
            DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL,
            HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
            1);

        ISubscription<autoware_control_msgs.msg.Control> _subscription;
        DirectVehicleActuation _actuation;
        bool _hasReceivedCommand;

        void Start()
        {
            if (!AwsimRos2Node.IsInitialized)
                AwsimRos2Node.Initialize();
            Initialize();
        }

        /// <summary>Call after AwsimRos2Node.Initialize().</summary>
        public void Initialize()
        {
            if (_subscription != null)
                return;
            _subscription = AwsimRos2Node.CreateSubscription<autoware_control_msgs.msg.Control>(_controlTopic, message =>
            {
                _actuation = Ros2ControlMapping.FromAccelerationAndSteering(
                    message.Longitudinal.Acceleration,
                    message.Lateral.Steering_tire_angle,
                    _maximumAcceleration,
                    _maximumSteeringDegrees);
                _hasReceivedCommand = true;
            }, _qosSettings.GetQosProfile());
        }

        void FixedUpdate()
        {
            if (_vehicle == null || !_hasReceivedCommand)
                return;
            _vehicle.Throttle = _actuation.Throttle;
            _vehicle.Brake = _actuation.Brake;
            _vehicle.Steering = _actuation.Steering;
        }

        void OnDestroy()
        {
            if (_subscription != null)
                AwsimRos2Node.RemoveSubscription<autoware_control_msgs.msg.Control>(_subscription);
        }
    }
}
