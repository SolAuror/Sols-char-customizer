"""Bakes the Sol/Character Skin region and utility masks into the body UVs.

Region mask (RGBA): R lips, G palms, soles, and nails, B blush, A areolae.
Utility mask (RGB): R occlusion, G transmission (1 where thin), B curvature.

Region masks come from the MakeHuman body-part vertex groups on the MPFB base
mesh (lips, fingernails, toenails, nipple, ears), which shares vertex order and
UVs with the exported body. Utility masks are computed from the body's rest
geometry by ray casting. Values are found per vertex, smoothed over the mesh
for soft edges, then rasterized into UV space and padded past island borders.
Nothing in the scene is modified apart from the two saved images.
"""

import os

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import scene as character_scene

SIDES = (("l", "Left"), ("r", "Right"))
FINGERS = ("thumb", "index", "middle", "ring", "pinky")

# Blush sources: armature bone, falloff radius in metres, strength.
BLUSH_BONES = (
    ("c_cheek_inflate.l", 0.028, 1.0),
    ("c_cheek_inflate.r", 0.028, 1.0),
    ("c_nose_03.x", 0.012, 0.7),
)
EAR_BLUSH = 0.6

# Areolae are discs around each nipple tip rather than the whole MakeHuman
# nipple group, which covers much of the breast cone. Full strength inside the
# inner radius, fading out by the outer one (metres).
AREOLA_INNER_RADIUS = 0.009
AREOLA_OUTER_RADIUS = 0.016

AO_RAYS = 32
AO_DISTANCE = 0.25
THICKNESS_RAYS = 24
THICKNESS_DISTANCE = 0.1
# Thickness at which transmission falls to 1/e; ears (~5 mm) stay bright,
# fingers (~15 mm) dim, and the torso goes dark.
TRANSMISSION_DEPTH = 0.012
# Mean curvature (1/m) that maps to full curvature; 1 cm radius features read
# about 0.6, the torso near zero.
CURVATURE_FULL = 160.0

REGION_SMOOTHING = 3
UTILITY_SMOOTHING = 2
PADDING = 8
# How far palms and soles must face away from the nails before they count, as
# the cosine between the normal and the palm direction. Below the low end the
# backs and sides of hands and feet keep the surrounding skin.
PALM_FACING = (0.35, 0.8)
# Image-space blur, in texels at 2048 and scaled with size, that removes the
# faceting left by interpolating per-vertex values across large triangles.
BLUR_TEXELS_AT_2048 = 3


def bake(blender_scene=None, body_name=None, source_name=None, output_dir="//", size=2048):
    """Bakes both masks and saves them as PNGs. Returns a report dict."""
    blender_scene = blender_scene or bpy.context.scene
    character = character_scene.find_character(blender_scene, body_name=body_name)
    body = character.body.object
    geometry = _Geometry(body)
    source = find_source(blender_scene, body, source_name)
    notes = []

    if source is None:
        notes.append("No MPFB base mesh with matching vertices was found, so the region mask is empty. "
                     "Keep the MPFB body (with its 'lips' and 'fingernails' groups) in the file.")
        region = np.zeros((geometry.count, 4))
    else:
        region = _region_fields(geometry, _source_weights(source, geometry.count), character.armature, body, notes)

    utility = np.stack([
        _occlusion(geometry),
        _transmission(geometry),
        _curvature(geometry),
    ], axis=1)

    region = geometry.smooth(region, REGION_SMOOTHING)
    utility = geometry.smooth(utility, UTILITY_SMOOTHING)

    region_pixels, coverage = geometry.rasterize(region, size)
    utility_pixels, _ = geometry.rasterize(utility, size)
    blur = max(1, round(BLUR_TEXELS_AT_2048 * size / 2048))
    region_pixels = _pad(_blur(region_pixels, coverage, blur), coverage, PADDING)
    utility_pixels = _pad(_blur(utility_pixels, coverage, blur), coverage, PADDING)
    utility_pixels = np.concatenate([utility_pixels, np.ones((size, size, 1))], axis=2)

    folder = bpy.path.abspath(output_dir)
    os.makedirs(folder, exist_ok=True)
    region_path = os.path.join(folder, f"{body.name}_SkinRegion.png")
    utility_path = os.path.join(folder, f"{body.name}_SkinUtility.png")
    _save(region_pixels, region_path)
    _save(utility_pixels, utility_path)

    return {
        "body": body.name,
        "source": source.name if source else None,
        "region": region_path,
        "utility": utility_path,
        "coverage": float(coverage.mean()),
        "means": {
            "lips": float(region[:, 0].mean()),
            "palmsSolesNails": float(region[:, 1].mean()),
            "blush": float(region[:, 2].mean()),
            "areolae": float(region[:, 3].mean()),
            "occlusion": float(utility[:, 0].mean()),
            "transmission": float(utility[:, 1].mean()),
            "curvature": float(utility[:, 2].mean()),
        },
        "notes": notes,
    }


def find_source(blender_scene, body, source_name=None):
    """The MPFB base mesh whose first vertices are the body's, with matching UVs."""
    candidates = [obj for obj in blender_scene.objects
                  if obj.type == "MESH" and obj is not body and "lips" in obj.vertex_groups]
    if source_name:
        candidates = [obj for obj in candidates if obj.name == source_name]
    count = len(body.data.vertices)
    body_uv = _vertex_uvs(body.data, count)
    for obj in candidates:
        if len(obj.data.vertices) < count:
            continue
        uv = _vertex_uvs(obj.data, count)
        if np.allclose(uv, body_uv, atol=1e-4):
            return obj
    return None


def _vertex_uvs(mesh, count):
    loops = len(mesh.loops)
    indices = np.empty(loops, dtype=np.int64)
    mesh.loops.foreach_get("vertex_index", indices)
    uvs = np.empty(loops * 2)
    mesh.uv_layers.active.data.foreach_get("uv", uvs)
    uvs = uvs.reshape(-1, 2)
    result = np.full((count, 2), np.nan)
    inside = indices < count
    result[indices[inside]] = uvs[inside]
    return np.nan_to_num(result, nan=-1.0)


def _source_weights(source, count):
    names = {group.index: group.name for group in source.vertex_groups}
    weights = {}
    for vertex in source.data.vertices[:count]:
        for element in vertex.groups:
            if element.weight <= 0.0:
                continue
            column = weights.get(names[element.group])
            if column is None:
                column = weights[names[element.group]] = np.zeros(count)
            column[vertex.index] = element.weight
    return weights


def _region_fields(geometry, weights, armature, body, notes):
    count = geometry.count
    zero = np.zeros(count)

    def group(name):
        return weights.get(name, zero)

    lips = group("lips")
    nails = np.maximum(group("fingernails"), group("toenails"))

    palms_soles = np.zeros(count)
    for suffix, side in SIDES:
        on_side = group(side) > 0.0
        hand = group(f"hand_{suffix}") + sum(group(f"{finger}_0{joint}_{suffix}")
                                             for finger in FINGERS for joint in (1, 2, 3))
        foot = group(f"foot_{suffix}") + group(f"ball_{suffix}")
        # Nails sit on the back of the hand and the top of the foot, so the palm
        # and sole face away from their average normal.
        for region, nail_group in ((hand, "fingernails"), (foot, "toenails")):
            nail = (group(nail_group) > 0.0) & on_side
            if not nail.any():
                notes.append(f"No {nail_group} on the {side.lower()} side; that side's palm or sole is skipped.")
                continue
            away = -geometry.normals[nail].mean(axis=0)
            away /= np.linalg.norm(away)
            facing = _smoothstep(*PALM_FACING, geometry.normals @ away)
            palms_soles = np.maximum(palms_soles, np.clip(region, 0.0, 1.0) * facing * on_side)
    palms_soles = np.maximum(palms_soles, nails)

    blush = group("ears") * EAR_BLUSH
    head = group("head")
    to_body = body.matrix_world.inverted() @ armature.matrix_world
    for bone_name, radius, strength in BLUSH_BONES:
        bone = armature.data.bones.get(bone_name)
        if bone is None:
            notes.append(f"Bone '{bone_name}' not found; its blush is skipped.")
            continue
        centre = np.array(to_body @ bone.head_local)
        distance = np.linalg.norm(geometry.local_points - centre, axis=1) * geometry.scale
        blush = np.maximum(blush, strength * np.exp(-(distance / radius) ** 2) * np.clip(head, 0.0, 1.0))

    areolae = np.zeros(count)
    tips = group("nippleTip") if group("nippleTip").any() else group("nipple")
    for _, side in SIDES:
        tip = (tips > 0.0) & (group(side) > 0.0)
        if not tip.any():
            notes.append(f"No nipple group on the {side.lower()} side; that areola is skipped.")
            continue
        centre = geometry.points[tip].mean(axis=0)
        distance = np.linalg.norm(geometry.points - centre, axis=1)
        areolae = np.maximum(areolae, 1.0 - _smoothstep(AREOLA_INNER_RADIUS, AREOLA_OUTER_RADIUS, distance))

    return np.stack([lips, palms_soles, blush, areolae], axis=1).clip(0.0, 1.0)


def _occlusion(geometry):
    directions = _hemisphere(AO_RAYS)
    visible = np.zeros(geometry.count)
    for index in range(geometry.count):
        origin = Vector(geometry.points[index] + geometry.normals[index] * 1e-4)
        for direction in _orient(directions, geometry.normals[index]):
            if geometry.tree.ray_cast(origin, Vector(direction), AO_DISTANCE)[0] is None:
                visible[index] += 1.0
    return visible / AO_RAYS


def _transmission(geometry):
    directions = _hemisphere(THICKNESS_RAYS)
    depth = np.zeros(geometry.count)
    for index in range(geometry.count):
        normal = geometry.normals[index]
        origin = Vector(geometry.points[index] - normal * 1e-4)
        total = 0.0
        for direction in _orient(directions, -normal):
            hit = geometry.tree.ray_cast(origin, Vector(direction), THICKNESS_DISTANCE)
            total += hit[3] if hit[0] is not None else THICKNESS_DISTANCE
        depth[index] = total / THICKNESS_RAYS
    return np.exp(-depth / TRANSMISSION_DEPTH)


def _curvature(geometry):
    first, second = geometry.edges[:, 0], geometry.edges[:, 1]
    offset = geometry.points[first] - geometry.points[second]
    length_squared = np.maximum((offset ** 2).sum(axis=1), 1e-12)
    total = np.zeros(geometry.count)
    np.add.at(total, first, 2.0 * (geometry.normals[first] * offset).sum(axis=1) / length_squared)
    np.add.at(total, second, -2.0 * (geometry.normals[second] * offset).sum(axis=1) / length_squared)
    mean = total / np.maximum(geometry.valence, 1)
    return np.clip(np.abs(mean) / CURVATURE_FULL, 0.0, 1.0)


class _Geometry:
    """The body's rest mesh in world space (metres), with UV triangles and adjacency."""

    def __init__(self, obj):
        mesh = obj.data
        self.count = len(mesh.vertices)
        local = np.empty(self.count * 3)
        reference = mesh.shape_keys.reference_key if mesh.shape_keys else None
        (reference.data if reference else mesh.vertices).foreach_get("co", local)
        self.local_points = local.reshape(-1, 3)
        normals = np.empty(self.count * 3)
        mesh.vertices.foreach_get("normal", normals)

        matrix = np.array(obj.matrix_world)
        linear = matrix[:3, :3]
        self.scale = float(np.cbrt(abs(np.linalg.det(linear))))
        self.points = self.local_points @ linear.T + matrix[:3, 3]
        world_normals = normals.reshape(-1, 3) @ np.linalg.inv(linear)
        self.normals = world_normals / np.maximum(np.linalg.norm(world_normals, axis=1, keepdims=True), 1e-12)

        mesh.calc_loop_triangles()
        triangles = len(mesh.loop_triangles)
        self.triangle_vertices = np.empty(triangles * 3, dtype=np.int64)
        mesh.loop_triangles.foreach_get("vertices", self.triangle_vertices)
        self.triangle_vertices = self.triangle_vertices.reshape(-1, 3)
        triangle_loops = np.empty(triangles * 3, dtype=np.int64)
        mesh.loop_triangles.foreach_get("loops", triangle_loops)
        uvs = np.empty(len(mesh.loops) * 2)
        mesh.uv_layers.active.data.foreach_get("uv", uvs)
        self.triangle_uvs = uvs.reshape(-1, 2)[triangle_loops].reshape(-1, 3, 2)

        edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
        mesh.edges.foreach_get("vertices", edges)
        self.edges = edges.reshape(-1, 2)
        self.valence = np.bincount(self.edges.ravel(), minlength=self.count)

        self.tree = BVHTree.FromPolygons([tuple(point) for point in self.points],
                                         [tuple(triangle) for triangle in self.triangle_vertices])

    def smooth(self, values, iterations):
        """Averages each vertex with its neighbours, which softens mask edges."""
        values = np.asarray(values, dtype=np.float64)
        flat = values.reshape(self.count, -1)
        for _ in range(iterations):
            total = flat.copy()
            np.add.at(total, self.edges[:, 0], flat[self.edges[:, 1]])
            np.add.at(total, self.edges[:, 1], flat[self.edges[:, 0]])
            flat = total / (self.valence + 1)[:, None]
        return flat.reshape(values.shape)

    def rasterize(self, values, size):
        """Interpolates per-vertex values across UV triangles. Row 0 is v = 0."""
        values = np.asarray(values, dtype=np.float64).reshape(self.count, -1)
        image = np.zeros((size, size, values.shape[1]))
        coverage = np.zeros((size, size), dtype=bool)
        corners = self.triangle_uvs * size - 0.5
        for triangle, (a, b, c) in enumerate(corners):
            low = np.floor(np.minimum(np.minimum(a, b), c)).astype(int).clip(0, size - 1)
            high = np.ceil(np.maximum(np.maximum(a, b), c)).astype(int).clip(0, size - 1)
            xs, ys = np.meshgrid(np.arange(low[0], high[0] + 1), np.arange(low[1], high[1] + 1))
            area = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
            if abs(area) < 1e-12:
                continue
            w_b = ((xs - a[0]) * (c[1] - a[1]) - (ys - a[1]) * (c[0] - a[0])) / area
            w_c = ((b[0] - a[0]) * (ys - a[1]) - (b[1] - a[1]) * (xs - a[0])) / area
            w_a = 1.0 - w_b - w_c
            inside = (w_a >= -1e-6) & (w_b >= -1e-6) & (w_c >= -1e-6)
            if not inside.any():
                continue
            ia, ib, ic = self.triangle_vertices[triangle]
            blended = (w_a[inside, None] * values[ia] + w_b[inside, None] * values[ib]
                       + w_c[inside, None] * values[ic])
            image[ys[inside], xs[inside]] = blended
            coverage[ys[inside], xs[inside]] = True
        return image, coverage


def _blur(image, coverage, radius):
    """Two box-blur passes per axis (close to a Gaussian), weighted by coverage so
    empty space outside UV islands never darkens their edges."""
    weight = coverage.astype(np.float64)[..., None]
    total = image * weight
    for _ in range(2):
        for axis in (0, 1):
            total = _box(total, radius, axis)
            weight = _box(weight, radius, axis)
    blurred = total / np.maximum(weight, 1e-6)
    return np.where(coverage[..., None], blurred, image)


def _box(image, radius, axis):
    padded = np.concatenate([np.zeros_like(np.take(image, [0], axis=axis))] + [image], axis=axis)
    summed = np.cumsum(padded, axis=axis)
    size = image.shape[axis]
    upper = np.clip(np.arange(size) + radius + 1, 0, size)
    lower = np.clip(np.arange(size) - radius, 0, size)
    return (np.take(summed, upper, axis=axis) - np.take(summed, lower, axis=axis)) / (2 * radius + 1)


def _pad(image, coverage, iterations):
    """Extends islands outward so mipmaps and filtering do not bleed in black."""
    image = image.copy()
    filled = coverage.copy()
    for _ in range(iterations):
        total = np.zeros_like(image)
        count = np.zeros(filled.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(np.roll(filled, dy, axis=0), dx, axis=1)
            total += np.roll(np.roll(image * filled[..., None], dy, axis=0), dx, axis=1)
            count += shifted
        grow = ~filled & (count > 0)
        image[grow] = total[grow] / count[grow, None]
        filled |= grow
    return image


def _save(pixels, path):
    size = pixels.shape[0]
    name = os.path.splitext(os.path.basename(path))[0]
    existing = bpy.data.images.get(name)
    if existing is not None:
        bpy.data.images.remove(existing)
    image = bpy.data.images.new(name, size, size, alpha=True)
    image.colorspace_settings.name = "Non-Color"
    rgba = pixels if pixels.shape[2] == 4 else np.concatenate([pixels, np.ones((size, size, 1))], axis=2)
    image.pixels.foreach_set(rgba.clip(0.0, 1.0).astype(np.float32).ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return image


def _hemisphere(count):
    """Cosine-weighted directions around +Z on a fixed spiral, so bakes repeat exactly."""
    index = np.arange(count) + 0.5
    radius = np.sqrt(index / count)
    angle = index * np.pi * (3.0 - np.sqrt(5.0))
    return np.stack([radius * np.cos(angle), radius * np.sin(angle), np.sqrt(1.0 - radius ** 2)], axis=1)


def _orient(directions, normal):
    helper = np.array([1.0, 0.0, 0.0]) if abs(normal[0]) < 0.9 else np.array([0.0, 1.0, 0.0])
    tangent = np.cross(normal, helper)
    tangent /= np.linalg.norm(tangent)
    bitangent = np.cross(normal, tangent)
    return directions[:, 0:1] * tangent + directions[:, 1:2] * bitangent + directions[:, 2:3] * normal


def _smoothstep(low, high, value):
    t = np.clip((value - low) / (high - low), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)
