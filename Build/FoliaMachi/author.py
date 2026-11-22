"""Build the original street and vehicle meshes in Blender, in Folia's Y-up metres."""
import base64
from array import array
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector


def z_up(p):
    return (p[0], -p[2], p[1])


def linear(byte):
    value = byte / 255
    return value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4


class Kit:
    def __init__(self, name):
        self.collection = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(self.collection)
        self.materials = {}

    def mat(self, name, srgb_hex, roughness, metallic=0, emission=None, kind=0):
        rgb = [linear(c) for c in bytes.fromhex(srgb_hex.lstrip("#"))]
        key = (name, srgb_hex, roughness, metallic, tuple(emission or ()), kind)
        if key in self.materials:
            return self.materials[key]
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        node = mat.node_tree.nodes.get("Principled BSDF")
        node.inputs["Base Color"].default_value = (*rgb, 1)
        node.inputs["Roughness"].default_value = roughness
        node.inputs["Metallic"].default_value = metallic
        mat.diffuse_color = (*rgb, 1)
        if emission:
            node.inputs["Emission Color"].default_value = (*emission, 1)
            node.inputs["Emission Strength"].default_value = 1
        if kind == 13:
            node.inputs["Coat Weight"].default_value = 1
            node.inputs["Coat Roughness"].default_value = .09
        elif kind == 1:
            node.inputs["Transmission Weight"].default_value = 1
            node.inputs["IOR"].default_value = 1.5
        mat["machi"] = json.dumps(dict(name=name, color=rgb, roughness=roughness,
                                       metalness=metallic, emission=emission, kind=11 if emission else kind))
        self.materials[key] = mat
        return mat

    def mesh(self, name, verts, faces, mat, smooth=False):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([z_up(v) for v in verts], [], faces)
        mesh.update()
        mesh.materials.append(mat)
        if smooth:
            for face in mesh.polygons:
                face.use_smooth = True
        obj = bpy.data.objects.new(name, mesh)
        self.collection.objects.link(obj)
        return obj

    def box(self, name, center, size, mat, bevel=0, segments=2):
        x, y, z = (s * .5 for s in size)
        verts = [(-x,-y,-z), (x,-y,-z), (x,y,-z), (-x,y,-z),
                 (-x,-y,z), (x,-y,z), (x,y,z), (-x,y,z)]
        faces = [(3,2,1,0), (4,5,6,7), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7)]
        obj = self.mesh(name, verts, faces, mat)
        obj.location = z_up(center)
        if bevel > 0:
            mod = obj.modifiers.new("Edge radius", "BEVEL")
            mod.width = min(bevel, min(size) * .48)
            mod.segments = segments
        return obj

    def rod(self, name, start, end, radius, mat, sides=12):
        a, b = Vector(start), Vector(end)
        axis = (b - a).normalized()
        radial = axis.orthogonal().normalized()
        tangent = axis.cross(radial)
        ring = [radius * (radial * math.cos(i * math.tau / sides) +
                          tangent * math.sin(i * math.tau / sides)) for i in range(sides)]
        verts = [p + offset for p in (a, b) for offset in ring]
        faces = [(i, (i+1) % sides, (i+1) % sides + sides, i+sides) for i in range(sides)]
        faces.extend([tuple(reversed(range(sides))), tuple(range(sides, 2*sides))])
        obj = self.mesh(name, verts, faces, mat)
        for face in list(obj.data.polygons)[:sides]:
            face.use_smooth = True
        # Keep hard cap rims, including for smooth corner normals along the tube.
        for edge in obj.data.edges:
            a, b = edge.vertices
            edge.use_edge_sharp = (a < sides) == (b < sides)
        return obj


def encode(values):
    if sys.byteorder != "little":
        values.byteswap()
    return base64.b64encode(values.tobytes()).decode("ascii")


def pack(collection):
    deps = bpy.context.evaluated_depsgraph_get()
    batches = {}
    for obj in collection.all_objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(deps)
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            world = obj.matrix_world
            normal_matrix = world.to_3x3().inverted().transposed()
            reverse = world.to_3x3().determinant() < 0
            positions = [world @ v.co for v in mesh.vertices]
            normals = [(normal_matrix @ n.vector).normalized() for n in mesh.corner_normals]
            for face in mesh.loop_triangles:
                if face.area < 1e-12:
                    continue
                mat = mesh.materials[face.material_index]
                if mat not in batches:
                    batches[mat] = ({}, array('f'), array('I'))
                lookup, vertices, indices = batches[mat]
                for loop in (reversed(face.loops) if reverse else face.loops):
                    p = positions[mesh.loops[loop].vertex_index]
                    n = normals[loop]
                    vertex = tuple(round(c, 6) for c in (p.x,p.z,-p.y,n.x,n.z,-n.y))
                    index = lookup.get(vertex)
                    if index is None:
                        index = len(lookup)
                        lookup[vertex] = index
                        vertices.extend(vertex)
                    indices.append(index)
        finally:
            evaluated.to_mesh_clear()
    return [dict(material=json.loads(mat["machi"]), vertices=encode(vertices), indices=encode(indices))
            for mat, (_, vertices, indices) in batches.items()]


SCENES = ("konbini", "station", "kowloon")
VEHICLES = ("traffic", "sedan", "taxi", "kei", "ad-truck", "scooter")


def export(model_id, directory):
    """Callable from Blender MCP. Only the newly created collection is exported."""
    import fixtures
    import neighborhood
    import vehicles

    kit = Kit("Machi " + model_id)
    if model_id in SCENES:
        neighborhood.build(model_id, kit)
        fixtures.build(model_id, kit)
    elif model_id == "window-plant":
        neighborhood.window_plant(kit)
    elif model_id in VEHICLES:
        vehicles.build(model_id, kit)
    else:
        raise ValueError(model_id)
    batches = pack(kit.collection)
    out = Path(directory)
    out.mkdir(parents=True, exist_ok=True)
    (out / (model_id + ".json")).write_text(json.dumps(batches, separators=(",", ":")), encoding="utf8")
    print(model_id, len(kit.collection.objects), "objects,", len(batches), "materials")


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    directory = sys.argv[sys.argv.index("--") + 1]
    for model_id in (*SCENES, *VEHICLES, "window-plant"):
        export(model_id, directory)
