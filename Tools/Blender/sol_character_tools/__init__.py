"""Sol Character Tools: exports joint offsets and runs pre-export checks.

Install from Edit > Preferences > Add-ons > Install from Disk with a zip of this
folder, or run headless through Tools/Blender/sol_export_cli.py.
"""

bl_info = {
    "name": "Sol Character Tools",
    "author": "Sol Auror",
    "version": (1, 0, 0),
    "blender": (4, 2, 0),
    "location": "3D Viewport > Sidebar > Sol",
    "description": "Exports per-shape joint offsets for the Unity character customizer and checks shape keys and skin weights.",
    "category": "Import-Export",
}

import json
import os

import bpy

from . import checks, joints, scene, sidecar

REPORT_TEXT_NAME = "Sol Character Checks"


def _load_config(path):
    if not path:
        return None
    resolved = bpy.path.abspath(path)
    with open(resolved, encoding="utf-8") as handle:
        return json.load(handle)


def _publish_report(data):
    text = bpy.data.texts.get(REPORT_TEXT_NAME) or bpy.data.texts.new(REPORT_TEXT_NAME)
    text.clear()
    text.write(sidecar.summary(data) + "\n")


class SolCharacterSettings(bpy.types.PropertyGroup):
    output_path: bpy.props.StringProperty(
        name="Sidecar",
        description="Where to write the .solrig file, normally next to the exported FBX in the Unity project",
        subtype="FILE_PATH",
        default="//character.solrig",
    )
    config_path: bpy.props.StringProperty(
        name="Check Config",
        description="Optional JSON listing intentional exceptions (see Tools/Blender/character_checks.json)",
        subtype="FILE_PATH",
    )
    compare_path: bpy.props.StringProperty(
        name="Compare With",
        description="Optional .solrig of the other sex, to report shape keys that differ",
        subtype="FILE_PATH",
    )


class SOL_OT_run_checks(bpy.types.Operator):
    bl_idname = "sol.run_character_checks"
    bl_label = "Run Checks"
    bl_description = "Check shape keys, proxies, and skin weights without writing anything"

    def execute(self, context):
        return _run(self, context, write_file=False)


class SOL_OT_export_sidecar(bpy.types.Operator):
    bl_idname = "sol.export_character_sidecar"
    bl_label = "Export Sidecar"
    bl_description = "Compute joint offsets per shape key, run checks, and write the .solrig file"

    def execute(self, context):
        return _run(self, context, write_file=True)


def _run(operator, context, write_file):
    settings = context.scene.sol_character
    try:
        compare = bpy.path.abspath(settings.compare_path) if settings.compare_path else None
        data = sidecar.build(context.scene, config=_load_config(settings.config_path), compare_with=compare)
    except (LookupError, OSError, ValueError) as error:
        operator.report({"ERROR"}, str(error))
        return {"CANCELLED"}

    _publish_report(data)
    errors = sum(1 for item in data["checks"] if item["severity"] == "error")
    warnings = sum(1 for item in data["checks"] if item["severity"] == "warning")
    if write_file:
        path = bpy.path.abspath(settings.output_path)
        sidecar.write(data, path)
        operator.report({"INFO"}, f"Wrote {os.path.basename(path)}: {errors} errors, {warnings} warnings. "
                                  f"See the '{REPORT_TEXT_NAME}' text.")
    else:
        operator.report({"INFO"}, f"{errors} errors, {warnings} warnings. See the '{REPORT_TEXT_NAME}' text.")
    return {"FINISHED"}


class SOL_PT_character_panel(bpy.types.Panel):
    bl_label = "Sol Character"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Sol"

    def draw(self, context):
        settings = context.scene.sol_character
        layout = self.layout
        layout.prop(settings, "output_path")
        layout.prop(settings, "config_path")
        layout.prop(settings, "compare_path")
        row = layout.row()
        row.operator(SOL_OT_run_checks.bl_idname, icon="CHECKMARK")
        row.operator(SOL_OT_export_sidecar.bl_idname, icon="EXPORT")


_classes = (SolCharacterSettings, SOL_OT_run_checks, SOL_OT_export_sidecar, SOL_PT_character_panel)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.sol_character = bpy.props.PointerProperty(type=SolCharacterSettings)


def unregister():
    del bpy.types.Scene.sol_character
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
