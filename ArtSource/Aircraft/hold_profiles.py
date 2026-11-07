"""Refresh hold packing envelopes from source definitions without rebaking unchanged aircraft geometry."""
import importlib
import json
import os
import sys

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(here,"Common"))
import cargo_doors

for name in ("Cessna172","Narrowbody","Widebody","DoubleDeck","Quadjet"):
    folder = os.path.join(here,name); sys.path.insert(0,folder)
    for module in ("parts","model"): sys.modules.pop(module,None)
    model = importlib.import_module("model")
    path = os.path.join(here,"../../Assets/AirCity/Aircraft",name,name+".aircraft")
    with open(path) as stream: profile = json.load(stream)
    definitions = {d["point"]:d for d in cargo_doors.definitions(model)}
    for door in profile["cargoDoors"]:
        for key in ("holdSideMin","holdSideMax","holdHeight","holdFloorStart"): door[key] = definitions[door["point"]][key]
    with open(path,"w") as stream: json.dump(profile,stream,indent=2)
    sys.path.remove(folder)
    print("hold envelope",name)
