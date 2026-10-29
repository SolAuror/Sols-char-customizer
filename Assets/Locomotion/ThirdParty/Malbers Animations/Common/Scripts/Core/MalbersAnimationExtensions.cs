using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MalbersAnimations
{
    public static class MalbersAnimationsExtensions
    {
        #region Types
        /// <summary>
        /// Checks if a given type inherits or implements a specified base type.
        /// </summary>
        /// <param name="type">The type which needs to be checked.</param>
        /// <param name="baseType">The base type/interface which is expected to be inherited or implemented by the 'type'</param>
        /// <returns>Return true if 'type' inherits or implements 'baseType'. False otherwise</returns>        
        public static bool InheritsOrImplements(this Type type, Type baseType)
        {
            type = ResolveGenericType(type);
            baseType = ResolveGenericType(baseType);

            while (type != typeof(object))
            {
                if (baseType == type || HasAnyInterfaces(type, baseType)) return true;

                type = ResolveGenericType(type.BaseType);
                if (type == null) return false;
            }

            return false;
        }

        private static Type ResolveGenericType(Type type)
        {
            if (type is not { IsGenericType: true }) return type;

            var genericType = type.GetGenericTypeDefinition();
            return genericType != type ? genericType : type;
        }

        private static bool HasAnyInterfaces(Type type, Type interfaceType)
        {
            return type.GetInterfaces().Any(i => ResolveGenericType(i) == interfaceType);
        }


        public static bool IsSubclassDeep(this Type type, Type parenType)
        {
            while (type != null)
            {
                if (type.IsSubclassOf(parenType))
                    return true;
                type = type.BaseType;
            }

            return false;
        }

        public static bool TryGetGenericTypeOfDefinition(this Type type, Type genericTypeDefinition,
            out Type genericType)
        {
            genericType = null;
            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition)
                {
                    genericType = type;
                    return true;
                }
                type = type.BaseType;
            }

            return false;
        }

        public static bool IsSubclassOfGenericTypeDefinition(this Type t, Type genericTypeDefinition)
        {
            if (!genericTypeDefinition.IsGenericTypeDefinition)
            {
                throw new Exception("genericTypeDefinition parameter isn't generic type definition");
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == genericTypeDefinition)
            {
                return true;
            }
            else
            {
                t = t.BaseType;
                while (t != null)
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == genericTypeDefinition)
                        return true;

                    t = t.BaseType;
                }
            }

            return false;
        }
        #endregion

        #region NullCheck
        /// <summary> Use this to check if a Unity Object is null or not. Usage:  [ a=b.Ref() ?? c; ] </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //: optimize very simple method call (especially in hot paths)
        public static T Ref<T>(this T o) where T : UnityEngine.Object
        {
            return o ?? null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] //: optimize very simple method call (especially in hot paths)
        public static T OrNull<T>(this T obj) where T : UnityEngine.Object => (bool)obj ? obj : null;

        /// <summary>  Use on custom C# types that are Unity objects to double check if the underlying Unity object is actually null or not.  </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //: optimize very simple method call (especially in hot paths)
        public static bool IsUnityRefNull<T>(this T o) where T : class
            => o == null || (o is UnityEngine.Object unityObj) && unityObj == null;
        #endregion

        #region Float Int
        public static bool MCompare(this float current, float newValue, ComparerNumber comparer)
        {
            return comparer switch
            {
                ComparerNumber.Equal => (current == newValue),
                ComparerNumber.Greater => (current > newValue),
                ComparerNumber.Less => (current < newValue),
                ComparerNumber.NotEqual => (current != newValue),
                ComparerNumber.GreaterEqual => (current >= newValue),
                ComparerNumber.LessEqual => (current <= newValue),
                _ => false,
            };
        }

        public static bool MCompare(this int current, int newValue, ComparerNumber comparer)
        {
            return comparer switch
            {
                ComparerNumber.Equal => (current == newValue),
                ComparerNumber.Greater => (current > newValue),
                ComparerNumber.Less => (current < newValue),
                ComparerNumber.NotEqual => (current != newValue),
                ComparerNumber.GreaterEqual => (current >= newValue),
                ComparerNumber.LessEqual => (current <= newValue),
                _ => false,
            };
        }

        public static bool CompareFloat(this float current, float newValue, ComparerInt comparer) => MCompare(current, newValue, (ComparerNumber)comparer); //Backwards Compatibility
        public static bool CompareInt(this int current, int newValue, ComparerInt comparer) => MCompare(current, newValue, (ComparerNumber)comparer); //Backwards Compatibility
        public static bool CompareInt(this int current, float newValue, ComparerInt comparer) => MCompare(current, newValue, (ComparerNumber)comparer); //Backwards Compatibility

        public static bool InRange(this float current, float min, float max) => current >= min && current <= max;
        public static bool InRange(this int current, float min, float max) => current >= min && current <= max;

        #endregion

        #region Vector3
        /// <summary>A useful Epsilon</summary>
        public const float Epsilon = 0.0001f;

        /// <summary>Round Decimal Places on a Vector</summary>
        public static Vector3 Round(this Vector3 vector3, int decimalPlaces = 2)
        {
            float multiplier = 1;
            for (int i = 0; i < decimalPlaces; i++)
            {
                multiplier *= 10f;
            }
            return new Vector3(
                Mathf.Round(vector3.x * multiplier) / multiplier,
                Mathf.Round(vector3.y * multiplier) / multiplier,
                Mathf.Round(vector3.z * multiplier) / multiplier);
        }

        /// <summary>
        /// Spread a direction by an angle, the spread angle is multiplied by the spreadMult for each axis, so you can have more spread on one axis than another. The spread is random between -spreadAngle and +spreadAngle for each axis.
        /// </summary>
        /// <param name="v3"> The direction to spread</param>
        /// <param name="spreadAngle"> The angle of the spread in degrees</param>
        /// <param name="spreadMult"> The multiplier for the spread angle for each axis, so you can have more spread on one axis than another</param>
        /// <returns> The spread direction</returns>
        public static Vector3 SpreadDirectionByAngle(this Vector3 v3, float spreadAngle, Vector3 spreadMult)
        {
            Quaternion spreadRotation =
                        Quaternion.Euler(
                         UnityEngine.Random.Range(-spreadAngle * spreadMult.x, spreadAngle * spreadMult.x),
                         UnityEngine.Random.Range(-spreadAngle * spreadMult.y, spreadAngle * spreadMult.y),
                         UnityEngine.Random.Range(-spreadAngle * spreadMult.z, spreadAngle * spreadMult.z));

            return spreadRotation * v3;
        }

        //MWC — deterministic counterpart of SpreadDirectionByAngle: instead of a random offset, the projectiles are
        //placed evenly around a cone ring (pattern is stable shot to shot, e.g. shotgun pellets, fan of arrows)
        /// <summary>
        /// Spread a direction evenly around a cone ring. Each index is placed at [spreadAngle] degrees from the original
        /// direction, rotated around it by (360 * index / total) degrees. The spreadMult scales the vertical (x) and
        /// horizontal (y) radius of the ring, so the ring can be turned into an ellipse or a flat line.
        /// </summary>
        /// <param name="v3"> The direction to spread </param>
        /// <param name="spreadAngle"> The angle of the spread in degrees (the radius of the cone) </param>
        /// <param name="spreadMult"> Multiplier for the vertical (x) and horizontal (y) radius of the ring. (z is unused) </param>
        /// <param name="index"> Index of the current element [0 - total) </param>
        /// <param name="total"> Total amount of elements spread around the ring </param>
        /// <returns> The spread direction (keeps the original magnitude) </returns>
        public static Vector3 SpreadDirectionEvenly(this Vector3 v3, float spreadAngle, Vector3 spreadMult, int index, int total)
        {
            if (total <= 1 || v3 == Vector3.zero) return v3; //A single element cannot form a ring

            var theta = 2 * Mathf.PI * index / total;               //Position around the ring in radians
            var pitch = Mathf.Cos(theta) * spreadAngle * spreadMult.x;
            var yaw = Mathf.Sin(theta) * spreadAngle * spreadMult.y;

            //Rotate on the direction's own space, so the ring is always built around it
            var local = Quaternion.LookRotation(v3) * Quaternion.Euler(pitch, yaw, 0);

            return local * Vector3.forward * v3.magnitude;
        }

        /// <summary>
        /// Spread a direction by an angle, the spread is random between -spreadAngle and +spreadAngle for each axis, and the spread angle is the same for all axes.
        /// </summary>
        /// <param name="v3"> The direction to spread</param>
        /// <param name="spreadAngle"> The angle of the spread in degrees</param>
        /// <returns> The spread direction</returns>
        public static Vector3 SpreadDirectionByAngle(this Vector3 v3, float spreadAngle)
        {
            Quaternion spreadRotation =
                        Quaternion.Euler(
                         UnityEngine.Random.Range(-spreadAngle, spreadAngle),
                         UnityEngine.Random.Range(-spreadAngle, spreadAngle),
                         UnityEngine.Random.Range(-spreadAngle, spreadAngle));

            return spreadRotation * v3;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static Vector3 FlattenY(this Vector3 origin) => new(origin.x, 0f, origin.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static Vector3 SetY(this Vector3 origin, float value) => new(origin.x, value, origin.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static Vector3 SetX(this Vector3 origin, float value) => new(value, origin.y, origin.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static Vector3 SetZ(this Vector3 origin, float value) => new(origin.x, origin.y, value);

        /// <summary>Checks if a vector is close to Vector3.zero</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static bool CloseToZero(this Vector3 v, float threshold = 0.0001f) => v.sqrMagnitude < threshold * threshold;

        /// <summary> Get the closest point on a line segment. </summary>
        /// <param name="p">A point in space</param>
        /// <param name="s0">Start of line segment</param>
        /// <param name="s1">End of line segment</param>
        /// <returns>The interpolation parameter representing the point on the segment, with 0==s0, and 1==s1</returns>
        public static Vector3 ClosestPointOnLine(this Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 aB = b - a;
            Vector3 aP = point - a;
            float sqrLenAB = aB.sqrMagnitude;

            if (sqrLenAB < Epsilon) return a;

            float t = Mathf.Clamp01(Vector3.Dot(aP, aB) / sqrLenAB);
            return a + (aB * t);
        }


        public static Vector3 ProjectPointOnPlane(this Vector3 point, Vector3 planeNormal, Vector3 planePoint)
        {
            float distance;
            Vector3 translationVector;

            //First calculate the distance from the point to the plane:
            distance = SignedDistancePlanePoint(planeNormal, planePoint, point);

            //Reverse the sign of the distance
            distance *= -1;

            //Get a translation vector
            translationVector = SetVectorLength(planeNormal, distance);

            //Translate the point to form a projection
            return point + translationVector;
        }

        public static float SignedDistancePlanePoint(Vector3 planeNormal, Vector3 planePoint, Vector3 point)
        {
            return Vector3.Dot(planeNormal, (point - planePoint));
        }

        //create a vector of direction "vector" with length "size"
        public static Vector3 SetVectorLength(Vector3 vector, float size)
        {

            //normalize the vector
            Vector3 vectorNormalized = Vector3.Normalize(vector);

            //scale the vector
            return vectorNormalized *= size;
        }

        /// <summary>Get the closest point (0-1) on a line segment</summary>
        /// <param name="p">A point in space</param>
        /// <param name="s0">Start of line segment</param>
        /// <param name="s1">End of line segment</param>
        /// <returns>The interpolation parameter representing the point on the segment, with 0==s0, and 1==s1</returns>
        public static float ClosestTimeOnSegment(this Vector3 p, Vector3 s0, Vector3 s1)
        {
            Vector3 s = s1 - s0;
            float len2 = Vector3.SqrMagnitude(s);
            if (len2 < Epsilon)
                return 0; // degenerate segment
            return Mathf.Clamp01(Vector3.Dot(p - s0, s) / len2);
        }


        /// <summary> Calculate the Direction from an Origin to a Target or Destination  </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static Vector3 DirectionTo(this Vector3 origin, Vector3 destination) => Vector3.Normalize(destination - origin);

        //CustomPatch: added extra useful vector math helper to avoid normalizing a vector that has its length already previously calculated
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 NormalizeWithLength(this Vector3 heading, float preCalculatedLength)
        {
            if (preCalculatedLength > 1E-05f)
                return heading / preCalculatedLength;

            return Vector3.zero;
        }

        /// <summary>returns the delta position from a rotation.</summary>
        public static Vector3 DeltaPositionFromRotate(this Transform transform, Vector3 point, Vector3 axis, float deltaAngle)
        {
            var pos = transform.position;
            var direction = pos - point;
            var rotation = Quaternion.AngleAxis(deltaAngle, axis);
            direction = rotation * direction;

            pos = point + direction - pos;
            pos.y = 0;                                                      //the Y is handled by the Fix Position method

            return pos;
        }

        /// <summary>returns the delta position from a rotation.</summary>
        public static Vector3 DeltaPositionFromRotate(this Transform transform, Vector3 platform, Quaternion deltaRotation)
        {
            var pos = transform.position;

            var direction = pos - platform;
            var directionAfterRotation = deltaRotation * direction;

            var NewPoint = platform + directionAfterRotation;


            pos = NewPoint - transform.position;

            return pos;
        }

        /// <summary>  Returns if a point is inside a Sphere Radius </summary>
        /// <param name="point">Point you want to find inside a sphere</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static bool PointInsideSphere(this Vector3 point, Vector3 sphereCenter, float sphereRadius)
        {
            Vector3 direction = point - sphereCenter;
            float distanceSquared = direction.sqrMagnitude;
            return (distanceSquared <= sphereRadius * sphereRadius);
        }


        #endregion

        #region Transforms

        /// <summary>
        /// Rotates a parent transform around its own pivot by a given angle and axis.
        /// Children will orbit the parent's center, but their world rotations remain strictly locked.
        /// </summary>
        public static void RotateAroundAxisLockChildRotations(this Transform parent, Vector3 axis, float angle, bool lockChild)
        {
            int childCount = parent.childCount;

            // 1. Calculate the delta rotation quaternion
            Quaternion deltaRotation = Quaternion.AngleAxis(angle, axis);

            // Fast exit for leaf nodes (no children to manage)
            if (childCount == 0 || !lockChild)
            {
                parent.rotation = deltaRotation * parent.rotation;
                return;
            }


            // 2. Cache child world rotations on the stack (Zero GC allocation)
            Span<Quaternion> childRotations = stackalloc Quaternion[childCount];
            for (int i = 0; i < childCount; i++)
            {
                childRotations[i] = parent.GetChild(i).rotation;
            }

            // 3. Apply the rotation mathematically to the parent's pivot
            // Multiplying deltaRotation * parent.rotation applies it in World Space.
            // To apply in Local Space, use: parent.rotation = parent.rotation * deltaRotation;
            parent.rotation = deltaRotation * parent.rotation;

            // 4. Restore the children to their original world orientations.
            // Their positions have naturally updated due to the parent's rotation.
            for (int i = 0; i < childCount; i++)
            {
                parent.GetChild(i).rotation = childRotations[i];
            }
        }





        /// <summary>
        /// Rotates a parent transform while keeping all children locked in their 
        /// original world position and rotation. Zero GC allocation.
        /// </summary>
        public static void RotateLockChildren(this Transform parent, Quaternion deltaRotation, bool lockChild)
        {
            int childCount = parent.childCount;

            // Fast exit for leaf nodes (no children to manage)
            if (childCount == 0 || !lockChild)
            {
                parent.rotation *= deltaRotation;
                return;
            }

            // 2. Cache child world rotations on the stack (Zero GC allocation)
            Span<Quaternion> childRotations = stackalloc Quaternion[childCount];
            for (int i = 0; i < childCount; i++)
            {
                childRotations[i] = parent.GetChild(i).rotation;
            }


            // 1. Apply the rotation to the parent
            parent.rotation *= deltaRotation;


            // Restore the children to their original world orientations.
            // Their positions have naturally updated due to the parent's rotation.
            for (int i = 0; i < childCount; i++)
            {
                parent.GetChild(i).rotation = childRotations[i];
            }
        }


        // MWC — New. [RotateLockChildren] treats its argument as a DELTA (parent.rotation *= delta), so callers that
        // already computed the final WORLD rotation had no child-locking overload and were silently squaring the pose.
        /// <summary> Sets the parent WORLD rotation while keeping all children on their original world rotations. Zero GC allocation </summary>
        public static void SetRotationLockChildren(this Transform parent, Quaternion worldRotation, bool lockChild)
        {
            int childCount = parent.childCount;

            // Fast exit for leaf nodes (no children to manage)
            if (childCount == 0 || !lockChild)
            {
                parent.rotation = worldRotation;
                return;
            }

            // Cache child world rotations on the stack (Zero GC allocation)
            Span<Quaternion> childRotations = stackalloc Quaternion[childCount];
            for (int i = 0; i < childCount; i++)
            {
                childRotations[i] = parent.GetChild(i).rotation;
            }

            parent.rotation = worldRotation;

            // Restore the children to their original world orientations.
            // Their positions have naturally updated due to the parent's rotation.
            for (int i = 0; i < childCount; i++)
            {
                parent.GetChild(i).rotation = childRotations[i];
            }
        }


        /// <summary>
        /// Rotates a parent transform, allowing children to orbit, but reverses the rotation
        /// on the children so they maintain their original world orientation.
        /// </summary>
        public static void OrbitWithoutRotatingChildren(this Transform parent, Quaternion deltaRotation)
        {
            int childCount = parent.childCount;

            // Safety check: stackalloc is incredibly fast but has a size limit. 
            // 100 children is well within safe stack limits for a struct like Quaternion.
            if (childCount == 0) return;

            // Use stack allocation to avoid GC spikes in Update loops
            Span<Quaternion> childRotations = stackalloc Quaternion[childCount];

            // 1. Cache the original world rotations
            for (int i = 0; i < childCount; i++)
            {
                childRotations[i] = parent.GetChild(i).rotation;
            }

            // 2. Apply rotation to parent
            parent.rotation *= deltaRotation;

            // 3. Restore world rotations to the children
            for (int i = 0; i < childCount; i++)
            {
                parent.GetChild(i).rotation = childRotations[i];
            }
        }


        public static string[] SplitByUpperCase(this string value)
        {
            return Regex.Split(value, @"(?=[A-Z])");
        }

        public static Transform FindGrandChild(this Transform aParent, params string[] names)
        {
            foreach (Transform child in aParent)
            {
                string childNameLower = child.name.ToLower();
                bool matchesAll = true;

                foreach (string name in names)
                {
                    if (!childNameLower.Contains(name.ToLower()))
                    {
                        matchesAll = false;
                        break;
                    }
                }

                if (matchesAll) return child;

                Transform found = child.FindGrandChild(names);
                if (found != null) return found;
            }

            return null;
        }

        public static Transform FindGrandChildinSkin(this Transform aParent, params string[] names)
        {
            var SkinMeshrenderers = aParent.GetComponentsInChildren<SkinnedMeshRenderer>();

            var transforms = SkinMeshrenderers.SelectMany(smr => smr.bones).ToArray();

            foreach (Transform child in transforms)
            {
                string childNameLower = child.name.ToLower();
                bool matchesAll = true;
                foreach (string name in names)
                {
                    if (!childNameLower.Contains(name.ToLower()))
                    {
                        matchesAll = false;
                        break;
                    }
                }
                if (matchesAll) return child;
            }
            return null;
        }

        /// <summary>Returns the Real Transform Core</summary> 
        public static Transform FindObjectCore(this Transform transf)
        {
            var core = transf;
            var IsObjectCore = core.FindInterface<IObjectCore>();
            if (IsObjectCore != null) return IsObjectCore.transform;

            return core;
        }


        /// <summary> Find the if a Transform is in the same hierarchy(grandchild) of a parent. Returns true also if the Child = Parent</summary>
        public static bool SameHierarchy(this Transform child, Transform parent)
        {
            if (child == parent) return true; //Include yourself!! IMPORTANT
            if (child.parent == null) return false;
            if (child.parent == parent) return true;

            return SameHierarchy(child.parent, parent);
        }

        /// <summary> Calculate the Direction from an Origin to a Target or Destination  </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static Vector3 DirectionTo(this Transform origin, Transform destination) => DirectionTo(origin.position, destination.position);
        /// <summary> Calculate the Direction from an Origin to a Target or Destination  </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static Vector3 DirectionTo(this Transform origin, Vector3 destination) => DirectionTo(origin.position, destination);

        /// <summary> Find the closest transform from the origin </summary>
        public static Transform NearestTransform(this Transform origin, params Transform[] transforms)
        {
            Transform bestTarget = null;
            float closestDistanceSqr = Mathf.Infinity;
            Vector3 currentPosition = origin.position;
            foreach (Transform potentialTarget in transforms)
            {
                Vector3 directionToTarget = potentialTarget.position - currentPosition;
                float dSqrToTarget = directionToTarget.sqrMagnitude;
                if (dSqrToTarget < closestDistanceSqr)
                {
                    closestDistanceSqr = dSqrToTarget;
                    bestTarget = potentialTarget;
                }
            }

            return bestTarget;
        }

        /// <summary> Find the closest point from a transform </summary>
        public static Vector3 NearestPoint(this Transform origin, params Vector3[] allPoints)
        {
            Vector3 nearest = Vector3.zero;
            float closestDistanceSqr = Mathf.Infinity;
            Vector3 currentPosition = origin.position;
            foreach (var point in allPoints)
            {
                float dSqrToTarget = (point - currentPosition).sqrMagnitude;

                if (dSqrToTarget < closestDistanceSqr)
                {
                    closestDistanceSqr = dSqrToTarget;
                    nearest = point;
                }
            }
            return nearest;
        }

        /// <summary> Find the Furthest transform from the origin </summary>
        public static Transform FurthestTransform(this Transform t, params Transform[] transforms)
        {
            Transform bestTarget = null;
            float closestDistanceSqr = Mathf.Infinity;
            Vector3 currentPosition = t.position;
            foreach (Transform potentialTarget in transforms)
            {
                Vector3 directionToTarget = potentialTarget.position - currentPosition;
                float dSqrToTarget = directionToTarget.sqrMagnitude;
                if (dSqrToTarget > closestDistanceSqr)
                {
                    closestDistanceSqr = dSqrToTarget;
                    bestTarget = potentialTarget;
                }
            }

            return bestTarget;
        }

        public static Transform ChildContainsName(this Transform aParent, params string[] aName)
        {
            Transform result = null;

            foreach (var name in aName)
            {
                foreach (Transform child in aParent)
                {
                    if (child.name.ToLower().Contains(name.ToLower()))
                    {
                        result = child;
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>Resets the Local Position and rotation of a transform</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static void ResetLocal(this Transform transform)
        {
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        /// <summary>Resets the Local Position and rotation of a transform</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: added compiler optimization taken also into account by IL2CPP
        public static void SetLocalTransform(this Transform transform, Vector3 LocalPos, Vector3 LocalRot, Vector3 localScale)
        {
            transform.localPosition = LocalPos;
            transform.localEulerAngles = LocalRot;
            transform.localScale = localScale;
        }

        /// <summary>Parent a transform to another Transform, and Solves the Scale problem in case the Parent has a deformed scale  </summary>
        /// <param name="parent">Transform to be the parent</param>
        /// <param name="Position">Relative position to the Parent (World Position)</param>
        public static Transform SetParentScaleFixer(this Transform transform, Transform parent, Vector3 Position, GameObject Link = null)
        {
            Vector3 NewScale = parent.transform.lossyScale;
            NewScale.x = 1f / Mathf.Max(NewScale.x, Epsilon);
            NewScale.y = 1f / Mathf.Max(NewScale.y, Epsilon);
            NewScale.z = 1f / Mathf.Max(NewScale.z, Epsilon);

            //CustomPatch: modified below Link creation to add some optimizations
            Transform linkTransform = null;
            //Create a new Link if is not created already.
            if (Link == null)
            {
                //ASdded a standard name for the link and cached transform for repeated operations below (even if Unity now uses a cache for the transform internally,
                //it still has an extra cost because marshaling/unmarshaling when calling native c++ engine methods that add up quickly on mobile devices)
                Link = new("AC-HierarchyLink");
                Link.AddComponent<HierarchyLink>(); //MWC: hierarchy searches (E.g. the projectile's closest-bone walk) must skip this graft
                linkTransform = Link.transform;
                //Removed extra memory allocation for non development builds
#if UNITY_EDITOR || MALBERS_DEBUG  
                Link.name = transform.name + "Link";
#endif
            }
            else
                linkTransform = Link.transform;

            linkTransform.SetParent(parent);
            linkTransform.localScale = NewScale;
            linkTransform.position = Position;
            linkTransform.localRotation = Quaternion.identity;

            transform.SetParent(linkTransform);
            transform.localPosition = Vector3.zero;
            return linkTransform;
        }

        /// <summary>Returns the hash value of a parameter if it exists on the Animator; otherwise, returns 0.</summary>
        public static int TryOptionalParameter(this Animator animator, string param)
        {
            if (string.IsNullOrEmpty(param) || animator == null) return 0;

            int animHash = Animator.StringToHash(param);

            // Use a for loop for better performance (avoid foreach allocation in old Unity versions)
            var parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == animHash)
                    return animHash;
            }
            return 0;
        }


        #endregion

        #region String
        public static string RemoveSpecialCharacters(this string str)
        {
            System.Text.StringBuilder sb = new(); //CustomPatch: TODO: use Unity's Pooling API to get a pooled string builders (will get back to this)
            foreach (char c in str)
            {
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        #endregion

        #region GameObjects
        /// <summary>The GameObject is a prefab, Meaning in not in any scene</summary>
        public static bool IsPrefab(this GameObject go) => !go.scene.IsValid();
        #endregion

        #region Delay Action

        ///// <summary> Same as StartCoroutine but if the coroutine was playing it stop it. it also stores the new coroutine in the IEnumerator </summary>
        //public static void StartCoroutine(this MonoBehaviour Mono, ref IEnumerator Cor, IEnumerator newCoro)
        //{
        //    if (Cor != null) //If there is a Coroutine running stop it
        //    {
        //        Mono.StopCoroutine(Cor);
        //    }
        //    Cor = null;

        //    if (Mono.gameObject.activeInHierarchy)
        //    {
        //        Cor = newCoro;
        //        Mono.StartCoroutine(Cor);
        //    }
        //}

        /// <summary>Do an action the next frame</summary>
        public static IEnumerator Delay_Action(this MonoBehaviour mono, Action action) => Delay_Action(mono, 1, action);

        /// <summary>Do an action the next given frames</summary>
        public static IEnumerator Delay_Action(this MonoBehaviour mono, int frames, Action action)
        {
            if (mono.enabled && mono.gameObject.activeInHierarchy)
            {
                var coro = DelayedAction(frames, action);
                mono.StartCoroutine(coro);

                return coro;
            }
            return null;
        }

        /// <summary>If the action is active stop it!</summary>
        public static void Stop_Action(this MonoBehaviour mono, IEnumerator action)
        {
            if (action != null) mono.StopCoroutine(action);
        }



        /// <summary>Do an action after certain time</summary>
        public static IEnumerator Delay_Action(this MonoBehaviour mono, float time, Action action)
        {
            if (mono.enabled && mono.gameObject.activeInHierarchy)
            {
                var coro = DelayedAction(time, action);
                mono.StartCoroutine(coro);

                return coro;
            }
            return null;
        }

        /// <summary>Do an action after certain time and stop an old one</summary>
        public static void Delay_Action(this MonoBehaviour mono, ref IEnumerator oldAction, float time, Action action)
        {
            if (oldAction != null) mono.StopCoroutine(oldAction);
            oldAction = Delay_Action(mono, time, action);
        }

        public static IEnumerator Delay_Action(this MonoBehaviour mono, Func<bool> Condition, Action action)
        {
            if (mono.enabled && mono.gameObject.activeInHierarchy)
            {
                var coro = DelayedAction(Condition, action);
                mono.StartCoroutine(coro);

                return coro;
            }
            return null;
        }

        public static IEnumerator Delay_Action(this MonoBehaviour mono, WaitForSeconds time, Action action)
        {
            if (mono.enabled && mono.gameObject.activeInHierarchy)
            {
                var coro = DelayedAction(time, action);
                mono.StartCoroutine(coro);

                return coro;
            }
            return null;
        }

        private static IEnumerator DelayedAction(int frame, Action action)
        {
            for (int i = 0; i < frame; i++)
                yield return null;

            action.Invoke();
        }


        private static IEnumerator DelayedAction(Func<bool> Condition, Action action)
        {
            yield return new WaitWhile(Condition);
            action.Invoke();
        }

        //CustomPatch: TODO: HIDDEN BUG (already causing issue with projectiles: if time == 0f there should be NO 1 frame yielding like it happens now => fixing it now could create issues for logic that relies on this extra frame without knowing
        private static IEnumerator DelayedAction(float time, Action action)
        {
            yield return new WaitForSeconds(time);
            action.Invoke();
        }

        private static IEnumerator DelayedAction(WaitForSeconds time, Action action)
        {
            yield return time;
            action.Invoke();
        }

        #endregion

        #region Components/Interfaces
        public static T CopyComponent<T>(this T original, GameObject destination) where T : Component
        {
            Type type = original.GetType();

            Component copy = destination.AddComponent(type);

            var fields = type.GetFields();

            foreach (System.Reflection.FieldInfo field in fields)
                field.SetValue(copy, field.GetValue(original));

            return copy as T;
        }

        public static T FindComponent<T>(this GameObject c) where T : Component
        {
            if (c.TryGetComponent<T>(out var Ttt)) return Ttt;

            Ttt = c.GetComponentInParent<T>();
            if (Ttt != null) return Ttt;

            Ttt = c.GetComponentInChildren<T>(true);
            if (Ttt != null) return Ttt;

            return default;
        }

        public static Component FindComponent(this GameObject c, Type t)
        {
            if (c.TryGetComponent(t, out var Ttt)) return Ttt;

            Ttt = c.GetComponentInParent(t);
            if (Ttt != null) return Ttt;

            Ttt = c.GetComponentInChildren(t, true);
            if (Ttt != null) return Ttt;

            return default;
        }

        public static T[] FindComponents<T>(this GameObject c) where T : Component
        {
            T[] Ttt = c.GetComponents<T>();
            if (Ttt != null) return Ttt;

            Ttt = c.GetComponentsInParent<T>();
            if (Ttt != null) return Ttt;

            Ttt = c.GetComponentsInChildren<T>(true);
            if (Ttt != null) return Ttt;

            return default;
        }

        /// <summary>Search for the Component in the root of the Object </summary>
        public static T MFindComponentInRoot<T>(this GameObject c) where T : Component
        {
            var root = c.transform.root;

            if (root.TryGetComponent<T>(out var Ttt)) return Ttt;

            Ttt = c.GetComponentInParent<T>();
            if (Ttt != null) return Ttt;

            Ttt = root.GetComponentInChildren<T>(true);
            if (Ttt != null) return Ttt;

            return default;
        }

        public static T FindInterface<T>(this GameObject c)
        {
            if (c.TryGetComponent<T>(out var Ttt)) return Ttt;

            Ttt = c.GetComponentInParent<T>(true);
            if (Ttt != null) return Ttt;

            Ttt = c.GetComponentInChildren<T>(true);
            if (Ttt != null) return Ttt;

            return default;
        }

        public static T FindInterface<T>(this GameObject c, bool includeInactive)
        {
            if (c.TryGetComponent<T>(out var Ttt)) return Ttt;

            Ttt = c.GetComponentInParent<T>(includeInactive);
            if (Ttt != null) return Ttt;

            Ttt = c.GetComponentInChildren<T>(includeInactive);
            if (Ttt != null) return Ttt;

            return default;
        }

        /// <summary>  Gets the component if it exists, or adds it if it doesn't.  </summary>
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            if (!go.TryGetComponent<T>(out var comp))
                comp = go.AddComponent<T>();
            return comp;
        }

        /// <summary>  Gets the component if it exists, or adds it if it doesn't.  </summary>
        public static T GetOrAddComponent<T>(this Transform go) where T : Component
        {
            if (!go.TryGetComponent<T>(out var comp))
                comp = go.gameObject.AddComponent<T>();
            return comp;
        }

        public static T[] FindInterfaces<T>(this GameObject c)
        {
            T[] Ttt = c.GetComponents<T>();
            if (Ttt != null && Ttt.Length > 0) return Ttt;

            Ttt = c.GetComponentsInParent<T>();
            if (Ttt != null && Ttt.Length > 0) return Ttt;

            Ttt = c.GetComponentsInChildren<T>(true);
            if (Ttt != null && Ttt.Length > 0) return Ttt;

            return default;
        }

        /// <summary>Search for the Component in the hierarchy Up or Down</summary>
        public static T FindComponent<T>(this Component c) where T : Component => c.gameObject.FindComponent<T>();

        public static T FindInterface<T>(this Component c) => c.gameObject.FindInterface<T>();
        public static T FindInterface<T>(this Component c, bool includeInactive) => c.gameObject.FindInterface<T>(includeInactive);
        public static T[] FindInterfaces<T>(this Component c) => c.gameObject.FindInterfaces<T>();

        /// <summary>Search for the Component in the root of the Object </summary>
        public static T MFindComponentInRoot<T>(this Component c) where T : Component => c.gameObject.MFindComponentInRoot<T>();

        /// <summary>  Reset the delta RootMotion of the Animator  </summary>
        public static IDeltaRootMotion TryResetDeltaRootMotion(this Component c)
        {
            if (c.TryGetComponent(out IDeltaRootMotion target))
            {
                target.ResetDeltaRootMotion();
                return target;
            }
            return null;
        }


        public static IDeltaRootMotion TryResetDeltaRootMotion(this Component c, ref IDeltaRootMotion cachedDeltaRootMotion)
        {
            if (!cachedDeltaRootMotion.IsUnityRefNull() || c.TryGetComponent(out cachedDeltaRootMotion))
            {
                cachedDeltaRootMotion.ResetDeltaRootMotion();
                return cachedDeltaRootMotion;
            }
            return null;
        }

        #endregion

        /// <summary>  Checks if a GameObject has been destroyed. </summary>
        /// <param name="gameObject">GameObject reference to check for destructedness</param>
        /// <returns>If the game object has been marked as destroyed by UnityEngine</returns>
        public static bool IsDestroyed(this GameObject gameObject)
        {
            // UnityEngine overloads the == operator for the GameObject type
            // and returns null when the object has been destroyed, but 
            // actually the object is still there but has not been cleaned up yet
            // if we test both we can determine if the object has been destroyed.
            return gameObject == null && !ReferenceEquals(gameObject, null);
        }

        #region Layers and Colliders
        /// <summary> Changes the Layer of a GameObject and its children.  </summary>
        public static void SetLayer(this GameObject parent, int layer, bool includeChildren = true)
        {
            parent.layer = layer;
            if (includeChildren)
            {
                foreach (var trans in parent.transform.GetComponentsInChildren<Transform>(true))
                    trans.gameObject.layer = layer;
            }
        }

        #endregion

        #region SetEnable
        /// <summary>Enable disable the Mono</summary>
        public static void SetEnable(this MonoBehaviour c, bool enable) => c.enabled = enable;
        /// <summary>Enable disable the Mono</summary>
        public static void SetEnable(this Collider c, bool enable) => c.enabled = enable;
        #endregion
    }
}