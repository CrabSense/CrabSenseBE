import hashlib
import pathlib
import tempfile
import unittest
from worker import checked_artifact, sample_stride, api_root


class WorkerTests(unittest.TestCase):
    def test_artifact_hash_and_path(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = pathlib.Path(tmp) / "model.pt"
            p.write_bytes(b"fixture-not-a-model")
            expected = hashlib.sha256(p.read_bytes()).hexdigest()
            self.assertEqual(checked_artifact(tmp, p.name, expected), p)
            with self.assertRaises(ValueError):
                checked_artifact(tmp, p.name, "0" * 64)
            with self.assertRaises(ValueError):
                checked_artifact(tmp, "../model.pt", expected)

    def test_sampling(self):
        self.assertEqual(sample_stride(30, 2), 15)
        self.assertEqual(sample_stride(25, 120), 1)
        with self.assertRaises(ValueError):
            sample_stride(0, 2)

    def test_transport(self):
        self.assertEqual(api_root("https://example.com", "id"), "https://example.com/api/ai/runtime/id")
        with self.assertRaises(ValueError):
            api_root("http://example.com", "id")


if __name__ == "__main__":
    unittest.main()
