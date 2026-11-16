"""Refresh only generated deicing skin metadata; run through Blender, without rebaking aircraft art."""
import importlib
import json
from pathlib import Path
import sys

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here / "Common"))
from deicing import sections

for name in ("Cessna172", "Narrowbody", "Widebody", "DoubleDeck", "Quadjet", "Turboprop", "Trijet", "Supersonic"):
    folder = str(here / name)
    sys.path.insert(0, folder)
    for module in ("parts", "model"):
        sys.modules.pop(module, None)
    model = importlib.import_module("model")
    path = here.parent.parent / "Assets" / "AirCity" / "Aircraft" / name / (name + ".aircraft")
    profile = json.loads(path.read_text())
    profile["deicingWing"] = sections(model)
    if len(profile["deicingWing"]) < 2:
        raise RuntimeError("No deicing wing sections for " + name)
    path.write_text(json.dumps(profile, indent=2) + "\n")
    sys.path.remove(folder)
    print("Deicing skin", name, len(profile["deicingWing"]), "sections")
