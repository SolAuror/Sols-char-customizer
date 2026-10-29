// Last Change: 9/17/2026 5:33pm
namespace MalbersAnimations.Items
{
    [System.Serializable, AddTypeMenu("Animal/Strafe")]
    [UnityEngine.HelpURL(ItemDocs.Processors + "animal-strafe")]
    public class ItemProcessorStrafe : ItemProcessor
    {
        public bool strafe = true;
        public override string DynamicName => $"Strafe [{(strafe ? "On" : "Off")}] ";

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            item.User.Animal.Strafe = strafe;
        }


        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            return string.Empty;
        }
    }
}