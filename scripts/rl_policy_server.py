#!/usr/bin/env python3
"""Reference JSON-lines policy server for the Unity PythonRlBridge protocol.

Replace `choose_action` with a PPO/SAC policy. The transport deliberately has no ML framework
dependency so a single Unity simulation can be exercised before selecting a trainer.
"""
import argparse
import json
import socketserver


def choose_action(observation, camera_jpeg_base64=None):
    # `camera_jpeg_base64` is the rendered primary sensor frame. Decode it in a
    # vision policy; this reference policy deliberately retains the safe vector fallback.
    safe_speed = observation[1] if len(observation) > 1 else 0.0
    return {"desiredSpeed": max(0.0, safe_speed), "desiredLateralOffsetNormalized": 0.0}


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        for raw in self.rfile:
            request = json.loads(raw)
            if request.get("type") == "step":
                self.wfile.write((json.dumps(choose_action(request.get("observation", []), request.get("cameraJpegBase64"))) + "\n").encode())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=5015)
    args = parser.parse_args()
    with socketserver.TCPServer(("127.0.0.1", args.port), Handler) as server:
        print(f"Unity RL bridge listening on 127.0.0.1:{args.port}")
        server.serve_forever()


if __name__ == "__main__":
    main()
