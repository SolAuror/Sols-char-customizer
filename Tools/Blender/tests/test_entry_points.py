import json
import os

import bpy
import pytest

import sol_character_tools
import sol_export_cli
from conftest import character_fbx


def test_addon_registers_and_unregisters(empty_scene):
    sol_character_tools.register()
    try:
        assert hasattr(bpy.types.Scene, "sol_character")
        assert hasattr(bpy.ops.sol, "export_character_sidecar")
    finally:
        sol_character_tools.unregister()
    assert not hasattr(bpy.types.Scene, "sol_character")


def test_cli_writes_a_sidecar_from_an_fbx(tmp_path):
    fbx = character_fbx("SK_Human_F.fbx")
    if not os.path.exists(fbx):
        pytest.skip("SK_Human_F.fbx is not in this checkout")
    out = tmp_path / "SK_Human_F.solrig"

    exit_code = sol_export_cli.main(["sol_export_cli.py", "--", "--fbx", fbx, "--out", str(out)])

    data = json.loads(out.read_text(encoding="utf-8"))
    assert exit_code == 0
    assert data["format"] == "sol-character-sidecar"
    assert data["source"] == "SK_Human_F.fbx"
    assert len(data["shapes"]) == 75
    teeth_only = next(shape for shape in data["shapes"] if shape["name"] == "Teeth_Fangs_Vamp")
    assert data["body"] not in teeth_only["meshes"]
