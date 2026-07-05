using UnityEngine;
public class BoneOffsetProbe : MonoBehaviour
{
    [SerializeField] private Transform[] bones;

    [ContextMenu("Log Bone Locals")]
    private void LogBoneLocals()
    {
        foreach (Transform bone in bones)
        {
            if (bone == null)
            {
                continue;
            }

            Debug.Log(
                $"{bone.name} localPos={bone.localPosition} localRot={bone.localRotation.eulerAngles} localScale={bone.localScale}",
                bone);
        }
    }
}