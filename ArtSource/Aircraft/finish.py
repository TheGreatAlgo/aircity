"""Stage 2 of the aircraft pipeline: maps, registration patch, LODs, FBX, profile, previews. See ArtSource/README.md.

    blender -b --factory-startup --python finish.py -- --model Cessna172 --previews
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "Common"))

import pipeline  # noqa: E402

pipeline.stage2(pipeline.load_model())
