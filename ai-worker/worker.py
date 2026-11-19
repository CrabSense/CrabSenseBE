"""CrabSense inference worker: local trusted YOLO weights, no synthetic detections."""
import hashlib
import json
import logging
import math
import os
import pathlib
import tempfile
import threading
import time
from urllib.parse import urlsplit

LOG = logging.getLogger("crabsense-ai")


def checked_artifact(root, name, expected):
    root = pathlib.Path(root).resolve()
    path = (root / name).resolve()
    if path.parent != root or not path.is_file() or path.suffix != ".pt":
        raise ValueError("Model artifact is missing or outside model directory")
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest() != expected.lower():
        raise ValueError("Model SHA-256 mismatch")
    return path


def sample_stride(source_fps, target_fps):
    if not math.isfinite(source_fps) or source_fps <= 0 or target_fps <= 0:
        raise ValueError("Invalid video FPS")
    return max(1, math.ceil(source_fps / target_fps))


def api_root(url, service):
    parsed = urlsplit(url)
    if parsed.scheme != "https" and not (parsed.scheme == "http" and parsed.hostname in ("localhost", "127.0.0.1", "::1")):
        raise ValueError("Use HTTPS except for localhost development")
    if parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise ValueError("Invalid API URL")
    return url.rstrip("/") + "/api/ai/runtime/" + service


class Worker:
    def __init__(self, root, service, key, models):
        self.root = api_root(root, service)
        self.headers = {"X-AI-Worker-Key": key}
        self.models = models
        self.model = None
        self.version = None
        self.state = "Stopped"

    def post(self, path, data=None):
        import requests
        response = requests.post(self.root + path, headers=self.headers, json=data, timeout=(5, 30))
        response.raise_for_status()
        return response.json().get("data")

    def heartbeat(self):
        return self.post("/heartbeat", {"state": self.state, "version": self.version})

    def command(self, command):
        try:
            if command["action"] == "Stop":
                self.model = None
                self.version = None
                self.state = "Stopped"
            else:
                from ultralytics import YOLO
                import numpy as np
                spec = command["model"]
                path = checked_artifact(self.models, spec["artifactName"], spec["sha256"])
                candidate = YOLO(str(path), task="detect")
                # Smoke inference validates the artifact before acknowledging deployment.
                candidate.predict(np.zeros((320, 320, 3), dtype=np.uint8), verbose=False)
                self.model = candidate
                self.version = spec["version"]
                self.state = "Running"
        except Exception as error:
            LOG.warning("Model command failed: %s", type(error).__name__)
            self.post("/ack", {"commandId": command["id"], "success": False,
                              "version": self.version, "error": str(error)[:400]})
            return
        self.post("/ack", {"commandId": command["id"], "success": True, "version": self.version})

    def download(self, job, destination):
        import requests
        with requests.get(self.root + "/jobs/" + job["id"] + "/content",
                          headers=self.headers, params={"lease": job["leaseId"]},
                          stream=True, timeout=(5, 45)) as response:
            response.raise_for_status()
            size = 0
            with open(destination, "wb") as stream:
                for chunk in response.iter_content(65536):
                    size += len(chunk)
                    if size > 100 * 1024 * 1024:
                        raise ValueError("Media exceeds 100 MB")
                    stream.write(chunk)
            if size == 0:
                raise ValueError("Empty media")

    def infer(self, job, category, path):
        import cv2
        if self.model is None or self.version != job["modelVersion"]:
            raise ValueError("Pinned model is not loaded")
        threshold = float(job["confidenceThreshold"])
        labels = set(json.loads(job["detectionClassesJson"]))
        start = time.monotonic()
        best = None
        sampled = 0

        def analyze(frame, frame_index):
            nonlocal best, sampled
            if time.monotonic() - start > 180 or sampled >= 300:
                raise ValueError("Inference budget exceeded; submit a shorter clip")
            sampled += 1
            for result in self.model.predict(frame, conf=max(0.001, threshold), verbose=False):
                if result.boxes is None:
                    raise ValueError("Model is not an object detector")
                for box in result.boxes:
                    confidence = float(box.conf[0])
                    label = str(result.names[int(box.cls[0])])
                    if confidence < threshold or (labels and label not in labels):
                        continue
                    if best is None or confidence > best["confidence"]:
                        best = {"label": label, "confidence": confidence, "frameIndex": frame_index,
                                "xyxy": [float(x) for x in box.xyxy[0].tolist()]}

        if category == "image":
            image = cv2.imread(str(path))
            if image is None:
                raise ValueError("Cannot decode image")
            analyze(image, 0)
        elif category == "video":
            capture = cv2.VideoCapture(str(path))
            try:
                if not capture.isOpened():
                    raise ValueError("Cannot decode video")
                stride = sample_stride(capture.get(cv2.CAP_PROP_FPS), float(job["frameSamplingFps"]))
                index = 0
                while True:
                    if time.monotonic() - start > 180:
                        raise ValueError("Inference budget exceeded; submit a shorter clip")
                    ok, frame = capture.read()
                    if not ok:
                        break
                    if index % stride == 0:
                        analyze(frame, index)
                    index += 1
            finally:
                capture.release()
        else:
            raise ValueError("Unsupported media category")
        if sampled == 0:
            raise ValueError("No decodable frames")
        return {"label": best["label"] if best else "no_detection",
                "confidence": best["confidence"] if best else 0,
                "evidence": {"bestDetection": best, "sampledFrames": sampled,
                             "samplingFps": job["frameSamplingFps"], "category": category}}

    def process(self, claimed):
        job = claimed["job"]
        category = claimed["category"]
        started = time.monotonic()
        stop = threading.Event()

        def keepalive():
            while not stop.wait(15):
                try:
                    self.heartbeat()
                except Exception:
                    LOG.warning("Heartbeat during inference failed")

        thread = threading.Thread(target=keepalive, daemon=True)
        thread.start()
        payload = {"leaseId": job["leaseId"]}
        try:
            with tempfile.TemporaryDirectory(prefix="crabsense-ai-") as tmp:
                path = pathlib.Path(tmp) / ("input.jpg" if category == "image" else "input.mp4")
                self.download(job, path)
                payload.update(self.infer(job, category, path))
            payload.update(success=True, durationMs=int((time.monotonic() - started) * 1000))
        except Exception as error:
            LOG.warning("Inference failed: %s", type(error).__name__)
            payload.update(success=False, error=str(error)[:400])
        finally:
            stop.set()
            thread.join(timeout=35)
        self.post("/jobs/" + job["id"] + "/result", payload)

    def run(self):
        while True:
            try:
                response = self.heartbeat()
                if not response["enabled"]:
                    self.model = None
                    self.version = None
                    self.state = "Stopped"
                if response.get("command"):
                    self.command(response["command"])
                elif self.model is not None and response["enabled"]:
                    claimed = self.post("/claim")
                    if claimed:
                        self.process(claimed)
                        continue
            except Exception as error:
                LOG.warning("Worker operation failed: %s", type(error).__name__)
            time.sleep(5)


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    Worker(os.environ["AI_API_URL"], os.environ["AI_SERVICE_ID"],
           os.environ["AI_WORKER_KEY"], os.environ["AI_MODEL_DIR"]).run()
