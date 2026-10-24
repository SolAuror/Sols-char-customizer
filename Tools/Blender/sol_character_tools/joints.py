"""Joint offsets per shape key.

Each deform bone gets a stencil: a weighted set of vertices whose average is the
bone's joint. A shape key's joint offset is the stencil-weighted average of that
shape's vertex deltas, so offsets combine linearly exactly like blendshapes.

Candidate stencils per bone:
- band: vertices whose skin weight is shared with a neighbour, either the deform
  parent or a sibling such as a twist bone. Voxel heat skinning puts that band
  around the joint: the wrist band is shared by the hand and the forearm twist.
- region: the bone's own strongly weighted vertices (roots, eyeballs).
- colocated: the head sits on the parent's head, so it can share the parent's
  joint (twist bones at the start of a limb).
- children: the bone has no weights (an offset or control bone above a weighted
  one); it follows its weighted children.
- inherit: nothing else applies; it follows its parent's joint.

A joint is where the bone pivots, so of the available candidates the one whose
rest centre lies closest to the bone head wins. Inherit is only a last resort.
"""

import numpy as np

BAND_MIN_SHARE = 0.05
BAND_MIN_TOTAL = 1.0
COLOCATED_FRACTION = 0.02
MIN_OFFSET_METRES = 0.00005


class Stencil:
    def __init__(self, indices, weights, method, partner=None):
        total = float(weights.sum())
        self.indices = indices
        self.weights = weights / total if total > 0 else weights
        self.method = method
        self.partner = partner

    def apply(self, values):
        return self.weights @ values[self.indices]

    def relabel(self, method):
        return Stencil(self.indices, self.weights, method, self.partner)

    @staticmethod
    def average(stencils, method):
        indices = np.concatenate([stencil.indices for stencil in stencils])
        weights = np.concatenate([stencil.weights / len(stencils) for stencil in stencils])
        return Stencil(indices, weights, method)


class JointSolver:
    def __init__(self, character):
        self.character = character
        self.offsets_by_mesh = np.cumsum([0] + [mesh.count for mesh in character.meshes])
        self.basis = np.concatenate([mesh.basis for mesh in character.meshes])
        self.vertex_count = len(self.basis)
        self.stencils = {}
        self.details = {}
        self._columns = {}

    def weight_column(self, bone_name):
        column = self._columns.get(bone_name)
        if column is None:
            column = np.zeros(self.vertex_count)
            for mesh, start in zip(self.character.meshes, self.offsets_by_mesh):
                weights = mesh.weights.get(bone_name)
                if weights is not None:
                    column[start:start + mesh.count] = weights
            self._columns[bone_name] = column
        return column

    def delta(self, shape_name):
        return np.concatenate([mesh.delta(shape_name) for mesh in self.character.meshes])

    def solve(self):
        for bone_name in self.character.deform_bones():
            self.stencil(bone_name)
        return self.stencils

    def stencil(self, bone_name):
        if bone_name in self.stencils:
            return self.stencils[bone_name]

        parent = self.character.bones[bone_name]["parent"]
        candidates = self.own_candidates(bone_name)
        if parent is not None and self._same_head(bone_name, parent):
            parent_stencil = self.stencil(parent)
            if parent_stencil is not None:
                candidates.append(parent_stencil.relabel("colocated"))
        if not candidates:
            children = [self.own_stencil(child) for child in self.character.deform_children(bone_name)]
            children = [child for child in children if child is not None]
            if children:
                candidates.append(Stencil.average(children, "children"))

        head = self.character.bones[bone_name]["head"]
        stencil = min(candidates, key=lambda item: self._distance(item, head), default=None)
        if stencil is None and parent is not None:
            parent_stencil = self.stencil(parent)
            if parent_stencil is not None:
                stencil = parent_stencil.relabel("inherit")

        self.stencils[bone_name] = stencil
        if stencil is not None:
            centre = stencil.apply(self.basis)
            self.details[bone_name] = {
                "method": stencil.method,
                "partner": stencil.partner,
                "vertices": int(len(np.unique(stencil.indices))),
                "centre": centre,
                "headDistance": float(np.linalg.norm(centre - head)),
            }
        return stencil

    def own_candidates(self, bone_name):
        """Bands with each neighbour that shares enough weight, plus the bone's own region."""
        weights = self.weight_column(bone_name)
        if not weights.any():
            return []

        candidates = []
        for partner in self._band_partners(bone_name):
            share = np.minimum(weights, self.weight_column(partner))
            band = np.nonzero(share > BAND_MIN_SHARE)[0]
            if float(share[band].sum()) >= BAND_MIN_TOTAL:
                candidates.append(Stencil(band, share[band], "band", partner))

        region = np.nonzero(weights > 0.0)[0]
        candidates.append(Stencil(region, weights[region] ** 2, "region"))
        return candidates

    def own_stencil(self, bone_name):
        """The bone's best stencil from its own weights alone."""
        head = self.character.bones[bone_name]["head"]
        return min(self.own_candidates(bone_name), key=lambda item: self._distance(item, head), default=None)

    def _distance(self, stencil, head):
        return float(np.linalg.norm(stencil.apply(self.basis) - head))

    def _band_partners(self, bone_name):
        parent = self.character.bones[bone_name]["parent"]
        if parent is None:
            return []
        siblings = [name for name in self.character.deform_children(parent) if name != bone_name]
        # A sibling colocated with this bone (a twist bone on the same head) shares
        # its whole region, not a joint band.
        siblings = [name for name in siblings if not self._same_head(name, bone_name)]
        return [parent] + siblings

    def _same_head(self, first, second):
        bones = self.character.bones
        reach = self._reach(bones[second]["parent"] or second)
        return float(np.linalg.norm(bones[first]["head"] - bones[second]["head"])) <= COLOCATED_FRACTION * reach

    def _reach(self, bone_name):
        """Distance from a bone's head to its farthest deform child, as a length scale."""
        head = self.character.bones[bone_name]["head"]
        distances = [np.linalg.norm(self.character.bones[child]["head"] - head)
                     for child in self.character.deform_children(bone_name)]
        return float(max(distances)) if distances and max(distances) > 0 else 0.01


def compute_joint_offsets(character, solver=None):
    """Returns (bone order, {shape name: [(bone index, offset)]}, solver)."""
    solver = solver or JointSolver(character)
    stencils = solver.solve()
    bones = [name for name in character.deform_bones() if stencils.get(name) is not None]
    index_of = {name: index for index, name in enumerate(bones)}

    offsets = {}
    for shape_name in character.shape_names():
        delta = solver.delta(shape_name)
        moved = []
        for bone_name in bones:
            offset = stencils[bone_name].apply(delta)
            if np.linalg.norm(offset) >= MIN_OFFSET_METRES:
                moved.append((index_of[bone_name], offset))
        offsets[shape_name] = moved
    return bones, offsets, solver
