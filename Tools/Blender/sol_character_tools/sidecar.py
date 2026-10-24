"""Builds and writes the character sidecar Unity reads next to the FBX.

Positions and offsets are in the armature's own axes, scaled to metres. Unity
matches the exported rest bone heads against its imported bones to find the
axis mapping, so the exporter does not need to know the FBX axis settings.
"""

import json
import os

import bpy
import numpy as np

from . import checks, joints, scene

FORMAT = "sol-character-sidecar"
FORMAT_VERSION = 2
GENERATOR = "sol_character_tools 1.0"


def build(blender_scene=None, armature_name=None, body_name=None, config=None, compare_with=None):
    blender_scene = blender_scene or bpy.context.scene
    character = scene.find_character(blender_scene, armature_name, body_name)
    bones, offsets, solver = joints.compute_joint_offsets(character)

    compare_shapes = None
    if compare_with:
        with open(compare_with, encoding="utf-8") as handle:
            other = json.load(handle)
        compare_shapes = [shape["name"] for shape in other["shapes"] if other["body"] in shape.get("meshes", [])]

    findings = checks.run_checks(character, solver, config, compare_shapes)
    return {
        "format": FORMAT,
        "version": FORMAT_VERSION,
        "generator": GENERATOR,
        "blender": bpy.app.version_string,
        "source": os.path.basename(bpy.data.filepath) if bpy.data.filepath else None,
        "armature": character.armature.name,
        "body": character.body.name,
        "bodyVertexCount": character.body.count,
        "proxies": [proxy.name for proxy in character.proxies],
        "space": "armature-metres",
        "bones": [_bone_entry(character, solver, name) for name in bones],
        "shapes": [
            {
                "name": name,
                "meshes": [mesh.name for mesh in character.meshes if name in mesh.shapes],
                "boneIndices": [index for index, _ in offsets[name]],
                "offsets": [component for _, offset in offsets[name] for component in _round(offset)],
            }
            for name in character.shape_names()
        ],
        "checks": findings,
    }


def write(data, path):
    directory = os.path.dirname(os.path.abspath(path))
    os.makedirs(directory, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(data, handle, indent=1, default=_json_default)
        handle.write("\n")


def summary(data):
    counts = {"error": 0, "warning": 0, "info": 0}
    for item in data["checks"]:
        counts[item["severity"]] += 1
    moved = sum(1 for shape in data["shapes"] if shape["boneIndices"])
    lines = [
        f"{data['body']}: {len(data['bones'])} joints, {moved}/{len(data['shapes'])} shapes move joints",
        f"Checks: {counts['error']} errors, {counts['warning']} warnings, {counts['info']} info",
    ]
    lines += [f"  [{item['severity']}] {item['code']}: {item['message']}" for item in data["checks"]]
    return "\n".join(lines)


def _bone_entry(character, solver, name):
    info = character.bones[name]
    detail = solver.details[name]
    return {
        "name": name,
        "parent": info["parent"],
        "head": _round(info["head"]),
        "method": detail["method"],
        "bandPartner": detail["partner"],
        "stencilVertices": detail["vertices"],
        "stencilHeadDistance": round(detail["headDistance"], 6),
    }


def _round(vector):
    return [round(float(component), 6) for component in vector]


def _json_default(value):
    if isinstance(value, np.generic):
        return value.item()
    if isinstance(value, np.ndarray):
        return value.tolist()
    raise TypeError(f"Cannot serialise {type(value).__name__}")
