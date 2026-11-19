# CrabSense AI runtime
The platform registers a service, a trusted local YOLO object-detection artifact, and camera assignments. The runtime processes **already uploaded** image/video MediaAssets with an explicit camera and box. It does not control physical devices or automatically harvest crabs. Existing rules-engine analysis is not image inference.

## Setup
1. Deploy the BE migration 20261115220000_AiRuntimeManagement and open System Admin > AI services.
2. Create a service and register its initial model version. Copy a **trained CrabSense model** (.pt) into a private worker model directory; compute its SHA-256 and register filename/version/hash. There are no model weights in this repository. Only trust administrator-reviewed weights: PyTorch .pt files may execute code.
3. Configure the BE server environment variable AiRuntime__Workers__<service-guid>__Key with a distinct random key of at least 32 characters. Keep it out of Git, logs, and browser configuration. Restart BE to load configuration.
4. Run one worker per service in an isolated environment (Python 3.11 recommended):

    python -m venv .venv
    pip install -r ai-worker/requirements.txt
    # Set AI_API_URL (HTTPS API origin), AI_SERVICE_ID, AI_WORKER_KEY, AI_MODEL_DIR in environment.
    python ai-worker/worker.py

5. Wait for a Stopped heartbeat, then Restart to load the registered initial version. Deploy or Rollback loads and smoke-tests the target artifact before acknowledgement. A hash mismatch or failure leaves the previous confirmed version unchanged.
6. Assign a camera. Upload an image/video through the existing authenticated media workflow with its original DeviceId and BoxId (and CrabId if known). As a farm user assigned to the box area, POST its MediaId to /api/ai/inference/jobs (optional AiServiceId when multiple services are ready). Check the farm job detail and result history, and the System Admin technical jobs/24h metrics. System Admin cannot read private farm evidence. The worker reads private media through a service-scoped lease; it never requests public sharing.
7. Stop/unplug worker: runtime becomes Offline after 90s; command timeout is 5 min. Jobs have 10 min leases and at most three expired-lease attempts; explicit failed-job retry is available for the active model. A job pins model/version, camera, box, crab, threshold and sampling. Model changes do not relabel old jobs. Video input is limited to 100 MB, 300 sampled frames and a 180 s inference budget; longer videos should be split. Inference currently processes frames one at a time; BatchSize remains a stored setting, not a performance guarantee.
8. Run unit checks: cd ai-worker && python -m unittest test_worker.py.

## Boundaries / physical acceptance
No trained CrabSense artifact, camera stream, or worker deployment is included. Verify detection classes on labeled crab footage, model accuracy and latency with actual hardware before production use. NO2/NO3 analysis is a separate calibrated measurement pipeline, not a YOLO class. Automatic streaming capture, event-based alert deduplication and app push notifications are not implemented by this worker. The legacy /ai/analyze media endpoint still returns 501; use the new /api/ai/inference/jobs endpoint for asynchronous inference. No fake GPU or accuracy metrics are reported.
