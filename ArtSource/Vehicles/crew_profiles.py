"""Refresh only workstation metadata without rebaking unchanged vehicle geometry (python3)."""
import ast
import json
from pathlib import Path

here = Path(__file__).resolve().parent
for name in ("BaggageTractor", "BeltLoader", "ContainerLoader"):
    tree = ast.parse((here / name / "model.py").read_text())
    declaration = next(n.value for n in tree.body if isinstance(n, ast.Assign) and
                       any(isinstance(t, ast.Name) and t.id == "CREW" for t in n.targets))
    crew = {arg.arg: ast.literal_eval(arg.value) for arg in declaration.keywords}
    unity = lambda v: [v[0], v[2], v[1]]
    crew = {k: [{"position": unity(p)} for p in v] if k == "accessSteps" else
            unity(v) if isinstance(v, tuple) else v for k, v in crew.items()}
    path = here.parent.parent / "Assets/AirCity/Vehicles" / name / (name + ".vehicle")
    profile = json.loads(path.read_text()); profile["crew"] = crew
    path.write_text(json.dumps(profile, indent=2))
    print(path)
