import bpy
import numpy as np
import pytest

from sol_character_tools import masks


def _uv_plane(scene, name="Plane"):
    """A unit quad split in two triangles, UVs covering the whole image."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], [], [(0, 1, 2, 3)])
    uv = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        co = mesh.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (co.x, co.y)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


def test_rasterize_interpolates_vertex_values_across_uvs(empty_scene):
    geometry = masks._Geometry(_uv_plane(empty_scene))
    values = np.array([0.0, 1.0, 1.0, 0.0])  # follows u

    image, coverage = geometry.rasterize(values, 64)

    assert coverage.all()
    row = image[32, :, 0]
    assert row[0] < 0.05 and row[-1] > 0.95
    assert np.all(np.diff(row) >= -1e-9)


def test_padding_fills_outside_islands_from_their_edges():
    image = np.zeros((8, 8, 1))
    coverage = np.zeros((8, 8), dtype=bool)
    image[3:5, 3:5] = 0.75
    coverage[3:5, 3:5] = True

    padded = masks._pad(image, coverage, 2)

    assert padded[2, 3, 0] == 0.75
    assert padded[0, 0, 0] == 0.0


def test_blur_softens_edges_without_darkening_island_borders():
    image = np.zeros((16, 16, 1))
    coverage = np.zeros((16, 16), dtype=bool)
    coverage[4:12, 4:12] = True
    image[4:12, 4:12] = 1.0
    image[4:12, 4:8] = 0.0

    blurred = masks._blur(image, coverage, 1)

    assert blurred[4, 11, 0] == pytest.approx(1.0)  # island corner keeps its value; outside is not averaged in
    assert 0.0 < blurred[8, 7, 0] < 1.0  # the step inside the island is softened
    assert blurred[0, 0, 0] == 0.0


def test_a_flat_open_plane_is_unoccluded_and_has_no_curvature(empty_scene):
    geometry = masks._Geometry(_uv_plane(empty_scene))

    assert np.allclose(masks._occlusion(geometry), 1.0)
    assert np.allclose(masks._curvature(geometry), 0.0)


def test_source_must_share_vertex_uvs(empty_scene):
    body = _uv_plane(empty_scene, "Body")
    source = _uv_plane(empty_scene, "Base")
    source.vertex_groups.new(name="lips")
    assert masks.find_source(empty_scene, body) is source

    for loop in source.data.uv_layers.active.data:
        loop.uv = (loop.uv.x * 0.5, loop.uv.y)
    assert masks.find_source(empty_scene, body) is None


def test_hemisphere_directions_are_unit_and_upward():
    directions = masks._hemisphere(32)

    assert np.allclose(np.linalg.norm(directions, axis=1), 1.0)
    assert np.all(directions[:, 2] > 0.0)
