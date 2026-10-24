"""Finds the character in a Blender scene and reads it as numpy arrays.

Everything here is read-only: skin weights, shape keys, and bones are never
modified, so hand-authored shapes and voxel-heat skinning stay untouched.
"""

import numpy as np


class MeshData:
    """One mesh deformed by the character armature, in armature space (metres)."""

    def __init__(self, obj, to_armature, unit_scale):
        mesh = obj.data
        self.object = obj
        self.name = obj.name
        self.count = len(mesh.vertices)

        matrix = np.array(to_armature, dtype=np.float64) * unit_scale
        self._linear = matrix[:3, :3]
        self._translation = matrix[:3, 3]

        keys = mesh.shape_keys
        reference = keys.reference_key if keys else None
        self.basis = self._points(reference.data if reference else mesh.vertices)

        # Deltas are taken against the basis, which is what the FBX exporter
        # writes and Unity imports, even when a key is relative to another key.
        self.shapes = {}
        self.shape_info = {}
        if keys:
            for block in keys.key_blocks:
                if block == reference:
                    continue
                self.shapes[block.name] = self._points(block.data) - self.basis
                self.shape_info[block.name] = {
                    "relative_key": block.relative_key.name if block.relative_key else None,
                    "reference_key": reference.name,
                    "value": float(block.value),
                    "muted": bool(block.mute),
                }

        self.groups = {group.index: group.name for group in obj.vertex_groups}
        self.weights = {}
        self.influence_counts = np.zeros(self.count, dtype=np.int32)
        self.weight_sums = np.zeros(self.count, dtype=np.float64)
        for vertex in mesh.vertices:
            for element in vertex.groups:
                name = self.groups.get(element.group)
                if name is None or element.weight <= 0.0:
                    continue
                column = self.weights.get(name)
                if column is None:
                    column = self.weights[name] = np.zeros(self.count, dtype=np.float64)
                column[vertex.index] = element.weight
                self.weight_sums[vertex.index] += element.weight
                if element.weight > 0.001:
                    self.influence_counts[vertex.index] += 1

        # Blender's armature deform and Unity both divide by the weight total, so
        # joint maths uses normalized weights; the raw sums stay for the checks.
        safe_sums = np.where(self.weight_sums > 0.0, self.weight_sums, 1.0)
        for column in self.weights.values():
            column /= safe_sums

    def _points(self, elements):
        coordinates = np.empty(len(elements) * 3, dtype=np.float64)
        elements.foreach_get("co", coordinates)
        coordinates = coordinates.reshape(-1, 3)
        return coordinates @ self._linear.T + self._translation

    def delta(self, shape_name):
        """Shape delta, or zeros when this mesh does not carry the shape."""
        delta = self.shapes.get(shape_name)
        return delta if delta is not None else np.zeros((self.count, 3))


class CharacterScene:
    """The armature, its main body mesh, and the proxies it also deforms."""

    def __init__(self, armature, body, proxies):
        self.armature = armature
        self.unit_scale = _uniform_scale(armature)
        to_armature_space = armature.matrix_world.inverted()
        self.body = MeshData(body, to_armature_space @ body.matrix_world, self.unit_scale)
        self.proxies = [MeshData(proxy, to_armature_space @ proxy.matrix_world, self.unit_scale)
                        for proxy in proxies]
        self.meshes = [self.body] + self.proxies

        self.bones = {}
        for bone in armature.data.bones:
            self.bones[bone.name] = {
                "head": np.array(bone.head_local, dtype=np.float64) * self.unit_scale,
                "parent": _deform_parent(bone),
                "deform": bool(bone.use_deform),
                "children": [child.name for child in bone.children],
            }

    def deform_bones(self):
        """Deform bones in hierarchy order, parents before children."""
        ordered = []

        def visit(bone):
            if bone.use_deform:
                ordered.append(bone.name)
            for child in bone.children:
                visit(child)

        for bone in self.armature.data.bones:
            if bone.parent is None:
                visit(bone)
        return ordered

    def deform_children(self, bone_name):
        return [name for name, info in self.bones.items() if info["deform"] and info["parent"] == bone_name]

    def shape_names(self):
        names = list(self.body.shapes)
        for proxy in self.proxies:
            names.extend(name for name in proxy.shapes if name not in names)
        return names


def find_character(scene, armature_name=None, body_name=None):
    armatures = [obj for obj in scene.objects if obj.type == "ARMATURE"]
    if armature_name:
        armatures = [obj for obj in armatures if obj.name == armature_name]
    if not armatures:
        raise LookupError("No armature found" + (f" named '{armature_name}'." if armature_name else "."))

    best = None
    for armature in armatures:
        deformed = [obj for obj in scene.objects if obj.type == "MESH" and _is_deformed_by(obj, armature)]
        if deformed and (best is None or len(deformed) > len(best[1])):
            best = (armature, deformed)
    if best is None:
        raise LookupError("No mesh is deformed by an armature in this scene.")

    armature, meshes = best
    if body_name:
        bodies = [obj for obj in meshes if obj.name == body_name]
        if not bodies:
            raise LookupError(f"No mesh named '{body_name}' is deformed by '{armature.name}'.")
        body = bodies[0]
    else:
        body = max(meshes, key=lambda obj: len(obj.data.vertices))
    proxies = [obj for obj in meshes if obj is not body]
    return CharacterScene(armature, body, proxies)


def _is_deformed_by(obj, armature):
    if any(modifier.type == "ARMATURE" and modifier.object == armature for modifier in obj.modifiers):
        return True
    return obj.parent == armature and obj.parent_type == "ARMATURE"


def _deform_parent(bone):
    parent = bone.parent
    while parent is not None and not parent.use_deform:
        parent = parent.parent
    return parent.name if parent is not None else None


def _uniform_scale(obj):
    scale = obj.matrix_world.to_scale()
    return float(sum(abs(component) for component in scale) / 3.0)
