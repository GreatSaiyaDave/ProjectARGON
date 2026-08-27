#!/usr/bin/env python3
"""
WRLDZ salvage optical bench — S23 Ultra tray + 50 mm lens + 45° combiner slot.

Units: millimetres. Emits STL via Blender (background).

  ~/.local/bin/blender --background --python Tools/WRLDZ/salvage/optical_bench.py

Output: Tools/WRLDZ/salvage/ost_optical_bench.stl
"""
from __future__ import annotations

import math
import os
import sys

# Blender's python, or generate a .blend-less mesh via bpy.
try:
    import bpy
    import bmesh
    from mathutils import Vector
except ImportError:
    sys.stderr.write("Run this with Blender:\n  blender --background --python optical_bench.py\n")
    sys.exit(1)

# Galaxy S23 Ultra body (mm) + slop
PHONE_L = 163.4
PHONE_W = 78.1
PHONE_T = 8.9
SLOP = 2.0
WALL = 3.0

LENS_FOCAL = 50.0
LENS_D = 36.0
COMBINER_W = 70.0
COMBINER_H = 50.0
COMBINER_T = 2.2
EYE_RELIEF = 22.0

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ost_optical_bench.stl")


def box(name, sx, sy, sz, loc):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    ob = bpy.context.active_object
    ob.name = name
    ob.scale = (sx, sy, sz)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return ob


def cylinder(name, radius, depth, loc, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(
        radius=radius, depth=depth, location=loc, rotation=rot, vertices=48)
    ob = bpy.context.active_object
    ob.name = name
    return ob


def boolean_diff(host, cutter):
    mod = host.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    mod.solver = "EXACT"
    bpy.context.view_layer.objects.active = host
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def mm_to_blender(mm):
    # Blender default unit = metre
    return mm * 0.001


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    inner_l = PHONE_L + SLOP
    inner_w = PHONE_W + SLOP
    tray_l = inner_l + WALL * 2
    tray_w = inner_w + WALL * 2
    tray_h = PHONE_T + WALL + 4.0

    # Scale everything in mm then shrink at export via scene unit
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 0.001

    # Work in millimetres numerically, place in metres
    def P(x, y, z):
        return (x * 0.001, y * 0.001, z * 0.001)

    def Sx(x, y, z):
        return (x * 0.001, y * 0.001, z * 0.001)

    # Base plate
    base = box("Base", tray_l * 0.001, (tray_w + 90) * 0.001, 4 * 0.001,
               P(0, 20, 2))

    # Phone tray (open bottom so screen faces the lens)
    tray = box("Tray", tray_l * 0.001, tray_w * 0.001, tray_h * 0.001,
               P(0, 0, 4 + tray_h / 2))
    pocket = box("Pocket", inner_l * 0.001, inner_w * 0.001, (tray_h + 2) * 0.001,
                 P(0, 0, 4 + WALL + tray_h / 2))
    boolean_diff(tray, pocket)

    # USB-C notch (crown / +X)
    notch = box("UsbNotch", 12 * 0.001, 14 * 0.001, 10 * 0.001,
                P(tray_l / 2 - 4, 0, 4 + PHONE_T / 2 + WALL))
    boolean_diff(tray, notch)

    # Lens baffle under the tray, hole Ø lens
    baffle_z = 4 - LENS_FOCAL + 8
    baffle = box("LensBaffle", 80 * 0.001, 80 * 0.001, 3 * 0.001,
                 P(0, 0, baffle_z))
    hole = cylinder("LensHole", (LENS_D / 2 + 0.6) * 0.001, 6 * 0.001,
                    P(0, 0, baffle_z), rot=(0, 0, 0))
    boolean_diff(baffle, hole)

    # Combiner slot: 45° kerf in a block in front of the lens
    slot_z = baffle_z - 18
    slot_block = box("CombinerBlock", 90 * 0.001, 16 * 0.001, 70 * 0.001,
                     P(0, 42, slot_z))
    slot_block.rotation_euler = (math.radians(45), 0, 0)
    bpy.context.view_layer.objects.active = slot_block
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    kerf = box("Kerf", (COMBINER_W + 2) * 0.001, (COMBINER_T + 0.4) * 0.001,
               (COMBINER_H + 4) * 0.001, slot_block.location)
    kerf.rotation_euler = (math.radians(45), 0, 0)
    bpy.context.view_layer.objects.active = kerf
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    boolean_diff(slot_block, kerf)

    # Join
    bpy.ops.object.select_all(action="DESELECT")
    for name in ("Base", "Tray", "LensBaffle", "CombinerBlock"):
        ob = bpy.data.objects.get(name)
        if ob:
            ob.select_set(True)
            bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()
    body = bpy.context.active_object
    body.name = "OstOpticalBench"

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.wm.stl_export(filepath=OUT, export_selected_objects=True)
    print("WROTE", OUT)


if __name__ == "__main__":
    main()
