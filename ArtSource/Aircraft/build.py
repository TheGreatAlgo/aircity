"""Stage 1 of the aircraft pipeline: geometry, UV atlas, bakes. See ArtSource/README.md.

    blender -b --factory-startup --python build.py -- --model Cessna172 --texture --check
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "Common"))

import pipeline  # noqa: E402

pipeline.stage1(pipeline.load_model())
