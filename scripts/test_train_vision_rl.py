#!/usr/bin/env python3
"""Fast unit checks for the visual policy's episode-level update semantics."""
import unittest
import json
import socket
import threading
import base64
from io import BytesIO
from pathlib import Path
from tempfile import TemporaryDirectory

import torch
from PIL import Image

from train_vision_rl import VisionReinforceTrainer, TrainerServer, Handler


class VisionReinforceTrainerTests(unittest.TestCase):
    def test_discounted_returns_preserve_terminal_timeout_penalty(self):
        trainer = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"))

        returns = trainer.discounted_returns([0.0, 0.0, -5.0])

        self.assertEqual(returns[-1], -5.0)
        self.assertLess(returns[0], 0.0)

    def test_episode_update_keeps_cross_episode_baseline(self):
        trainer = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"))
        trainer._ensure_policy([0.0, 0.0])
        parameter = trainer.policy.log_std
        trainer.rollout = [
            {"log_probability": parameter.sum(), "entropy": parameter.sum(), "reward": 0.0},
            {"log_probability": parameter.sum(), "entropy": parameter.sum(), "reward": 0.0},
        ]

        metric = trainer.finish_episode(-5.0)

        self.assertEqual(metric["return"], -5.0)
        self.assertLess(metric["returnBaseline"], 0.0)
        self.assertGreater(metric["returnScale"], 0.0)

    def test_initializes_from_compatible_visual_bc_checkpoint(self):
        source = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"))
        source._ensure_policy([0.0, 0.0])
        with TemporaryDirectory() as directory:
            checkpoint = f"{directory}/visual-bc.pt"
            source.save(Path(checkpoint))
            trainer = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"), initialize_from=checkpoint)
            trainer._ensure_policy([0.0, 0.0])

        self.assertTrue(torch.equal(source.policy.log_std, trainer.policy.log_std))

    def test_full_resume_restores_optimizer_statistics_rng_and_environment(self):
        torch.set_num_threads(1)
        source = VisionReinforceTrainer(0.002, 0.8, torch.device("cpu"))
        contract = {"schemaVersion": 1, "mapMode": "fixed", "seed": 7}
        source.bind_environment(contract)
        observation = [0.0, 3.0]
        source.act(observation, None)
        source.finish_episode(-2)
        with TemporaryDirectory() as directory:
            checkpoint = Path(directory) / "resume.pt"
            source.save(checkpoint)
            expected = source.act(observation, None)
            resumed = VisionReinforceTrainer(0.1, 0.1, torch.device("cpu"), resume=checkpoint)
            resumed.bind_environment(contract)
            actual = resumed.act(observation, None)
            self.assertEqual(expected, actual)
            self.assertEqual(1, resumed.episode)
            self.assertEqual(source.return_baseline, resumed.return_baseline)
            self.assertEqual(source.return_scale, resumed.return_scale)
            self.assertEqual(source.discount, resumed.discount)
            self.assertEqual(source.learning_rate, resumed.learning_rate)
            source.finish_episode(1)
            resumed.finish_episode(1)
            for expected_parameter, actual_parameter in zip(source.policy.parameters(), resumed.policy.parameters()):
                self.assertTrue(torch.equal(expected_parameter, actual_parameter), "Optimizer resume must reproduce the next update")

    def test_resume_rejects_weights_only_or_changed_environment(self):
        source = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"))
        source._ensure_policy([0, 0])
        with TemporaryDirectory() as directory:
            checkpoint = Path(directory) / "checkpoint.pt"
            source.save(checkpoint)
            with self.assertRaisesRegex(ValueError, "optimizer and environment"):
                VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"), resume=checkpoint)
            source.bind_environment({"schemaVersion": 1, "mapMode": "fixed"})
            source.save(checkpoint)
            resumed = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"), resume=checkpoint)
            with self.assertRaisesRegex(ValueError, "settings differ"):
                resumed.bind_environment({"schemaVersion": 1, "mapMode": "endless"})
            with self.assertRaisesRegex(ValueError, "observation layout"):
                resumed._ensure_policy([0, 0, 0])

    def test_operator_protocol_requires_handshake_and_rgb_without_silent_training(self):
        trainer = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"))
        with TemporaryDirectory() as directory:
            root = Path(directory)
            with TrainerServer(("127.0.0.1", 0), Handler, trainer, root / "model.pt", root / "metrics.jsonl", True) as server:
                thread = threading.Thread(target=server.serve_forever, daemon=True)
                thread.start()
                try:
                    with socket.create_connection(server.server_address, timeout=3) as client, client.makefile("rw") as stream:
                        stream.write(json.dumps({"type": "step", "observation": [0, 0]}) + "\n"); stream.flush()
                        self.assertIn("handshake", json.loads(stream.readline())["error"])
                    with socket.create_connection(server.server_address, timeout=3) as client, client.makefile("rw") as stream:
                        stream.write(json.dumps({"type": "hello", "environment": {"schemaVersion": 1}}) + "\n"); stream.flush()
                        self.assertTrue(json.loads(stream.readline())["ack"])
                        stream.write(json.dumps({"type": "step", "observation": [0, 0]}) + "\n"); stream.flush()
                        self.assertIn("camera frame", json.loads(stream.readline())["error"])
                    self.assertIsNone(trainer.policy)
                    self.assertFalse((root / "model.pt").exists())
                finally:
                    server.shutdown(); thread.join(timeout=3)

    def test_stop_ack_means_checkpoint_and_metrics_were_saved_with_episode_identity(self):
        torch.set_num_threads(1)
        trainer = VisionReinforceTrainer(0.001, 0.99, torch.device("cpu"))
        encoded = BytesIO()
        Image.new("RGB", (32, 32), (30, 60, 90)).save(encoded, format="JPEG")
        contract = {"schemaVersion": 1, "cameraHz": 10}
        with TemporaryDirectory() as directory:
            root = Path(directory)
            with TrainerServer(("127.0.0.1", 0), Handler, trainer, root / "model.pt", root / "metrics.jsonl", True) as server:
                thread = threading.Thread(target=server.serve_forever, daemon=True)
                thread.start()
                try:
                    with socket.create_connection(server.server_address, timeout=5) as client, client.makefile("rw") as stream:
                        def request(value):
                            stream.write(json.dumps(value) + "\n"); stream.flush()
                            return json.loads(stream.readline())
                        self.assertTrue(request({"type": "hello", "environment": contract})["ack"])
                        action = request({"type": "step", "observation": [0, 3], "reward": 0,
                            "cameraJpegBase64": base64.b64encode(encoded.getvalue()).decode(), "episodeId": 7,
                            "cameraFrameIndex": 9, "simulationTime": 0.5, "cameraSimulationTime": 0.5})
                        self.assertIn("desiredSpeed", action)
                        response = request({"type": "stop", "reward": 1})
                        self.assertTrue(response["ack"])
                        self.assertEqual(1, response["completedEpisodes"])
                        checkpoint = torch.load(root / "model.pt", weights_only=True)
                        self.assertEqual(contract, checkpoint["environment_contract"])
                        self.assertTrue(checkpoint["optimizer_state"]["state"])
                        metric = json.loads((root / "metrics.jsonl").read_text().strip())
                        self.assertEqual("operator-stop", metric["reason"])
                        self.assertEqual(7, metric["episodeId"])
                        self.assertEqual(9, metric["firstCameraFrame"])
                        self.assertEqual(1, metric["return"])
                        self.assertEqual(1, metric["steps"])
                finally:
                    server.shutdown(); thread.join(timeout=3)


if __name__ == "__main__":
    unittest.main()
