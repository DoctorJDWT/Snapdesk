"""
Regenerate ``assets/app_icon.ico`` and PPM variants (stdlib only).

Run from the repo root:

  python scripts/build_app_assets.py
"""

from __future__ import annotations

import sys
from pathlib import Path

_ROOT = Path(__file__).resolve().parents[1]
_SRC = _ROOT / "src"
if str(_SRC) not in sys.path:
    sys.path.insert(0, str(_SRC))

from app_assetgen import build_all  # noqa: E402


def main() -> None:
    assets = _ROOT / "assets"
    build_all(assets)
    ico = assets / "app_icon.ico"
    print(f"Wrote {ico} ({ico.stat().st_size} bytes)")
    print("Wrote app_icon_dark.ppm and app_icon_light.ppm (64×64)")


if __name__ == "__main__":
    main()
