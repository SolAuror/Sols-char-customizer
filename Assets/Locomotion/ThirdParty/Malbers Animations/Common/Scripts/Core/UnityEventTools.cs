// Last Change: 9/17/2026 6:05pm
using UnityEngine.Events;

namespace MalbersAnimations
{
    /// <summary>Helpers for UnityEvents authored in the Inspector</summary>
    public static class UnityEventTools
    {
        private const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        private static readonly System.Reflection.FieldInfo PersistentCalls =
            typeof(UnityEventBase).GetField("m_PersistentCalls", Flags);

        private static System.Reflection.FieldInfo callList, argumentCache, objectArgument;

        /// <summary> Replaces the fake-null Object arguments of an authored UnityEvent with REAL nulls.
        /// An empty [None] Object slot deserializes as a live UnityEngine.Object instance with no native object
        /// behind it. Unity's own PersistentCall.GetObjectCall guards it with (castedObject != null), which is
        /// Object's OVERLOADED ==, so the fake-null reads as null, slips past the guard and is handed to the
        /// CachedInvokableCall&lt;T&gt; constructor, which throws:
        /// "Object of type 'UnityEngine.Object' cannot be converted to type 'T'".
        /// Only [Runtime Only] calls are ever built, so this never shows up until Play Mode.
        /// Call it once BEFORE the first Invoke of an event whose Inspector arguments may be left empty. </summary>
        public static void FixNullObjectArguments(UnityEventBase unityEvent)
        {
            if (unityEvent == null || PersistentCalls == null) return;

            var group = PersistentCalls.GetValue(unityEvent);
            if (group == null) return;

            callList ??= group.GetType().GetField("m_Calls", Flags);

            if (callList?.GetValue(group) is not System.Collections.IEnumerable calls) return;

            foreach (var call in calls)
            {
                if (call == null) continue;

                argumentCache ??= call.GetType().GetField("m_Arguments", Flags);

                var arguments = argumentCache?.GetValue(call);
                if (arguments == null) continue;

                objectArgument ??= arguments.GetType().GetField("m_ObjectArgument", Flags);
                if (objectArgument == null) return;

                var current = objectArgument.GetValue(arguments);

                //ReferenceEquals is the only way to see a fake-null: Object's == reports it as null
                if (!ReferenceEquals(current, null) && current as UnityEngine.Object == null)
                    objectArgument.SetValue(arguments, null);
            }
        }
    }
}
