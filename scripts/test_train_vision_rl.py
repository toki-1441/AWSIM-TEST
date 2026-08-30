#!/usr/bin/env python3
"""Fast unit checks for the visual policy's episode-level update semantics."""
import unittest

import torch

from train_vision_rl import VisionReinforceTrainer


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


if __name__ == "__main__":
    unittest.main()
