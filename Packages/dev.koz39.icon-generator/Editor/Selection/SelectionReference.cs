using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KOZ39.IconGenerator
{
    internal enum ReferenceState
    {
        Available,
        Deleted,
        Unloaded,
        Invalid,
    }

    internal sealed class SelectionReference
    {
        private const string SessionPrefix = "session:";
        internal string Id;
        internal GameObject Target;
        internal ReferenceState State;
        internal string Label;

        internal void Refresh()
        {
            if (Target != null)
            {
                UpdateAvailableState();
                return;
            }

            var resolved = Resolve(Id);
            Id = resolved.Id;
            Target = resolved.Target;
            State = resolved.State;
            Label = resolved.Label;
        }

        private void UpdateAvailableState()
        {
            Id = GetId(Target);
            State = ReferenceState.Available;
            Label = HierarchyPath(Target.transform);
        }

        internal static string GetId(GameObject target)
        {
            if (target == null)
            {
                return "";
            }

            var id = GlobalObjectId.GetGlobalObjectIdSlow(target);

            return id.assetGUID.ToString() != new string('0', 32) && id.targetObjectId != 0
                ? id.ToString()
                : $"{SessionPrefix}{target.GetInstanceID()}";
        }

        internal static bool IsSessionId(string id) =>
            id.StartsWith(SessionPrefix, StringComparison.Ordinal);

        internal static SelectionReference For(GameObject target)
        {
            var reference = new SelectionReference { Target = target };
            reference.UpdateAvailableState();

            return reference;
        }

        internal static SelectionReference Resolve(string value)
        {
            var result = new SelectionReference
            {
                Id = value,
                State = ReferenceState.Invalid,
                Label = value,
            };

            if (IsSessionId(value))
            {
                if (!int.TryParse(value.Substring(SessionPrefix.Length), out var instanceId))
                {
                    return result;
                }

                result.Target = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                result.State =
                    result.Target == null ? ReferenceState.Deleted : ReferenceState.Available;
            }
            else
            {
                if (!GlobalObjectId.TryParse(value, out var id))
                {
                    return result;
                }

                var path = AssetDatabase.GUIDToAssetPath(id.assetGUID.ToString());

                if (string.IsNullOrEmpty(path))
                {
                    result.State = ReferenceState.Deleted;

                    return result;
                }

                result.Label = path;

                if (
                    path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                    && !IsSceneLoaded(path)
                )
                {
                    result.State = ReferenceState.Unloaded;

                    return result;
                }

                result.Target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                result.State =
                    result.Target == null ? ReferenceState.Deleted : ReferenceState.Available;
            }

            if (result.Target != null)
            {
                result.UpdateAvailableState();
            }

            return result;
        }

        private static bool IsSceneLoaded(string path)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);

                if (scene.isLoaded && scene.path == path)
                {
                    return true;
                }
            }

            return false;
        }

        internal static string HierarchyPath(Transform target) =>
            AnimationUtility.CalculateTransformPath(target, null);
    }
}
