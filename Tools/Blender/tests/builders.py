"""Small synthetic characters with known answers."""

import bpy


def armature(scene, bones):
    """bones: [(name, head, tail, parent name or None)]."""
    data = bpy.data.armatures.new("Rig")
    obj = bpy.data.objects.new("Rig", data)
    scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, parent in bones:
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def skinned_mesh(scene, name, rig, vertices, faces, weights):
    """weights: callable(vertex position) -> {bone name: weight}."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    obj.parent = rig
    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig
    groups = {}
    for index, position in enumerate(vertices):
        for bone, weight in weights(position).items():
            if weight <= 0.0:
                continue
            group = groups.get(bone) or obj.vertex_groups.new(name=bone)
            groups[bone] = group
            group.add([index], weight, "REPLACE")
    obj.shape_key_add(name="Basis", from_mix=False)
    return obj


def add_shape(obj, name, move):
    """move: callable(index, position) -> (dx, dy, dz)."""
    key = obj.shape_key_add(name=name, from_mix=False)
    # Blender 5 adds keys at full value; authored files keep morphs at rest.
    key.value = 0.0
    for index, point in enumerate(key.data):
        delta = move(index, tuple(obj.data.vertices[index].co))
        point.co = obj.data.vertices[index].co + type(point.co)(delta)
    return key


def tube(x_start, x_end, slices, radius, sides=8):
    """A tube along X, returned as (vertices, faces)."""
    import math

    vertices = []
    for slice_index in range(slices + 1):
        x = x_start + (x_end - x_start) * slice_index / slices
        for side in range(sides):
            angle = 2.0 * math.pi * side / sides
            vertices.append((x, radius * math.cos(angle), radius * math.sin(angle)))
    faces = []
    for slice_index in range(slices):
        for side in range(sides):
            a = slice_index * sides + side
            b = slice_index * sides + (side + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    return vertices, faces


def arm_weights(position):
    """Upper arm to 1.0, forearm (split with its twist bone) to 2.0, hand beyond."""
    x = position[0]
    if x <= 0.8:
        return {"upper": 1.0}
    if x <= 1.2:
        t = (x - 0.8) / 0.4
        return {"upper": 1.0 - t, "lower": t}
    if x <= 1.8:
        t = (x - 1.2) / 0.6
        return {"lower": 1.0 - t, "lower_twist": t}
    if x <= 2.2:
        t = (x - 1.8) / 0.4
        return {"lower_twist": 1.0 - t, "hand": t}
    return {"hand": 1.0}


ARM_BONES = [
    ("upper", (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), None),
    ("lower", (1.0, 0.0, 0.0), (2.0, 0.0, 0.0), "upper"),
    ("lower_twist", (1.5, 0.0, 0.0), (2.0, 0.0, 0.0), "lower"),
    ("hand", (2.0, 0.0, 0.0), (2.5, 0.0, 0.0), "lower"),
    ("upper_twist", (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), "upper"),
]


def build_arm(scene):
    rig = armature(scene, ARM_BONES)
    vertices, faces = tube(0.0, 2.6, 26, 0.1)
    body = skinned_mesh(scene, "Body", rig, vertices, faces, arm_weights)
    return rig, body
