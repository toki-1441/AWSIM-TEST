#!/usr/bin/env python3
"""Validate/review a self-contained operator recording. No server or external assets needed."""
import argparse
import bisect
import base64
import html
import json
import math
import struct
import zlib
from pathlib import Path


def validate_preview(raw, width, height):
    """Check the non-interlaced RGB/RGBA PNG format emitted by the simulator."""
    if not raw.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("invalid PNG preview signature")
    offset, compressed, channels, ended = 8, bytearray(), None, False
    while offset < len(raw):
        if offset + 12 > len(raw):
            raise ValueError("truncated PNG preview")
        length = struct.unpack_from(">I", raw, offset)[0]
        kind = raw[offset + 4:offset + 8]
        end = offset + 8 + length
        if end + 4 > len(raw) or zlib.crc32(raw[offset + 4:end]) != struct.unpack_from(">I", raw, end)[0]:
            raise ValueError("invalid PNG preview checksum/length")
        payload = raw[offset + 8:end]
        if kind == b"IHDR":
            if length != 13 or offset != 8:
                raise ValueError("invalid PNG preview header")
            w, h, bits, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", payload)
            if (w, h) != (width, height) or bits != 8 or color not in (2, 6) or (compression, filtering, interlace) != (0, 0, 0):
                raise ValueError("PNG preview dimensions/format disagree with metadata")
            channels = 3 if color == 2 else 4
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            ended = length == 0 and end + 4 == len(raw)
            break
        offset = end + 4
    if not ended or channels is None:
        raise ValueError("incomplete PNG preview")
    expected = height * (width * channels + 1)
    try:
        decoder = zlib.decompressobj()
        pixels = decoder.decompress(compressed, expected + 1)
        if len(pixels) != expected or not decoder.eof or decoder.unused_data or any(pixels[row * (width * channels + 1)] > 4 for row in range(height)):
            raise ValueError("invalid PNG preview pixels")
    except zlib.error as error:
        raise ValueError("invalid PNG preview compression") from error
    return raw


def validate(directory):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    frames = [json.loads(line) for line in (directory / "frames.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]
    events = [json.loads(line) for line in (directory / "events.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]
    failures = []
    if manifest.get("schemaVersion") not in (2, 3):
        failures.append("unsupported manifest schema")
    if not frames:
        failures.append("no recorded frames")
    images = []
    depth_frames = bev_frames = 0
    def sensor_file(relative):
        if not relative:
            raise ValueError("missing sensor file reference")
        path = (directory / relative).resolve()
        if not path.is_relative_to(directory.resolve()):
            raise ValueError("sensor path escapes the recording directory")
        return path.read_bytes()
    previous_time = -math.inf
    previous_step = -1
    previous_camera = -1
    models = set()
    for index, frame in enumerate(frames):
        timestamp = frame.get("SimulationTime", math.nan)
        step = frame.get("Step", -1)
        if not math.isfinite(timestamp) or timestamp < previous_time or step <= previous_step:
            failures.append(f"frame {index}: non-monotonic or invalid time/step")
        previous_time, previous_step = timestamp, step
        models.add(frame.get("VehicleModel"))
        if frame.get("EpisodeId") != manifest.get("seed"):
            failures.append(f"frame {index}: episode seed disagrees with manifest")
        for field in ("DirectActuation", "Position", "Velocity", "FloatingOriginOffset"):
            value = frame.get(field, {})
            if not all(isinstance(value.get(axis), (float, int)) and math.isfinite(value[axis]) for axis in "xyz"):
                failures.append(f"frame {index}: missing/nonfinite {field}")
        encoded = frame.get("CameraJpegBase64")
        if encoded:
            try:
                raw = base64.b64decode(encoded, validate=True)
                if not raw.startswith(b"\xff\xd8") or not raw.endswith(b"\xff\xd9"):
                    raise ValueError("not a complete JPEG")
            except (ValueError, base64.binascii.Error):
                failures.append(f"frame {index}: invalid camera JPEG")
            images.append(frame)
        sampled = frame.get("CameraFrameIndex", -1) >= 0 or encoded or frame.get("MetricDepthFile") or frame.get("BevLabelsFile")
        if sampled:
            camera_time = frame.get("CameraSimulationTime", math.nan)
            camera_id = frame.get("CameraFrameIndex", -1)
            if not math.isfinite(camera_time) or not 0 <= timestamp - camera_time <= 0.05 or camera_id <= previous_camera:
                failures.append(f"frame {index}: camera timestamp/index is not synchronized")
            previous_camera = camera_id
            if manifest.get("rgb") and not encoded:
                failures.append(f"frame {index}: RGB missing from synchronized sensor sample")
            if manifest.get("depth"):
                try:
                    raw = sensor_file(frame.get("MetricDepthFile"))
                    mask = sensor_file(frame.get("DepthValidityFile"))
                    expected = manifest["cameraWidth"] * manifest["cameraHeight"]
                    if len(raw) != expected * 4 or len(mask) != expected:
                        raise ValueError("depth dimensions do not match camera calibration")
                    depths = [v[0] for v in struct.iter_unpack("<f", raw)]
                    for depth, valid in zip(depths, mask):
                        if valid not in (0, 1) or not math.isfinite(depth) or (valid == 0 and depth != 0) or (valid == 1 and not 0 < depth <= manifest["depthMaxMeters"]):
                            raise ValueError("invalid depth/validity/range contract")
                    preview = validate_preview(sensor_file(frame.get("DepthPreviewFile")), manifest["cameraWidth"], manifest["cameraHeight"])
                    frame["DepthPreviewBase64"] = base64.b64encode(preview).decode()
                    frame["DepthMetresBase64"] = base64.b64encode(raw).decode()
                    values = [d for d, valid in zip(depths, mask) if valid]
                    frame["DepthSummary"] = {"validPixels": len(values), "minimumMeters": min(values, default=None), "maximumMeters": max(values, default=None)}
                    depth_frames += 1
                except (OSError, ValueError, KeyError) as error:
                    failures.append(f"frame {index}: {error}")
            if manifest.get("bev"):
                try:
                    labels = sensor_file(frame.get("BevLabelsFile"))
                    if len(labels) != manifest["bevSize"] ** 2 or any(label >= len(manifest["semanticClasses"]) for label in labels):
                        raise ValueError("invalid BEV dimensions/classes")
                    annotations = json.loads(sensor_file(frame.get("AnnotationsFile")))
                    if annotations["frameIndex"] != camera_id or abs(annotations["simulationTime"] - camera_time) > 1e-5:
                        raise ValueError("BEV annotations are not synchronized")
                    if annotations.get("source") != "simulator-ground-truth-not-camera-visible":
                        raise ValueError("BEV must identify privileged ground truth")
                    preview = validate_preview(sensor_file(frame.get("BevPreviewFile")), manifest["bevSize"], manifest["bevSize"])
                    frame["BevPreviewBase64"] = base64.b64encode(preview).decode()
                    frame["BevAnnotations"] = annotations
                    bev_frames += 1
                except (OSError, ValueError, KeyError) as error:
                    failures.append(f"frame {index}: {error}")
    if models != {manifest.get("vehicleModel")}:
        failures.append("vehicle model changed within a recording or differs from manifest")
    if manifest.get("rgb") and not images:
        failures.append("RGB was requested but no camera images were recorded")
    if manifest.get("depth") and not depth_frames:
        failures.append("depth was requested but no valid depth frames were recorded")
    if manifest.get("bev") and not bev_frames:
        failures.append("BEV was requested but no valid BEV frames were recorded")
    if not any(event.get("kind") == "recording-start" and event.get("map") for event in events):
        failures.append("missing initial map snapshot")
    for index, event in enumerate(events):
        if event.get("episodeId") != manifest.get("seed"):
            failures.append("event belongs to another episode")
        timestamp = event.get("simulationTime")
        step = event.get("step")
        if not isinstance(timestamp, (int, float)) or not math.isfinite(timestamp):
            failures.append(f"event {index}: missing/nonfinite simulation time")
        if type(step) is not int or step < 0 or (frames and step > frames[-1].get("Step", -1) + 1):
            failures.append(f"event {index}: invalid recording step")
        for field in ("position", "floatingOriginOffset"):
            vector = event.get(field)
            if not isinstance(vector, dict) or not all(isinstance(vector.get(axis), (int, float)) and math.isfinite(vector[axis]) for axis in "xyz"):
                failures.append(f"event {index}: missing/nonfinite {field}")
        if event.get("kind") == "operator-tag":
            label = event.get("label")
            if not isinstance(label, str) or not label.strip():
                failures.append(f"event {index}: empty/invalid operator label")
            if frames and isinstance(timestamp, (int, float)) and math.isfinite(timestamp):
                starts = [e.get("simulationTime") for e in events if e.get("kind") == "recording-start" and isinstance(e.get("simulationTime"), (int, float)) and math.isfinite(e["simulationTime"])]
                start = min(starts) if starts else frames[0].get("SimulationTime", 0)
                if timestamp < start - 1e-4 or timestamp > frames[-1].get("SimulationTime", 0) + 1e-4:
                    failures.append(f"event {index}: operator tag time outside recording segment")
    report = {
        "valid": not failures, "source": str(directory.resolve()), "vehicleModel": manifest.get("vehicleModel"),
        "records": len(frames), "rgbFrames": len(images), "operatorTags": sum(e.get("kind") == "operator-tag" for e in events),
        "depthFrames": depth_frames, "bevFrames": bev_frames,
        "failures": failures,
        "note": "Structural validation only; not a physical-fidelity, camera-policy or planning-label quality claim.",
    }
    return report, manifest, frames, events


def index_tags(frames, events):
    """Navigation links only: retain actual tag time/pose, never pretend it was image-synchronous."""
    if not frames:
        return []
    times = [frame["SimulationTime"] for frame in frames]
    result = []
    for event in events:
        timestamp = event.get("simulationTime")
        if event.get("kind") != "operator-tag" or not isinstance(timestamp, (int, float)) or not math.isfinite(timestamp):
            continue
        right = bisect.bisect_left(times, timestamp)
        candidates = {max(0, right - 1), min(len(times) - 1, right)}
        index = min(candidates, key=lambda i: (abs(times[i] - timestamp), i))
        result.append({"event": event, "displayFrame": index, "displayTimeOffsetSeconds": times[index] - timestamp})
    return result


def write_review(path, report, manifest, frames, events):
    # Text is inserted with textContent, never evaluated as HTML. Escape </script> in JSON.
    samples = [f for f in frames if f.get("CameraJpegBase64") or f.get("DepthPreviewBase64") or f.get("BevPreviewBase64")]
    data = json.dumps({"manifest": manifest, "frames": frames, "events": events,
                      "tagNavigation": index_tags(samples or frames, events)}).replace("</", "<\\/")
    page = """<!doctype html><meta charset="utf-8"><title>Physical AI recording review</title>
<style>body{background:#14202c;color:#eee;font:16px sans-serif;margin:24px} img{width:30vw;object-fit:contain;vertical-align:top;image-rendering:auto} input{width:90vw} pre{white-space:pre-wrap} button{padding:8px}</style>
<h1>Recording review</h1><p>REPORT</p><button id="play">Play / pause</button>
<p><label>Operator tag: <select id="tag"><option value="">Choose a tag</option></select></label>
Nearest displayed frame; tag time/pose remain unchanged. <span id="tagOffset"></span></p>
<input id="seek" type="range" min="0" value="0"><p id="status"></p><p>RGB | optical-axis depth (hover for metres) | BEV ground truth</p>
<img id="rgb"><img id="depth"><img id="bev"><p id="pixel"></p><pre id="details"></pre>
<script>const data=DATA; const samples=data.frames.filter(f=>f.CameraJpegBase64||f.DepthPreviewBase64||f.BevPreviewBase64);
const frames=samples.length?samples:data.frames; const seek=document.getElementById('seek');
seek.max=Math.max(0,frames.length-1); let playing=false,elapsed=0,last=performance.now();
const tagSelect=document.getElementById('tag');
data.tagNavigation.forEach((link,i)=>{const option=document.createElement('option');option.value=i;
option.textContent=`${link.event.simulationTime.toFixed(3)} s: ${link.event.label}`;tagSelect.appendChild(option);});
tagSelect.disabled=data.tagNavigation.length===0;
tagSelect.onchange=()=>{if(tagSelect.value==='')return;const link=data.tagNavigation[+tagSelect.value];
playing=false;elapsed=0;seek.value=link.displayFrame;show();
document.getElementById('tagOffset').textContent=`Display time minus tag time: ${link.displayTimeOffsetSeconds.toFixed(3)} s`;};
function show(){const f=frames[+seek.value]; if(!f)return;
document.getElementById('tagOffset').textContent='';
document.getElementById('status').textContent=`Record ${seek.value}/${frames.length-1}, t=${f.SimulationTime.toFixed(3)} s, camera=${f.CameraFrameIndex}`;
// Missing imagery is displayed explicitly; do not silently reuse a stale camera frame.
const img=document.getElementById('rgb');img.hidden=!f.CameraJpegBase64;
if(f.CameraJpegBase64)img.src='data:image/jpeg;base64,'+f.CameraJpegBase64;
for(const [id,key] of [['depth','DepthPreviewBase64'],['bev','BevPreviewBase64']]){
const view=document.getElementById(id);view.hidden=!f[key];if(f[key])view.src='data:image/png;base64,'+f[key];}
const tags=data.tagNavigation.filter(link=>link.displayFrame===+seek.value);
document.getElementById('details').textContent=JSON.stringify({vehicleModel:f.VehicleModel,commandSpace:f.CommandSpace,
actuation:f.DirectActuation,position:f.Position,velocity:f.Velocity,originOffset:f.FloatingOriginOffset,terminal:f.Terminal,end:f.EndReason,
depth:f.DepthSummary,bev:f.BevAnnotations,tags},null,2);}
document.getElementById('depth').onmousemove=e=>{const f=frames[+seek.value];if(!f.DepthMetresBase64)return;
const rect=e.target.getBoundingClientRect(),w=data.manifest.cameraWidth,h=data.manifest.cameraHeight;
const x=Math.min(w-1,Math.floor((e.clientX-rect.left)/rect.width*w)),y=Math.min(h-1,Math.floor((e.clientY-rect.top)/rect.height*h));
const bytes=Uint8Array.from(atob(f.DepthMetresBase64),c=>c.charCodeAt(0));const d=new DataView(bytes.buffer).getFloat32((y*w+x)*4,true);
document.getElementById('pixel').textContent=`(${x}, ${y}): ${d>0?d.toFixed(3)+' m':'invalid'}`;};
seek.oninput=()=>{elapsed=0;show();};document.getElementById('play').onclick=()=>{playing=!playing;last=performance.now();};
setInterval(()=>{const now=performance.now();if(playing){elapsed+=(now-last)/1000;let i=+seek.value;
while(i<frames.length-1&&elapsed>=frames[i+1].SimulationTime-frames[i].SimulationTime){elapsed-=frames[i+1].SimulationTime-frames[i].SimulationTime;i++;}
seek.value=i;show();if(i===frames.length-1)playing=false;}last=now;},20);show();</script>"""
    path.write_text(page.replace("DATA", data, 1).replace("REPORT", html.escape(json.dumps(report, ensure_ascii=False)), 1), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recording", type=Path)
    parser.add_argument("--output", type=Path, required=True, help="new directory for report and standalone HTML review")
    args = parser.parse_args()
    if args.output.exists() and any(args.output.iterdir()):
        parser.error("output directory is not empty; choose a new review destination")
    report, manifest, frames, events = validate(args.recording)
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    write_review(args.output / "review.html", report, manifest, frames, events)
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report["valid"] else 1)


if __name__ == "__main__":
    main()
