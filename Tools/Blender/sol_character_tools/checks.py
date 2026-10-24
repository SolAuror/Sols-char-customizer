"""Checks run before export. Each returns findings; nothing is modified.

Severities: "error" means Unity will show something wrong, "warning" probably
wrong, "info" worth knowing. Project-specific exceptions live in a config file
(see Tools/Blender/character_checks.json).
"""

import re

import numpy as np
from mathutils import kdtree

SHAPE_SUFFIX = re.compile(r"^(?P<base>.+?)(?P<sign>[+-])$")
NEAR_EMPTY_METRES = 0.0001
NEAR_EMPTY_VERTICES = 4
MOVED_METRES = 0.00001
PROXY_GAP_WARNING_METRES = 0.002
PROXY_GAP_INFO_METRES = 0.0005
ASYMMETRY_METRES = 0.0005
ASYMMETRY_FRACTION = 0.1
MIRROR_MATCH_METRES = 0.0005
UNITY_DEFAULT_BONES_PER_VERTEX = 4


def finding(severity, code, message, **details):
    return {"severity": severity, "code": code, "message": message, "details": details}


def run_checks(character, solver=None, config=None, compare_shapes=None):
    config = config or {}
    findings = []
    findings += check_shape_names(character, config)
    findings += check_shape_keys(character)
    findings += check_symmetry(character, config)
    findings += check_proxy_coverage(character, config)
    findings += check_weights(character)
    if solver is not None:
        findings += check_joint_stencils(solver)
    if compare_shapes is not None:
        findings += check_against_other_character(character, compare_shapes)
    order = {"error": 0, "warning": 1, "info": 2}
    return sorted(findings, key=lambda item: (order[item["severity"]], item["code"]))


def check_shape_names(character, config):
    findings = []
    allow_unpaired = set(config.get("allowUnpaired", []))
    allow_unsuffixed = set(config.get("allowUnsuffixed", []))
    signs = {}
    for name in character.body.shapes:
        match = SHAPE_SUFFIX.match(name)
        if match is None:
            if name not in allow_unsuffixed:
                findings.append(finding(
                    "warning", "shape-unsuffixed",
                    f"'{name}' has no + or - suffix, so the catalog cannot pair it. Rename it '{name}+' "
                    "or list it under allowUnsuffixed.",
                    shape=name))
            continue
        signs.setdefault(match.group("base"), set()).add(match.group("sign"))

    for base, present in sorted(signs.items()):
        if present == {"+", "-"}:
            continue
        name = base + next(iter(present))
        if name in allow_unpaired or base in allow_unpaired:
            continue
        missing = base + ("-" if "+" in present else "+")
        findings.append(finding(
            "warning", "shape-unpaired",
            f"'{name}' has no '{missing}'. Add it, or list '{name}' under allowUnpaired if the morph is one-sided.",
            shape=name))
    return findings


def check_shape_keys(character):
    findings = []
    for mesh in character.meshes:
        for name, delta in mesh.shapes.items():
            info = mesh.shape_info[name]
            if info["relative_key"] not in (None, info["reference_key"]):
                findings.append(finding(
                    "warning", "shape-relative-key",
                    f"{mesh.name}: '{name}' is relative to '{info['relative_key']}', but FBX exports it "
                    "against the basis, so it will look different in Unity than in Blender.",
                    mesh=mesh.name, shape=name))
            if abs(info["value"]) > 1e-6:
                findings.append(finding(
                    "warning", "shape-nonzero-value",
                    f"{mesh.name}: '{name}' is saved at {info['value']:.2f}. FBX exports it as the default "
                    "blendshape weight, so characters load pre-morphed.",
                    mesh=mesh.name, shape=name, value=info["value"]))

        if mesh is not character.body:
            continue
        for name, delta in mesh.shapes.items():
            lengths = np.linalg.norm(delta, axis=1)
            moved = int((lengths > MOVED_METRES).sum())
            largest = float(lengths.max()) if len(lengths) else 0.0
            if largest < NEAR_EMPTY_METRES or moved < NEAR_EMPTY_VERTICES:
                findings.append(finding(
                    "warning", "shape-near-empty",
                    f"'{name}' moves {moved} body vertices by at most {largest * 1000:.3f} mm; it was "
                    "probably captured from an unchanged mesh.",
                    shape=name, movedVertices=moved, maxMillimetres=largest * 1000))
    return findings


def check_symmetry(character, config):
    body = character.body
    lateral = _lateral_axis(character)
    if lateral is None:
        return []

    mirror = _mirror_map(body.basis, lateral)
    matched = mirror >= 0
    if matched.mean() < 0.95:
        return [finding("info", "symmetry-skipped",
                        f"Only {matched.mean():.0%} of body vertices have a mirror partner; symmetry was not checked.")]

    allow = set(config.get("allowAsymmetric", []))
    flip = np.ones(3)
    flip[lateral] = -1.0
    findings = []
    for name, delta in body.shapes.items():
        if name in allow:
            continue
        mirrored = delta[mirror[matched]] * flip
        error = np.linalg.norm(delta[matched] - mirrored, axis=1)
        largest = float(np.linalg.norm(delta, axis=1).max())
        worst = float(error.max()) if len(error) else 0.0
        if worst > ASYMMETRY_METRES and worst > ASYMMETRY_FRACTION * largest:
            findings.append(finding(
                "warning", "shape-asymmetric",
                f"'{name}' differs from its mirror by up to {worst * 1000:.2f} mm "
                f"({worst / largest:.0%} of its largest move). List it under allowAsymmetric if intended.",
                shape=name, millimetres=worst * 1000))
    return findings


def check_proxy_coverage(character, config):
    """A proxy missing a shape only matters if the body moves near that proxy."""
    body = character.body
    tree = _tree(body.basis)
    ignore = config.get("ignoreProxyShapes", {})
    findings = []
    for proxy in character.proxies:
        nearest = np.array([tree.find(point.tolist())[1] for point in proxy.basis], dtype=np.int64)
        ignored = set(ignore.get(_proxy_key(proxy.name, ignore), []))
        for name, delta in body.shapes.items():
            if name in proxy.shapes or name in ignored:
                continue
            # Interior proxies (eyes, teeth) sit next to surfaces that should move on
            # their own, such as eyelids and lips, so small gaps are only reported.
            gap = float(np.linalg.norm(delta[nearest], axis=1).max())
            if gap > PROXY_GAP_INFO_METRES:
                severity = "warning" if gap > PROXY_GAP_WARNING_METRES else "info"
                findings.append(finding(
                    severity, "proxy-missing-shape",
                    f"{proxy.name} lacks '{name}', but the body next to it moves up to {gap * 1000:.1f} mm, "
                    "so it may separate or clip. Transfer the shape, or list it under ignoreProxyShapes if the "
                    "proxy should stay put.",
                    mesh=proxy.name, shape=name, millimetres=gap * 1000))

        for name in proxy.shapes:
            if name not in body.shapes:
                findings.append(finding(
                    "info", "proxy-extra-shape",
                    f"{proxy.name} has '{name}', which the body does not. It needs its own catalog entry.",
                    mesh=proxy.name, shape=name))
    return findings


def check_weights(character):
    findings = []
    for mesh in character.meshes:
        unweighted = int((mesh.weight_sums <= 1e-6).sum())
        if unweighted:
            findings.append(finding(
                "error", "weights-unweighted",
                f"{mesh.name}: {unweighted} vertices have no skin weight and will stay behind when animated.",
                mesh=mesh.name, vertices=unweighted))

        weighted = mesh.weight_sums > 1e-6
        unnormalized = int((np.abs(mesh.weight_sums[weighted] - 1.0) > 0.01).sum())
        if unnormalized:
            findings.append(finding(
                "info", "weights-unnormalized",
                f"{mesh.name}: {unnormalized} vertices have weights that do not sum to 1; Unity renormalizes them.",
                mesh=mesh.name, vertices=unnormalized))

        over = int((mesh.influence_counts > UNITY_DEFAULT_BONES_PER_VERTEX).sum())
        if over:
            findings.append(finding(
                "warning", "weights-over-four-influences",
                f"{mesh.name}: {over} vertices ({over / mesh.count:.0%}) have more than "
                f"{UNITY_DEFAULT_BONES_PER_VERTEX} bone influences, up to {int(mesh.influence_counts.max())}. "
                "Unity keeps 4 by default; set the model's Skin Weights to Custom with more bones, or limit "
                "and normalize to 4 in Blender before export.",
                mesh=mesh.name, vertices=over, maximum=int(mesh.influence_counts.max())))
    return findings


def check_joint_stencils(solver):
    findings = []
    fallback = sorted(name for name, detail in solver.details.items() if detail["method"] in ("region", "children", "inherit"))
    if fallback:
        findings.append(finding(
            "info", "joint-region-fallback",
            f"{len(fallback)} bones follow their own weighted region or their children rather than a joint band. "
            "That suits roots, eyes, and facial bones; a limb bone in this list has a weak joint band.",
            bones=fallback))

    unresolved = sorted(name for name in solver.character.deform_bones() if solver.stencils.get(name) is None)
    if unresolved:
        findings.append(finding(
            "warning", "joint-unresolved",
            f"{len(unresolved)} deform bones have no weights anywhere and no parent to follow; they get no offsets.",
            bones=unresolved))
    return findings


def check_against_other_character(character, other_shapes):
    findings = []
    mine = set(character.body.shapes)
    other = set(other_shapes)
    only_here = sorted(mine - other)
    only_there = sorted(other - mine)
    if only_here or only_there:
        findings.append(finding(
            "warning", "shapes-differ-from-other-character",
            "Body shape keys differ from the compared character, so shared catalog IDs will be missing on one side.",
            onlyHere=only_here, onlyInOther=only_there))
    return findings


def _proxy_key(proxy_name, ignore):
    for key in ignore:
        if key.lower() in proxy_name.lower():
            return key
    return None


def _lateral_axis(character):
    """The axis that separates .l bones from their .r partners."""
    bones = character.bones
    differences = []
    for name, info in bones.items():
        if name.endswith(".l") and name[:-2] + ".r" in bones:
            differences.append(np.abs(info["head"] - bones[name[:-2] + ".r"]["head"]))
    if not differences:
        return None
    return int(np.argmax(np.mean(differences, axis=0)))


def _mirror_map(points, axis):
    tree = _tree(points)
    mirror = np.full(len(points), -1, dtype=np.int64)
    flipped = points.copy()
    flipped[:, axis] *= -1.0
    for index, point in enumerate(flipped):
        _, partner, distance = tree.find(point.tolist())
        if distance <= MIRROR_MATCH_METRES:
            mirror[index] = partner
    return mirror


def _tree(points):
    tree = kdtree.KDTree(len(points))
    for index, point in enumerate(points):
        tree.insert(point.tolist(), index)
    tree.balance()
    return tree
