"""Extract the selected RainyCorner detail meshes using Blender in background mode."""

import json
import hashlib
import math
import pathlib
import sys

import bpy
from mathutils import Matrix, Vector


def starts(name, *prefixes):
    return name.startswith(prefixes)


def classify(obj):
    name = obj.name
    center = sum((Vector(corner) for corner in obj.bound_box), Vector()) / 8
    center = obj.matrix_world @ center
    x = center.x
    if starts(name, "Vending", "RC Vending", "Coin slot", "Coin return") and -3.94 < x < -3:
        if not starts(name, "Vending cold drink sign"):
            return "vending"
    if starts(name, "Gondola shelf", "Shelf upright", "Shelf price strip") and -.2 < x < 1.4:
        return "shelf"
    if starts(name, "Refrigerated shelf") and .9 < x < 1.2 and .4 < center.z < .5:
        return "fridgeRack"
    if starts(name, "Fridge door handle") and 1.4 < x < 1.6:
        return "fridgeHandle"
    if starts(name, "Cash counter", "POS ", "Coffee "):
        return "counter"
    if starts(name, "Outdoor AC condenser", "AC grille slat", "AC fan concentric", "RC AC grounded", "RC AC isolation"):
        return "ac"
    if starts(name, "Street lamp ", "Streetlamp head", "Amber streetlamp diffuser", "RC Streetlamp ground flange"):
        return "lamp"
    if starts(name, "RC Bicycle"):
        return "bike"
    return None


def keep(obj, category, counts):
    name = obj.name.split(".")[0]
    index = counts.get(name, 0)
    counts[name] = index + 1
    if category == "bike":
        if any(term in name for term in ("chain link", "roller and rivet", "nut", "bearing", "tire valve", "rim brake track")):
            return False
        if "laced spoke" in name or "basket" in name:
            return index % 4 == 0
    if category == "vending" and name == "Vending drink":
        return index % 2 == 0
    if category == "ac" and "safety grille" in name:
        return index % 2 == 0
    return True


def triangle_budget(obj):
    name = obj.name
    if "tire" in name or "rim" in name or "safety grille" in name:
        return 260
    if "curved" in name:
        return 220
    return 80


def surface(material):
    node = next((node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
    if node is None:
        return {"roughness": .72, "metalness": 0}
    return {"roughness": node.inputs["Roughness"].default_value, "metalness": node.inputs["Metallic"].default_value}


def extract():
    scene = bpy.context.scene
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bike_basis = scene.objects["RC Bicycle complete assembly"].matrix_world.inverted()
    # (Blender x, y, z) -> right-handed Three.js (x, z, -y).
    y_up = Matrix(((1., 0., 0., 0.), (0., 0., 1., 0.),
                   (0., -1., 0., 0.), (0., 0., 0., 1.)))
    source_triangles = {}
    exported_triangles = {}
    groups = {}
    counts = {}
    for obj in sorted(scene.objects, key=lambda item: item.name):
        category = classify(obj)
        if not category or obj.type not in {"MESH", "CURVE"}:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        triangles = len(mesh.loop_triangles)
        source_triangles[category] = source_triangles.get(category, 0) + triangles
        if not keep(obj, category, counts):
            evaluated.to_mesh_clear()
            continue

        budget = triangle_budget(obj)
        temporary = None
        mesh_copy = None
        if triangles > budget:
            mesh_copy = mesh.copy()
            temporary = bpy.data.objects.new("Spectrum extraction", mesh_copy)
            scene.collection.objects.link(temporary)
            modifier = temporary.modifiers.new("Preserve silhouette at diorama scale", "DECIMATE")
            modifier.ratio = max(.03, budget / triangles)
            evaluated.to_mesh_clear()
            depsgraph.update()
            evaluated = temporary.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            mesh.calc_loop_triangles()

        transform = y_up @ obj.matrix_world
        if category == "bike":
            transform = y_up @ bike_basis @ obj.matrix_world
        if category == "ac":
            transform = Matrix.Rotation(-math.pi / 2, 4, "Y") @ transform
        if category == "lamp":
            transform = Matrix.Rotation(math.pi, 4, "Y") @ transform
        normal_transform = transform.to_3x3().inverted_safe().transposed()
        buckets = groups.setdefault(category, {})
        positions = [transform @ vertex.co for vertex in mesh.vertices]
        for triangle in mesh.loop_triangles:
            material = mesh.materials[triangle.material_index]
            name = material.name
            bucket = buckets.get(name)
            if bucket is None:
                bucket = {"positions": [], "normals": [], "indices": [], "lookup": {}, "surface": surface(material)}
                buckets[name] = bucket
            exported_triangles[category] = exported_triangles.get(category, 0) + 1
            for loop, index in zip(triangle.loops, triangle.vertices):
                point = positions[index]
                column = (normal_transform @ mesh.corner_normals[loop].vector).normalized()
                key = tuple(round(float(value), 5) for value in (*point, *column))
                vertex = bucket["lookup"].get(key)
                if vertex is None:
                    vertex = len(bucket["positions"]) // 3
                    bucket["lookup"][key] = vertex
                    bucket["positions"].extend(key[:3])
                    bucket["normals"].extend(key[3:])
                bucket["indices"].append(vertex)
        evaluated.to_mesh_clear()
        if temporary is not None:
            bpy.data.objects.remove(temporary, do_unlink=True)
            bpy.data.meshes.remove(mesh_copy)

    for buckets in groups.values():
        for bucket in buckets.values():
            del bucket["lookup"]
    return {
        "groups": groups,
        "sourceTriangles": source_triangles,
        "exportedTriangles": exported_triangles,
        "blenderVersion": bpy.app.version_string,
    }


if __name__ == "__main__":
    output = pathlib.Path(sys.argv[sys.argv.index("--") + 1])
    expected = "c5855948c62434784deb3c7bf817a6ade98f81815093b79b00b0862ef4f4e01b"
    with pathlib.Path(bpy.data.filepath).open("rb") as input_stream:
        if hashlib.file_digest(input_stream, "sha256").hexdigest() != expected:
            raise RuntimeError("RainyCorner.blend does not match the pinned source.")
    result = extract()
    output.write_text(json.dumps(result, separators=(",", ":")), encoding="utf8")
    print(json.dumps({key: result[key] for key in ("sourceTriangles", "exportedTriangles", "blenderVersion")}))
