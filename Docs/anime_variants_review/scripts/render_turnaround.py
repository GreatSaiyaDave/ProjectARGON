"""Turnaround of a Meshy GLB: 4 yaw angles, twice — as delivered (PBR) and flattened to the painted
base colour (the "2D art in 3D" look the game targets). Blender headless:

    blender -b -P render_turnaround.py -- <model.glb> <outdir>
"""
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
GLB, OUT = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]

mn = Vector((1e9, 1e9, 1e9))
mx = Vector((-1e9, -1e9, -1e9))
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        mn = Vector((min(mn[i], w[i]) for i in range(3)))
        mx = Vector((max(mx[i], w[i]) for i in range(3)))
center, size = (mn + mx) / 2, mx - mn
height = max(size.z, 1e-3)
print(f"[turn] bounds {tuple(round(v, 3) for v in mn)} .. {tuple(round(v, 3) for v in mx)} · height {height:.3f}")

scene = bpy.context.scene
for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try:
        scene.render.engine = engine
        break
    except Exception:
        continue
scene.render.resolution_x, scene.render.resolution_y = 512, 768
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"

world = bpy.data.worlds.new("W")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.25, 0.25, 0.28, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.55

sun = bpy.data.lights.new("Key", type="SUN")
sun.energy = 3.0
key = bpy.data.objects.new("Key", sun)
scene.collection.objects.link(key)
key.rotation_euler = (math.radians(62), 0.0, math.radians(-38))

cam_data = bpy.data.cameras.new("Cam")
cam_data.lens = 62
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

VIEWS = [("front", 0), ("34", 45), ("side", 90), ("back", 180)]
dist = height * 2.5


def look_at(obj, target):
    obj.rotation_euler = (obj.location - target).to_track_quat("Z", "Y").to_euler()


def render(tag):
    for name, yaw in VIEWS:
        a = math.radians(yaw)
        cam.location = center + Vector((math.sin(a) * dist, -math.cos(a) * dist, height * 0.10))
        look_at(cam, center + Vector((0, 0, height * 0.02)))
        scene.render.filepath = f"{OUT}/turn_{tag}_{name}.png"
        bpy.ops.render.render(write_still=True)


render("pbr")

# flatten: every material becomes an emission of its own base-colour texture — the painted art, lit only
# by its own pixels, which is what the game's cel shader does with the sprite
for o in meshes:
    for slot in o.material_slots:
        m = slot.material
        if m is None or not m.use_nodes:
            continue
        img = next((n.image for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None)
        nm = bpy.data.materials.new("flat_" + m.name)
        nm.use_nodes = True
        nt = nm.node_tree
        nt.nodes.clear()
        em = nt.nodes.new("ShaderNodeEmission")
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(em.outputs[0], out.inputs[0])
        if img is not None:
            tx = nt.nodes.new("ShaderNodeTexImage")
            tx.image = img
            nt.links.new(tx.outputs[0], em.inputs[0])
        else:
            em.inputs[0].default_value = (0.75, 0.75, 0.78, 1)
        slot.material = nm
render("flat")
print("[turn] done")
