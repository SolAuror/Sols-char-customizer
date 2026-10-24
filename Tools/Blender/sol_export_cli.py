"""Headless runner for the Sol character exporter and checks.

From a .blend (normal use):
    blender -b Human_F.blend --python Tools/Blender/sol_export_cli.py -- \
        --out Assets/CharCustomization/RigData/SK_Human_F.solrig

From an exported FBX, with Blender or the bpy Python module:
    python Tools/Blender/sol_export_cli.py --fbx Assets/CharCustomization/Resources/SK_Human_F.fbx \
        --out Assets/CharCustomization/RigData/SK_Human_F.solrig

Exit code: 0 clean, 1 checks found errors, 2 the character could not be read.
"""

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from sol_character_tools import sidecar  # noqa: E402

DEFAULT_CONFIG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "character_checks.json")


def parse_arguments(argv):
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = argv[1:]
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--fbx", help="Import this FBX into an empty scene instead of using the open .blend.")
    parser.add_argument("--out", help="Write the .solrig sidecar here. Omit to only run checks.")
    parser.add_argument("--config", default=DEFAULT_CONFIG, help="Check exceptions JSON (default: %(default)s).")
    parser.add_argument("--compare-with", help="Report shape keys that differ from this other .solrig.")
    parser.add_argument("--armature", help="Armature object name, when the scene has several.")
    parser.add_argument("--body", help="Body mesh name, when the largest deformed mesh is not the body.")
    return parser.parse_args(argv)


def main(argv):
    arguments = parse_arguments(argv)
    if arguments.fbx:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=os.path.abspath(arguments.fbx))

    config = None
    if arguments.config and os.path.exists(arguments.config):
        with open(arguments.config, encoding="utf-8") as handle:
            config = json.load(handle)

    try:
        data = sidecar.build(
            bpy.context.scene,
            armature_name=arguments.armature,
            body_name=arguments.body,
            config=config,
            compare_with=arguments.compare_with)
    except LookupError as error:
        print(f"sol_export: {error}", file=sys.stderr)
        return 2

    if arguments.fbx:
        data["source"] = os.path.basename(arguments.fbx)
    if arguments.out:
        sidecar.write(data, arguments.out)
        print(f"Wrote {arguments.out}")
    print(sidecar.summary(data))
    return 1 if any(item["severity"] == "error" for item in data["checks"]) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
