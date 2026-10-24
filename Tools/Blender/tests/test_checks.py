from builders import add_shape, armature, build_arm, skinned_mesh, tube
from sol_character_tools import checks, joints, scene


def codes(findings, code):
    return [item for item in findings if item["code"] == code]


def run(empty_scene, config=None):
    character = scene.find_character(empty_scene)
    _, _, solver = joints.compute_joint_offsets(character)
    return checks.run_checks(character, solver, config)


def test_shape_names_must_pair(empty_scene):
    rig, body = build_arm(empty_scene)
    nothing = lambda index, position: (0.0, 0.0, 0.0)
    add_shape(body, "Paired+", lambda index, position: (0.0, 0.0, 0.01))
    add_shape(body, "Paired-", lambda index, position: (0.0, 0.0, -0.01))
    add_shape(body, "Single+", lambda index, position: (0.0, 0.0, 0.01))
    add_shape(body, "Allowed+", lambda index, position: (0.0, 0.0, 0.01))
    add_shape(body, "NoSign", lambda index, position: (0.0, 0.0, 0.01))

    findings = run(empty_scene, {"allowUnpaired": ["Allowed+"]})

    assert [item["details"]["shape"] for item in codes(findings, "shape-unpaired")] == ["Single+"]
    assert [item["details"]["shape"] for item in codes(findings, "shape-unsuffixed")] == ["NoSign"]


def test_near_empty_and_nonzero_shapes_are_reported(empty_scene):
    rig, body = build_arm(empty_scene)
    add_shape(body, "Empty+", lambda index, position: (0.0, 0.0, 0.00001) if index == 0 else (0.0, 0.0, 0.0))
    add_shape(body, "Empty-", lambda index, position: (0.0, 0.0, 0.01))
    body.data.shape_keys.key_blocks["Empty-"].value = 0.4

    findings = run(empty_scene)

    assert [item["details"]["shape"] for item in codes(findings, "shape-near-empty")] == ["Empty+"]
    assert [item["details"]["shape"] for item in codes(findings, "shape-nonzero-value")] == ["Empty-"]


def test_proxy_missing_a_shape_is_reported_only_where_the_body_moves(empty_scene):
    rig, body = build_arm(empty_scene)
    add_shape(body, "Elbow+", lambda index, position: (0.0, 0.0, 0.01) if 0.75 <= position[0] <= 1.25 else (0.0, 0.0, 0.0))
    vertices, faces = tube(0.9, 1.1, 2, 0.11)
    skinned_mesh(empty_scene, "ElbowPad", rig, vertices, faces, lambda position: {"lower": 1.0})
    vertices, faces = tube(2.3, 2.5, 2, 0.11)
    skinned_mesh(empty_scene, "Glove", rig, vertices, faces, lambda position: {"hand": 1.0})

    findings = codes(run(empty_scene), "proxy-missing-shape")

    assert [(item["details"]["mesh"], item["severity"]) for item in findings] == [("ElbowPad", "warning")]


def test_proxy_shape_exceptions_come_from_config(empty_scene):
    rig, body = build_arm(empty_scene)
    add_shape(body, "Elbow+", lambda index, position: (0.0, 0.0, 0.01) if 0.75 <= position[0] <= 1.25 else (0.0, 0.0, 0.0))
    vertices, faces = tube(0.9, 1.1, 2, 0.11)
    skinned_mesh(empty_scene, "ElbowPad", rig, vertices, faces, lambda position: {"lower": 1.0})

    findings = run(empty_scene, {"ignoreProxyShapes": {"elbowpad": ["Elbow+"]}})

    assert codes(findings, "proxy-missing-shape") == []


def test_unweighted_and_overweighted_vertices_are_reported(empty_scene):
    rig = armature(empty_scene, [(f"b{index}", (index, 0.0, 0.0), (index + 1, 0.0, 0.0), None) for index in range(6)])

    def weights(position):
        if position[0] < 0.5:
            return {}
        return {f"b{index}": 0.2 for index in range(6)} if position[0] > 2.0 else {"b0": 1.0}

    vertices, faces = tube(0.0, 3.0, 6, 0.1)
    skinned_mesh(empty_scene, "Body", rig, vertices, faces, weights)

    findings = run(empty_scene)

    assert codes(findings, "weights-unweighted")[0]["details"]["vertices"] == 8
    assert codes(findings, "weights-over-four-influences")[0]["details"]["maximum"] == 6


def test_asymmetric_shapes_are_reported(empty_scene):
    rig = armature(empty_scene, [
        ("spine", (0.0, 0.0, 0.0), (0.0, 0.0, 1.0), None),
        ("arm.l", (0.5, 0.0, 0.0), (1.5, 0.0, 0.0), "spine"),
        ("arm.r", (-0.5, 0.0, 0.0), (-1.5, 0.0, 0.0), "spine"),
    ])
    vertices, faces = tube(-1.5, 1.5, 30, 0.1)
    body = skinned_mesh(empty_scene, "Body", rig, vertices, faces,
                        lambda position: {"arm.l": 1.0} if position[0] > 0 else {"arm.r": 1.0})
    add_shape(body, "Both+", lambda index, position: (0.0, 0.0, 0.01))
    add_shape(body, "Both-", lambda index, position: (0.0, 0.0, -0.01))
    add_shape(body, "Left+", lambda index, position: (0.0, 0.0, 0.01) if position[0] > 0.6 else (0.0, 0.0, 0.0))
    add_shape(body, "Left-", lambda index, position: (0.0, 0.0, -0.01) if position[0] > 0.6 else (0.0, 0.0, 0.0))

    findings = codes(run(empty_scene, {"allowAsymmetric": ["Left-"]}), "shape-asymmetric")

    assert [item["details"]["shape"] for item in findings] == ["Left+"]
