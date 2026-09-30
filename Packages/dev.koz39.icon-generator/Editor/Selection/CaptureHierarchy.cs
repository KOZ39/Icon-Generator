using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal static class CaptureHierarchy
    {
        internal static bool HasMesh(Renderer renderer) =>
            renderer is SkinnedMeshRenderer skin
                ? skin.sharedMesh != null
                : renderer is MeshRenderer
                    && renderer.TryGetComponent<MeshFilter>(out var filter)
                    && filter.sharedMesh != null;

        internal static bool IsMeshRenderer(Renderer renderer) =>
            renderer is MeshRenderer or SkinnedMeshRenderer;

        internal static bool HasMesh(GameObject target) =>
            target.GetComponents<Renderer>().Any(HasMesh);

        internal static bool HasBlendShapes(GameObject target) =>
            target
                .GetComponents<SkinnedMeshRenderer>()
                .Any(renderer =>
                    renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0
                );

        internal static HashSet<Renderer> FindSourceRenderers(IEnumerable<GameObject> sources) =>
            sources
                .Where(source => source != null)
                .SelectMany(source => source.GetComponentsInChildren<Renderer>(true))
                .Where(renderer =>
                    HasMesh(renderer) && !ResolvedCaptureSelection.IsEditorOnly(renderer.transform)
                )
                .ToHashSet();

        internal static HashSet<Transform> FindSourceBones(Transform source)
        {
            var renderers = source.GetComponentsInChildren<Renderer>(true);
            var referencedBones = new HashSet<Transform>();

            foreach (var skin in renderers.OfType<SkinnedMeshRenderer>())
            {
                foreach (var bone in skin.bones.Append(skin.rootBone))
                {
                    if (bone != null && bone.IsChildOf(source))
                    {
                        referencedBones.Add(bone);
                    }
                }
            }

            var meshGroups = new HashSet<Transform>();

            foreach (var renderer in renderers)
            {
                if (!IsMeshRenderer(renderer))
                {
                    continue;
                }

                for (
                    var current = renderer.transform;
                    current != source && !referencedBones.Contains(current);
                    current = current.parent
                )
                {
                    if (!meshGroups.Add(current))
                    {
                        break;
                    }
                }
            }

            var bones = new HashSet<Transform>();

            foreach (var bone in referencedBones)
            {
                for (
                    var current = bone;
                    current != source && !meshGroups.Contains(current);
                    current = current.parent
                )
                {
                    if (!bones.Add(current))
                    {
                        break;
                    }
                }
            }

            return bones;
        }

        internal static HashSet<GameObject> FindHiddenBones(Transform source)
        {
            var bones = FindSourceBones(source);
            var hidden = new HashSet<GameObject>();
            Visit(source);

            return hidden;

            bool Visit(Transform target)
            {
                var visible =
                    HasMesh(target.gameObject)
                    || NdmfCaptureBridge.HasSourceControl(target.gameObject, false);

                foreach (Transform child in target)
                {
                    visible |= Visit(child);
                }

                if (!visible && bones.Contains(target))
                {
                    hidden.Add(target.gameObject);
                }

                return visible;
            }
        }
    }
}
