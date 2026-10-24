"""Runs against the exported characters in the Unity project, when present."""

import os

import bpy
import numpy as np
import pytest

from conftest import character_fbx
from sol_character_tools import sidecar


def load(name):
    path = character_fbx(name)
    if not os.path.exists(path):
        pytest.skip(f"{name} is not in this checkout")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    return sidecar.build(bpy.context.scene)


@pytest.fixture(scope="module")
def female():
    return load("SK_Human_F.fbx")


@pytest.fixture(scope="module")
def male():
    return load("SK_Human_M.fbx")


def offset(data, shape, bone):
    names = [entry["name"] for entry in data["bones"]]
    entry = next(item for item in data["shapes"] if item["name"] == shape)
    for position, index in enumerate(entry["boneIndices"]):
        if names[index] == bone:
            return np.array(entry["offsets"][position * 3:position * 3 + 3])
    return np.zeros(3)


def test_body_is_the_makehuman_mesh(female, male):
    assert female["bodyVertexCount"] == 13380
    assert male["bodyVertexCount"] == 13380


def test_height_raises_joints_more_the_higher_they_are(female):
    head = np.linalg.norm(offset(female, "Body_Height+", "head.x"))
    hips = np.linalg.norm(offset(female, "Body_Height+", "root.x"))
    knee = np.linalg.norm(offset(female, "Body_Height+", "leg_stretch.l"))
    assert head > hips > knee > 0.005


def test_eye_spacing_moves_the_eyes_apart(female):
    left = offset(female, "Head_Eyes_Distance+", "c_eye.l")
    right = offset(female, "Head_Eyes_Distance+", "c_eye.r")
    lateral = int(np.argmax(np.abs(left)))
    assert np.sign(left[lateral]) == -np.sign(right[lateral])
    assert abs(left[lateral]) > 0.001


def test_limb_joints_resolve_to_their_bands(female):
    details = {entry["name"]: entry for entry in female["bones"]}
    for bone in ("forearm_stretch.l", "hand.l", "leg_stretch.l", "foot.l"):
        assert details[bone]["method"] == "band", bone
        assert details[bone]["stencilHeadDistance"] < 0.02, bone


def test_known_issues_are_reported(female, male):
    female_codes = {(item["code"], item["details"].get("shape")) for item in female["checks"]}
    male_codes = {(item["code"], item["details"].get("shape")) for item in male["checks"]}
    assert ("shape-unsuffixed", "Body_Stat_Muscle") in female_codes
    assert ("proxy-extra-shape", "Key 1") in male_codes
    assert any(code == "weights-over-four-influences" for code, _ in female_codes)
