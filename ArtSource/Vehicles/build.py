"""The vehicle pipeline (see ArtSource/README.md):

    blender -b --factory-startup --python build.py -- --model SnowPlough [--previews] [--check]
    blender -b --factory-startup --python build.py -- --model SnowPlough --geometry --views front34
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "Common"))

import vpipe  # noqa: E402

vpipe.run()
