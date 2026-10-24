using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTools
{
    // Imports .solrig sidecars written by Tools/Blender into a joint offset table,
    // and surfaces the exporter's check findings as import warnings.
    [ScriptedImporter(1, "solrig")]
    public sealed class CharacterRigSidecarImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string json = File.ReadAllText(ctx.assetPath);
            if (!CharacterJointOffsetTable.TryCreate(json, out CharacterJointOffsetTable table, out string error))
            {
                ctx.LogImportError($"{Path.GetFileName(ctx.assetPath)}: {error}");
                return;
            }

            table.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("joint-offsets", table);
            ctx.SetMainObject(table);

            var report = new StringBuilder();
            int reported = 0;
            foreach (CharacterJointOffsetTable.JointCheck check in table.Checks)
            {
                if (check.Severity != "error" && check.Severity != "warning")
                {
                    continue;
                }

                reported++;
                report.AppendLine($"[{check.Severity}] {check.Code}: {check.Message}");
            }

            if (reported > 0)
            {
                ctx.LogImportWarning(
                    $"{table.name}: the Blender exporter reported {reported} issues. Fix them in the .blend and re-export.\n{report}",
                    table);
            }
        }
    }
}
