"""Download the release Demucs model into a PyInstaller-bundleable directory."""

import shutil
from pathlib import Path

import torch
import yaml
from demucs import pretrained
from demucs.pretrained import get_model

MODEL_NAME = "htdemucs"
BACKEND_DIR = Path(__file__).resolve().parents[1]
DESTINATION = BACKEND_DIR / "models"


def main() -> None:
    # This verifies the checkpoint and leaves it in torch.hub's cache.
    print(f"Caching Demucs model: {MODEL_NAME}")
    get_model(MODEL_NAME)

    remote_dir = Path(pretrained.__file__).resolve().parent / "remote"
    bag_file = remote_dir / f"{MODEL_NAME}.yaml"
    bag = yaml.safe_load(bag_file.read_text(encoding="utf-8"))
    signatures = bag["models"]

    checkpoint_dir = Path(torch.hub.get_dir()) / "checkpoints"
    DESTINATION.mkdir(parents=True, exist_ok=True)
    shutil.copy2(bag_file, DESTINATION / bag_file.name)

    for signature in signatures:
        matches = sorted(checkpoint_dir.glob(f"{signature}-*.th"))
        if not matches:
            raise FileNotFoundError(
                f"Downloaded checkpoint for signature {signature} was not found "
                f"in {checkpoint_dir}"
            )
        source = matches[0]
        destination = DESTINATION / source.name
        shutil.copy2(source, destination)
        size_mb = destination.stat().st_size / 1_000_000
        print(f"Cached {destination.name} ({size_mb:.1f} MB)")

    print(f"Bundle-ready model repository: {DESTINATION}")


if __name__ == "__main__":
    main()
