using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Per-shape joint offsets exported by Tools/Blender (a .solrig sidecar). Offsets
    // and rest bone heads are in the source armature's axes, in metres; the joint
    // offset binding maps them onto the imported skeleton.
    public sealed class CharacterJointOffsetTable : ScriptableObject
    {
        public const string SidecarFormat = "sol-character-sidecar";
        public const int SupportedVersion = 2;

        [SerializeField] private string source;
        [SerializeField] private string body;
        [SerializeField] private int bodyVertexCount;
        [SerializeField] private List<JointBone> bones = new();
        [SerializeField] private List<JointShape> shapes = new();
        [SerializeField] private List<JointCheck> checks = new();

        [NonSerialized] private Dictionary<string, int> shapeLookup;

        public string Source => source;
        public string Body => body;
        public int BodyVertexCount => bodyVertexCount;
        public IReadOnlyList<JointBone> Bones => bones;
        public IReadOnlyList<JointShape> Shapes => shapes;
        public IReadOnlyList<JointCheck> Checks => checks;

        public static bool TryCreate(string json, out CharacterJointOffsetTable table, out string error)
        {
            table = CreateInstance<CharacterJointOffsetTable>();
            if (table.TryLoad(json, out error))
            {
                return true;
            }

            DestroyImmediate(table);
            table = null;
            return false;
        }

        public bool TryGetShape(string shapeName, out JointShape shape)
        {
            if (shapeLookup == null)
            {
                shapeLookup = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int index = 0; index < shapes.Count; index++)
                {
                    shapeLookup[shapes[index].Name] = index;
                }
            }

            if (shapeName != null && shapeLookup.TryGetValue(shapeName, out int found))
            {
                shape = shapes[found];
                return true;
            }

            shape = null;
            return false;
        }

        public bool TryLoad(string json, out string error)
        {
            SidecarData data;
            try
            {
                data = JsonUtility.FromJson<SidecarData>(json);
            }
            catch (ArgumentException exception)
            {
                error = $"The sidecar is not valid JSON: {exception.Message}";
                return false;
            }

            if (data == null || data.format != SidecarFormat)
            {
                error = $"Not a {SidecarFormat} file.";
                return false;
            }

            if (data.version != SupportedVersion)
            {
                error = $"Sidecar version {data.version} is not supported; re-export it with Tools/Blender (version {SupportedVersion}).";
                return false;
            }

            source = data.source;
            body = data.body;
            bodyVertexCount = data.bodyVertexCount;
            bones = new List<JointBone>();
            shapes = new List<JointShape>();
            checks = new List<JointCheck>();
            shapeLookup = null;

            foreach (BoneData bone in data.bones ?? Array.Empty<BoneData>())
            {
                if (bone.head == null || bone.head.Length != 3)
                {
                    error = $"Bone '{bone.name}' has no rest head.";
                    return false;
                }

                bones.Add(new JointBone(bone.name, bone.parent, new Vector3(bone.head[0], bone.head[1], bone.head[2]), bone.method));
            }

            foreach (ShapeData shape in data.shapes ?? Array.Empty<ShapeData>())
            {
                int[] indices = shape.boneIndices ?? Array.Empty<int>();
                float[] values = shape.offsets ?? Array.Empty<float>();
                if (values.Length != indices.Length * 3)
                {
                    error = $"Shape '{shape.name}' has {indices.Length} bones but {values.Length} offset values.";
                    return false;
                }

                var offsets = new Vector3[indices.Length];
                for (int index = 0; index < indices.Length; index++)
                {
                    if (indices[index] < 0 || indices[index] >= bones.Count)
                    {
                        error = $"Shape '{shape.name}' references bone {indices[index]}, but there are {bones.Count}.";
                        return false;
                    }

                    offsets[index] = new Vector3(values[index * 3], values[index * 3 + 1], values[index * 3 + 2]);
                }

                shapes.Add(new JointShape(shape.name, shape.meshes ?? Array.Empty<string>(), indices, offsets));
            }

            foreach (CheckData check in data.checks ?? Array.Empty<CheckData>())
            {
                checks.Add(new JointCheck(check.severity, check.code, check.message));
            }

            error = null;
            return true;
        }

        [Serializable]
        public sealed class JointBone
        {
            [SerializeField] private string name;
            [SerializeField] private string parent;
            [SerializeField] private Vector3 restHead;
            [SerializeField] private string method;

            public JointBone()
            {
            }

            public JointBone(string name, string parent, Vector3 restHead, string method)
            {
                this.name = name;
                this.parent = parent;
                this.restHead = restHead;
                this.method = method;
            }

            public string Name => name;
            public string Parent => parent;
            public Vector3 RestHead => restHead;
            public string Method => method;
        }

        [Serializable]
        public sealed class JointShape
        {
            [SerializeField] private string name;
            [SerializeField] private string[] meshes;
            [SerializeField] private int[] boneIndices;
            [SerializeField] private Vector3[] offsets;

            public JointShape()
            {
            }

            public JointShape(string name, string[] meshes, int[] boneIndices, Vector3[] offsets)
            {
                this.name = name;
                this.meshes = meshes;
                this.boneIndices = boneIndices;
                this.offsets = offsets;
            }

            public string Name => name;
            public IReadOnlyList<string> Meshes => meshes;
            public IReadOnlyList<int> BoneIndices => boneIndices;
            public IReadOnlyList<Vector3> Offsets => offsets;
        }

        [Serializable]
        public sealed class JointCheck
        {
            [SerializeField] private string severity;
            [SerializeField] private string code;
            [SerializeField] private string message;

            public JointCheck()
            {
            }

            public JointCheck(string severity, string code, string message)
            {
                this.severity = severity;
                this.code = code;
                this.message = message;
            }

            public string Severity => severity;
            public string Code => code;
            public string Message => message;
        }

        // JsonUtility shapes of the sidecar file. Fields the table does not use, such
        // as check details, are ignored.
#pragma warning disable 0649
        [Serializable]
        private sealed class SidecarData
        {
            public string format;
            public int version;
            public string source;
            public string body;
            public int bodyVertexCount;
            public BoneData[] bones;
            public ShapeData[] shapes;
            public CheckData[] checks;
        }

        [Serializable]
        private sealed class BoneData
        {
            public string name;
            public string parent;
            public float[] head;
            public string method;
        }

        [Serializable]
        private sealed class ShapeData
        {
            public string name;
            public string[] meshes;
            public int[] boneIndices;
            public float[] offsets;
        }

        [Serializable]
        private sealed class CheckData
        {
            public string severity;
            public string code;
            public string message;
        }
#pragma warning restore 0649
    }
}
