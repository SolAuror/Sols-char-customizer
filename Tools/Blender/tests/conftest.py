import os
import sys

import bpy
import pytest

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPOSITORY = os.path.dirname(os.path.dirname(TOOLS))
sys.path.insert(0, TOOLS)


@pytest.fixture
def empty_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return bpy.context.scene


def character_fbx(name):
    return os.path.join(REPOSITORY, "Assets", "CharCustomization", "Resources", name)
