import json
import struct
import tempfile
import unittest
import zlib
from pathlib import Path
from scripts.review_recording import validate, write_review, index_tags


def png(width, height):
    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(bytes(height * (width * 4 + 1)))) + chunk(b"IEND", b"")


class ReviewRecordingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.manifest = {"schemaVersion": 2, "seed": 1, "vehicleModel": "planar-no-slip-assist", "rgb": False}
        vector = {"x": 0, "y": 0, "z": 0}
        self.frames = [{"Step": 0, "EpisodeId": 1, "SimulationTime": 0.5, "VehicleModel": "planar-no-slip-assist",
                        "DirectActuation": vector, "Position": vector, "Velocity": vector, "FloatingOriginOffset": vector}]
        event_pose = {"step": 0, "simulationTime": .5, "position": vector, "floatingOriginOffset": vector}
        self.events = [{**event_pose, "kind": "recording-start", "episodeId": 1, "map": {"schemaVersion": 1}},
                       {**event_pose, "kind": "operator-tag", "episodeId": 1, "label": "near miss </script><script>alert(1)</script>"}]

    def tearDown(self):
        self.temp.cleanup()

    def run_validation(self):
        (self.root / "manifest.json").write_text(json.dumps(self.manifest))
        (self.root / "frames.jsonl").write_text("\n".join(map(json.dumps, self.frames)))
        (self.root / "events.jsonl").write_text("\n".join(map(json.dumps, self.events)))
        return validate(self.root)

    def test_assisted_data_is_valid_when_labelled(self):
        report, *_ = self.run_validation()
        self.assertTrue(report["valid"], report)
        self.assertEqual(1, report["operatorTags"])

    def test_rejects_detached_annotation_time_step_pose_and_label(self):
        original = dict(self.events[1])
        for field, value, expected in [
            ("simulationTime", float("nan"), "simulation time"),
            ("simulationTime", 99, "outside recording segment"),
            ("step", 200, "recording step"),
            ("position", None, "position"),
            ("floatingOriginOffset", {"x": 0, "y": 0, "z": float("inf")}, "floatingOriginOffset"),
            ("label", "  ", "operator label"),
            ("episodeId", 9, "another episode"),
        ]:
            with self.subTest(field=field, value=value):
                self.events[1] = dict(original, **{field: value})
                report, *_ = self.run_validation()
                self.assertFalse(report["valid"])
                self.assertIn(expected, " ".join(report["failures"]))

    def test_requested_but_missing_rgb_is_not_claimed_as_camera_data(self):
        self.manifest["rgb"] = True
        report, *_ = self.run_validation()
        self.assertFalse(report["valid"])
        self.assertIn("no camera images", " ".join(report["failures"]))

    def test_rejects_model_mismatch_and_reordered_steps(self):
        self.frames.append(dict(self.frames[0], VehicleModel="planar-single-track"))
        report, *_ = self.run_validation()
        self.assertFalse(report["valid"])
        self.assertIn("non-monotonic", " ".join(report["failures"]))
        self.assertIn("vehicle model", " ".join(report["failures"]))

    def test_rejects_invalid_camera_timing_and_payload(self):
        self.frames[0].update(CameraJpegBase64="not-a-jpeg", CameraFrameIndex=1, CameraSimulationTime=10)
        report, *_ = self.run_validation()
        self.assertFalse(report["valid"])
        self.assertIn("invalid camera JPEG", " ".join(report["failures"]))
        self.assertIn("not synchronized", " ".join(report["failures"]))

    def test_html_never_interprets_event_labels_as_markup(self):
        report, manifest, frames, events = self.run_validation()
        path = self.root / "review.html"
        write_review(path, report, manifest, frames, events)
        self.assertNotIn("</script><script>alert", path.read_text())

    def test_sparse_sensor_frames_keep_every_tag_with_explicit_time_offset(self):
        frames = [{"SimulationTime": t} for t in (0, 1, 2)]
        events = [{"kind": "operator-tag", "simulationTime": t, "label": str(t)} for t in (-.1, .5, 1.6, 2.1)]
        links = index_tags(frames, events)
        self.assertEqual([0, 0, 2, 2], [link["displayFrame"] for link in links])
        self.assertEqual(events, [link["event"] for link in links])
        for link in links:
            self.assertAlmostEqual(frames[link["displayFrame"]]["SimulationTime"] - link["event"]["simulationTime"],
                                   link["displayTimeOffsetSeconds"])
        self.assertEqual([], index_tags([], events))

    def prepare_multimodal(self):
        self.manifest.update(schemaVersion=3, depth=True, bev=True, cameraWidth=2, cameraHeight=1,
                             depthMaxMeters=10, bevSize=2, semanticClasses=["background", "road"])
        self.frames[0].update(CameraFrameIndex=1, CameraSimulationTime=0.5, MetricDepthFile="depth.f32",
                             DepthValidityFile="valid.u8", DepthPreviewFile="depth.png", BevLabelsFile="bev.u8",
                             BevPreviewFile="bev.png", AnnotationsFile="annotations.json")
        (self.root / "depth.f32").write_bytes(struct.pack("<ff", 2, 0))
        (self.root / "valid.u8").write_bytes(bytes([1, 0]))
        (self.root / "bev.u8").write_bytes(bytes([0, 1, 1, 0]))
        (self.root / "depth.png").write_bytes(png(2, 1))
        (self.root / "bev.png").write_bytes(png(2, 2))
        (self.root / "annotations.json").write_text(json.dumps({"frameIndex": 1, "simulationTime": 0.5,
            "source": "simulator-ground-truth-not-camera-visible"}))

    def test_multimodal_binary_layout_mask_and_timestamps(self):
        self.prepare_multimodal()
        report, *_ = self.run_validation()
        self.assertTrue(report["valid"], report)
        self.assertEqual(1, report["depthFrames"])
        self.assertEqual(1, report["bevFrames"])
        (self.root / "valid.u8").write_bytes(bytes([0, 0]))
        report, *_ = self.run_validation()
        self.assertFalse(report["valid"])
        self.assertIn("depth/validity", " ".join(report["failures"]))

    def test_wrong_size_unsynchronised_annotations_and_path_escape_fail(self):
        self.prepare_multimodal()
        (self.root / "bev.u8").write_bytes(bytes([0]))
        self.frames[0]["MetricDepthFile"] = "../outside.f32"
        report, *_ = self.run_validation()
        self.assertFalse(report["valid"])
        self.assertIn("escapes", " ".join(report["failures"]))
        self.assertIn("dimensions", " ".join(report["failures"]))
        self.prepare_multimodal()
        (self.root / "annotations.json").write_text(json.dumps({"frameIndex": 2, "simulationTime": 0.5}))
        report, *_ = self.run_validation()
        self.assertFalse(report["valid"])
        self.assertIn("not synchronized", " ".join(report["failures"]))

    def test_corrupt_or_wrong_size_preview_fails(self):
        self.prepare_multimodal()
        (self.root / "depth.png").write_bytes(png(2, 2))
        (self.root / "bev.png").write_bytes(png(2, 2)[:-5])
        report, *_ = self.run_validation()
        self.assertFalse(report["valid"])
        self.assertIn("PNG preview dimensions", " ".join(report["failures"]))
        self.assertIn("truncated PNG", " ".join(report["failures"]))


if __name__ == "__main__":
    unittest.main()
