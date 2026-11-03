"""Cut cargo hatches from the baked skin, retaining every interpolated UV and shading normal.

This runs in stage 2: the closed skin remains the stage-1 bake surface, so a door carries exactly the
same paint as its surround. Door definitions come from the model's gameplay points and CARGO_HINGES.
"""
import math

import bpy
import numpy as np
from mathutils import Vector


def definitions(model):
    out = []
    for kind, name, pos, normal, size in model.POINTS:
        if kind != "CargoDoor":
            continue
        hinge, angle, seconds = model.CARGO_HINGES[name]
        x, y, z = pos
        w, h = size
        station = y + model.S_REF
        sign = 1 if x > 0 else -1
        hs, hz = (station - w / 2, z + h / 2) if hinge == "Forward" else (station, z + h)
        pivot = (sign * model.Fuselage.side_x(hs, hz), hs - model.S_REF, hz)
        if hinge == "Top" and angle < 0:
            # Plug hatches rotate about an inboard hinge; a skin-level axis clips their curved upper edge.
            pivot = (pivot[0] - sign * 0.10, pivot[1], pivot[2])
        hold = getattr(model, "CARGO_HOLDS", {}).get(name)
        depth = min(abs(x) * 1.6, 2.5)
        if hold:
            # The floor stays within the curved shell throughout the compartment, including the nose taper.
            half_width = min(model.Fuselage.side_x(s, z) for s in np.linspace(*hold, 32)) - 0.08
            depth = abs(x) + half_width
        out.append(dict(point=name, transform="CargoDoor_" + name, pivot=pivot,
                        axis=(0, 0, 1) if hinge == "Forward" else (0, -1, 0),
                        # These are Unity rotation signs (Blender port becomes Unity -X).
                        openAngle=angle * sign if hinge == "Forward" else -angle * sign,
                        seconds=seconds, position=pos, size=size, sign=sign,
                        holdDepth=depth, hold=hold))
    return out


def _clip(poly, axis, value, greater):
    """Sutherland-Hodgman, including normals and all UV sets in each vertex record."""
    result = []
    for a, b in zip(poly, poly[1:] + poly[:1]):
        da, db = (a[axis] - value, b[axis] - value) if isinstance(axis, int) else \
            (np.dot(a[:3], axis) - value, np.dot(b[:3], axis) - value)
        inside_a = da >= -1e-9 if greater else da <= 1e-9
        inside_b = db >= -1e-9 if greater else db <= 1e-9
        if inside_a:
            result.append(a)
        if inside_a != inside_b:
            result.append(a + (b - a) * (da / (da - db)))
    # Cuts through existing vertices must not emit repeated/zero-area corners.
    clean = []
    for v in result:
        if not clean or np.linalg.norm(v[:3] - clean[-1][:3]) > 1e-7:
            clean.append(v)
    if len(clean) > 1 and np.linalg.norm(clean[0][:3] - clean[-1][:3]) < 1e-7:
        clean.pop()
    if len(clean) < 3:
        return []
    area = sum((np.cross(clean[i][:3] - clean[0][:3], clean[i + 1][:3] - clean[0][:3])
                for i in range(1, len(clean) - 1)), np.zeros(3))
    return clean if np.linalg.norm(area) > 1e-10 else []


def _read(mesh):
    layers = [uv.name for uv in mesh.uv_layers]
    tags = list(mesh["part_tags"])
    tag = mesh.attributes["part_tag"].data
    records = []
    for p in mesh.polygons:
        poly = []
        for li in p.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            normal = mesh.corner_normals[li].vector
            poly.append(np.array([*co, *normal, *(v for uv in mesh.uv_layers for v in uv.data[li].uv)], float))
        records.append((poly, p.material_index, tags[tag[p.index].value]))
    return layers, records


def _write(name, records, materials, layers):
    mesh = bpy.data.meshes.new(name)
    verts, faces, normals, shared = [], [], [], {}
    for poly, _, _ in records:
        face = []
        for v in poly:
            key = tuple(round(float(c), 6) for c in v[:3])
            if key not in shared:
                shared[key] = len(verts)
                verts.append(tuple(v[:3]))
            face.append(shared[key])
        faces.append(tuple(face))
        normals.extend(tuple(v[3:6] / max(np.linalg.norm(v[3:6]), 1e-12)) for v in poly)
    mesh.from_pydata(verts, [], faces)
    used = sorted({mat for _, mat, _ in records})
    for index in used:
        mesh.materials.append(materials[index])
    mesh.polygons.foreach_set("material_index", [used.index(mat) for _, mat, _ in records])
    for i, name in enumerate(layers):
        uv = mesh.uv_layers.new(name=name)
        uv.data.foreach_set("uv", [float(c) for poly, _, _ in records for v in poly for c in v[6 + 2*i:8 + 2*i]])
    mesh.uv_layers.active = mesh.uv_layers["UVMap"]
    tags = sorted({tag for _, _, tag in records})
    mesh["part_tags"] = tags
    attr = mesh.attributes.new("part_tag", "INT", "FACE")
    attr.data.foreach_set("value", [tags.index(tag) for _, _, tag in records])
    mesh.shade_smooth()
    mesh.normals_split_custom_set(normals)
    mesh.update()
    return mesh


def _face(points, material, layers, tag="cargo_liner"):
    n = sum((Vector(a).cross(Vector(b)) for a, b in zip(points, points[1:] + points[:1])), Vector()).normalized()
    return ([np.array([*p, *n, *([0.0] * (2 * len(layers)))]) for p in points], material, tag)


def build(model, body):
    """Return separately hinged doors; leave actual holes, jambs and recessed hold liners in Body."""
    layers, records = _read(body.data)
    materials = list(body.data.materials)
    for name in ("Metal", "DarkMetal", "Dark"):
        if bpy.data.materials[name] not in materials:
            materials.append(bpy.data.materials[name])
    metal, dark_metal, dark = (materials.index(bpy.data.materials[n]) for n in ("Metal", "DarkMetal", "Dark"))
    doors = []
    for definition in definitions(model):
        x, y, z = definition["position"]
        w, h = definition["size"]
        sign = definition["sign"]
        planes = [(1, y - w/2, True), (1, y + w/2, False), (2, z, True), (2, z + h, False)]
        radius = min(0.10, w * 0.12, h * 0.12)
        for degrees in range(30, 360, 30):
            if degrees % 90 == 0:
                continue
            ny, nz = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))
            limit = ny*y + nz*(z + h/2) + abs(ny)*(w/2 - radius) + abs(nz)*(h/2 - radius) + radius
            planes.append((np.array([0, ny, nz]), limit, False))
        tag = "fus_port" if sign > 0 else "fus_stbd"
        remaining, panel = [], []
        for poly, mat, face_tag in records:
            coords = np.array(poly)
            if face_tag != tag or coords[:, 1].max() <= y - w/2 or coords[:, 1].min() >= y + w/2 or \
                    coords[:, 2].max() <= z or coords[:, 2].min() >= z + h:
                remaining.append((poly, mat, face_tag))
                continue
            inside = poly
            for axis, value, greater in planes:
                outside = _clip(inside, axis, value, not greater)
                if outside:
                    remaining.append((outside, mat, face_tag))
                inside = _clip(inside, axis, value, greater)
                if not inside:
                    break
            if inside:
                panel.append((inside, mat, face_tag))
        if not panel:
            raise ValueError(f"{model.NAME}: cargo point {definition['point']} did not intersect the skin")

        # Find the opening's actual polygon boundary, including vertices on the curved fuselage.
        edges = {}
        key = lambda p: tuple(round(float(v), 5) for v in p[:3])
        for poly, _, _ in panel:
            for a, b in zip(poly, poly[1:] + poly[:1]):
                edge = tuple(sorted((key(a), key(b))))
                if edge in edges:
                    del edges[edge]
                else:
                    edges[edge] = (a[:3].copy(), b[:3].copy())
        thickness = min(0.045, h * 0.07)
        depth = definition["holdDepth"]
        inward = np.array([-sign, 0, 0])
        hold = definition["hold"]
        def back_point(p):
            # A usable hold has a flat back plane, independent of the curved skin's overhang above the sill.
            result = p.copy()
            result[0] = x - sign * depth
            return result
        # The door has a painted outside, an inner panel and solid edges; a narrow seal reveals its seam.
        door_records = []
        centre = np.array([x, y, z + h/2])
        gap = min(0.006, w * 0.012)
        def inset(p):
            p = p.copy()
            p[1] = centre[1] + (p[1] - centre[1]) * (1 - 2*gap/w)
            p[2] = centre[2] + (p[2] - centre[2]) * (1 - 2*gap/h)
            return p
        for poly, mat, face_tag in panel:
            outer = [v.copy() for v in poly]
            for v in outer:
                v[:3] = inset(v[:3])
            door_records.append((outer, mat, face_tag))
            door_records.append(_face([v[:3] + inward*thickness for v in outer[::-1]], metal, layers, "cargo_inner"))
        for a, b in edges.values():
            ai, bi = inset(a), inset(b)
            door_records.append(_face([bi, ai, ai + inward*thickness, bi + inward*thickness], dark_metal, layers, "cargo_edge"))
            # Metallic jamb, then a dark-lined tunnel into the hold. Both face the opening.
            c, d = a + inward*0.035, b + inward*0.035
            remaining.append(_face([a, b, d, c], metal, layers))
            side_edge = abs(abs(a[1] - y) - w/2) < 1e-4 and abs(abs(b[1] - y) - w/2) < 1e-4
            if not hold or not side_edge:
                remaining.append(_face([c, d, back_point(b), back_point(a)], dark_metal, layers))
        # Back wall is inset well beyond the sill; the lower liner forms the hold floor.
        back = [[back_point(v[:3]) for v in poly] for poly, _, _ in panel]
        if not hold:
            for poly in back:
                remaining.append(_face(poly, dark, layers))
        else:
            y0, y1 = (station - model.S_REF for station in hold)
            inner = abs(back_point(np.array([x, y, z]))[0])
            out_x, back_x = sign * inner, -sign * inner
            def room_face(points, material=dark_metal):
                remaining.append(_face(points if sign < 0 else points[::-1], material, layers))
            floor = [[out_x, y0, z], [back_x, y0, z], [back_x, y1, z], [out_x, y1, z]]
            room_face(floor)
            room_face([[p[0], p[1], z+h] for p in floor[::-1]], dark)
            room_face([[back_x, y0, z+h], [back_x, y1, z+h], [back_x, y1, z], [back_x, y0, z]], dark)
            end = [[out_x, y0, z], [out_x, y0, z+h], [back_x, y0, z+h], [back_x, y0, z]]
            room_face(end)
            room_face([[p[0], y1, p[2]] for p in end[::-1]])
            for start, stop in ((y0, y-w/2), (y+w/2, y1)):
                room_face([[out_x, start, z], [out_x, stop, z], [out_x, stop, z+h], [out_x, start, z+h]])
            for station in np.arange(y0 + .4, y1, .6):
                room_face([[out_x, station-.018, z+.004], [back_x, station-.018, z+.004],
                           [back_x, station+.018, z+.004], [out_x, station+.018, z+.004]], metal)
        pivot = np.array(definition["pivot"])
        for poly, _, _ in door_records:
            for v in poly:
                v[:3] -= pivot
        mesh = _write(definition["transform"], door_records, materials, layers)
        door = bpy.data.objects.new(definition["transform"], mesh)
        body.users_collection[0].objects.link(door)
        door.parent = body.parent
        door.location = pivot
        doors.append(door)
        records = remaining
        print("CARGO DOOR", model.NAME, definition["point"], "faces", len(panel), "boundary", len(edges))
    body.data = _write("Body", records, materials, layers)
    bpy.context.view_layer.update()
    return doors
