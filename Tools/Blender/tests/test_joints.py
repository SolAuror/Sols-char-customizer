import numpy as np

from builders import add_shape, build_arm, skinned_mesh, tube
from sol_character_tools import joints, scene


def offsets_by_bone(character):
    bones, offsets, solver = joints.compute_joint_offsets(character)
    return {shape: {bones[index]: offset for index, offset in moved} for shape, moved in offsets.items()}, solver


def test_uniform_translation_moves_every_joint_equally(empty_scene):
    rig, body = build_arm(empty_scene)
    add_shape(body, "Grow+", lambda index, position: (0.0, 0.02, 0.0))

    offsets, _ = offsets_by_bone(scene.find_character(empty_scene))

    for bone in ("upper", "lower", "lower_twist", "hand", "upper_twist"):
        assert np.allclose(offsets["Grow+"][bone], (0.0, 0.02, 0.0), atol=1e-6), bone


def test_elbow_shape_moves_only_the_elbow_joint(empty_scene):
    rig, body = build_arm(empty_scene)
    add_shape(body, "Elbow+", lambda index, position: (0.0, 0.0, 0.01) if 0.75 <= position[0] <= 1.25 else (0.0, 0.0, 0.0))

    offsets, solver = offsets_by_bone(scene.find_character(empty_scene))

    assert solver.details["lower"]["method"] == "band"
    assert solver.details["lower"]["partner"] == "upper"
    assert np.allclose(offsets["Elbow+"]["lower"], (0.0, 0.0, 0.01), atol=1e-6)
    assert "hand" not in offsets["Elbow+"]


def test_wrist_band_uses_the_twist_sibling(empty_scene):
    build_arm(empty_scene)

    _, solver = offsets_by_bone(scene.find_character(empty_scene))

    assert solver.details["hand"]["method"] == "band"
    assert solver.details["hand"]["partner"] == "lower_twist"
    assert solver.details["hand"]["headDistance"] < 0.05


def test_twist_on_the_parent_head_shares_the_parents_joint(empty_scene):
    build_arm(empty_scene)

    _, solver = offsets_by_bone(scene.find_character(empty_scene))

    assert solver.details["upper_twist"]["method"] == "colocated"


def test_proxy_shapes_move_the_bones_that_skin_them(empty_scene):
    rig, body = build_arm(empty_scene)
    vertices, faces = tube(2.3, 2.5, 2, 0.05)
    ring = skinned_mesh(empty_scene, "Ring", rig, vertices, faces, lambda position: {"ring": 1.0})
    rig_data = rig.data
    import bpy
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    bone = rig_data.edit_bones.new("ring")
    bone.head, bone.tail = (2.4, 0.0, 0.0), (2.5, 0.0, 0.0)
    bone.parent = rig_data.edit_bones["hand"]
    bpy.ops.object.mode_set(mode="OBJECT")
    add_shape(ring, "RingSize+", lambda index, position: (0.0, 0.0, 0.005))

    offsets, solver = offsets_by_bone(scene.find_character(empty_scene))

    assert solver.details["ring"]["method"] == "region"
    assert np.allclose(offsets["RingSize+"]["ring"], (0.0, 0.0, 0.005), atol=1e-6)


def test_armature_scale_is_converted_to_metres(empty_scene):
    rig, body = build_arm(empty_scene)
    rig.scale = (0.01, 0.01, 0.01)
    empty_scene.view_layers[0].update()
    add_shape(body, "Grow+", lambda index, position: (0.0, 2.0, 0.0))

    character = scene.find_character(empty_scene)
    offsets, _ = offsets_by_bone(character)

    assert np.allclose(character.bones["hand"]["head"], (0.02, 0.0, 0.0), atol=1e-9)
    assert np.allclose(offsets["Grow+"]["hand"], (0.0, 0.02, 0.0), atol=1e-6)
