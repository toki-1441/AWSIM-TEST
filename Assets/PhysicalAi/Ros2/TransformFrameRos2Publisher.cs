// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using Awsim.Common;
using PhysicalAi.Sensors;
using ROS2;
using UnityEngine;

namespace PhysicalAi.Ros2
{
    /// <summary>Publishes the simulator's odom-to-base-link edge to the ROS2 TF tree.</summary>
    public sealed class TransformFrameRos2Publisher : MonoBehaviour
    {
        [SerializeField] TransformFrameSensor _sensor;
        [SerializeField] string _topic = "/tf";
        [SerializeField] QosSettings _qosSettings = new QosSettings(
            ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
            DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE,
            HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
            10);

        IPublisher<tf2_msgs.msg.TFMessage> _publisher;
        tf2_msgs.msg.TFMessage _message;

        void Start()
        {
            if (!AwsimRos2Node.IsInitialized)
                AwsimRos2Node.Initialize();
            Initialize();
        }

        /// <summary>Creates the publisher once; safe to call after node initialisation.</summary>
        public void Initialize()
        {
            if (_publisher != null || _sensor == null)
                return;
            _message = new tf2_msgs.msg.TFMessage
            {
                Transforms = new[]
                {
                    new geometry_msgs.msg.TransformStamped
                    {
                        Header = new std_msgs.msg.Header(),
                        Transform = new geometry_msgs.msg.Transform
                        {
                            Translation = new geometry_msgs.msg.Vector3(),
                            Rotation = new geometry_msgs.msg.Quaternion { W = 1d }
                        }
                    }
                }
            };
            _publisher = AwsimRos2Node.CreatePublisher<tf2_msgs.msg.TFMessage>(_topic, _qosSettings.GetQosProfile());
        }

        void FixedUpdate()
        {
            if (_publisher == null || _sensor == null)
                return;
            var sample = _sensor.Sample();
            var transform = _message.Transforms[0];
            transform.Header.Frame_id = sample.ParentFrame;
            transform.Child_frame_id = sample.ChildFrame;
            var position = Ros2Utility.UnityToRos2Position(sample.LocalPosition);
            transform.Transform.Translation.X = position.x;
            transform.Transform.Translation.Y = position.y;
            transform.Transform.Translation.Z = position.z;
            var rotation = Ros2Utility.UnityToRosRotation(sample.LocalRotation);
            transform.Transform.Rotation.X = rotation.x;
            transform.Transform.Rotation.Y = rotation.y;
            transform.Transform.Rotation.Z = rotation.z;
            transform.Transform.Rotation.W = rotation.w;
            var header = transform as MessageWithHeader;
            AwsimRos2Node.UpdateROSTimestamp(ref header);
            _publisher.Publish(_message);
        }

        void OnDestroy()
        {
            if (_publisher != null)
                AwsimRos2Node.RemovePublisher<tf2_msgs.msg.TFMessage>(_publisher);
        }
    }
}
