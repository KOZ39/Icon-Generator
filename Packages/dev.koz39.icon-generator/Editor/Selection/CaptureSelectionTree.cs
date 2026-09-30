using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed class CaptureSelectionNode
    {
        internal int Id;
        internal SelectionReference Source;
        internal GameObject Target;
        internal List<GameObject> Controls;
        internal bool Included;
        internal bool InheritedExclusion;
        internal List<CaptureSelectionNode> Children = new();

        internal GameObject MeshTarget =>
            Controls.FirstOrDefault(target => target != null && CaptureHierarchy.HasMesh(target));

        internal IEnumerable<CaptureSelectionNode> DescendantsAndSelf()
        {
            yield return this;

            foreach (var child in Children.SelectMany(node => node.DescendantsAndSelf()))
            {
                yield return child;
            }
        }
    }

    internal sealed class CaptureSelectionTree
    {
        internal IReadOnlyList<CaptureSelectionNode> Roots { get; }

        private CaptureSelectionTree(List<CaptureSelectionNode> roots) => Roots = roots;

        private static HashSet<GameObject> ResolveTargets(
            IEnumerable<SelectionReference> references
        ) =>
            references
                .Where(entry => entry.Target != null)
                .Select(entry => entry.Target)
                .ToHashSet();

        internal static CaptureSelectionTree Build(CaptureSelectionStore store)
        {
            var excluded = ResolveTargets(store.Excluded);
            var enabled = ResolveTargets(store.Enabled);
            var roots = new List<CaptureSelectionNode>();
            var captureRenderers = CaptureHierarchy.FindSourceRenderers(
                store.Sources.Select(source => source.Target)
            );
            var unavailableId = int.MinValue;

            foreach (var source in store.Sources)
            {
                if (
                    source.Target != null
                    && ResolvedCaptureSelection.IsEditorOnly(source.Target.transform)
                )
                {
                    continue;
                }

                var included = source.Target != null && !excluded.Contains(source.Target);
                var children = new List<CaptureSelectionNode>();

                if (source.Target != null)
                {
                    var bones = CaptureHierarchy.FindSourceBones(source.Target.transform);

                    foreach (Transform child in source.Target.transform)
                    {
                        AddSelectionBranch(children, child, excluded, bones, captureRenderers);
                    }

                    ApplySelectionState(children, excluded, enabled, !included);
                }

                roots.Add(
                    new CaptureSelectionNode
                    {
                        Id =
                            source.Target != null ? source.Target.GetInstanceID() : unavailableId++,
                        Source = source,
                        Target = source.Target,
                        Controls = new() { source.Target },
                        Included = included,
                        Children = children,
                    }
                );
            }

            return new CaptureSelectionTree(roots);
        }

        internal IEnumerable<GameObject> UncheckedMergedControls()
        {
            var pending = new Stack<CaptureSelectionNode>(Roots);

            while (pending.Count > 0)
            {
                var node = pending.Pop();

                if (!node.Included && node.Controls.Count > 1)
                {
                    foreach (var control in node.Controls)
                    {
                        yield return control;
                    }
                }

                foreach (var child in node.Children)
                {
                    pending.Push(child);
                }
            }
        }

        internal IEnumerable<GameObject> IndividualCaptureTargets(
            IReadOnlyCollection<GameObject> sources,
            bool includeInactiveObjects
        )
        {
            sources = sources
                .Where(source => !ResolvedCaptureSelection.IsEditorOnly(source.transform))
                .ToArray();

            foreach (var root in Roots)
            {
                if (
                    root.Target == null
                    || !sources.Contains(root.Target)
                    || (!includeInactiveObjects && !root.Included)
                )
                {
                    continue;
                }

                if (sources.Count > 1 || root.Children.Count == 0)
                {
                    yield return root.Target;
                }

                foreach (var target in CaptureTargets(root.Children, includeInactiveObjects))
                {
                    yield return target;
                }
            }
        }

        private static IEnumerable<GameObject> CaptureTargets(
            IEnumerable<CaptureSelectionNode> items,
            bool includeInactiveObjects
        )
        {
            foreach (var item in items)
            {
                if (
                    item.Target == null
                    || ResolvedCaptureSelection.IsEditorOnly(item.Target.transform)
                    || (!includeInactiveObjects && !item.Included)
                )
                {
                    continue;
                }

                yield return item.Target;

                foreach (var target in CaptureTargets(item.Children, includeInactiveObjects))
                {
                    yield return target;
                }
            }
        }

        private static void AddSelectionBranch(
            List<CaptureSelectionNode> siblings,
            Transform target,
            HashSet<GameObject> excluded,
            HashSet<Transform> bones,
            IReadOnlyCollection<Renderer> captureRenderers
        )
        {
            if (ResolvedCaptureSelection.IsEditorOnly(target))
            {
                return;
            }

            var explicitlyExcluded = excluded.Contains(target.gameObject);
            var children = new List<CaptureSelectionNode>();

            foreach (Transform child in target)
            {
                AddSelectionBranch(children, child, excluded, bones, captureRenderers);
            }

            var hasMesh = CaptureHierarchy.HasMesh(target.gameObject);
            var hasSourceControl = NdmfCaptureBridge.HasSourceControl(
                target.gameObject,
                hasMesh || children.Count > 0,
                captureRenderers
            );

            if (!hasMesh && !hasSourceControl && children.Count == 0)
            {
                return;
            }

            if (
                !hasMesh
                && children.Count > 0
                && NdmfCaptureBridge.IsMenuContainer(target.gameObject)
            )
            {
                if (hasSourceControl && children.Count == 1)
                {
                    children[0].Controls.Add(target.gameObject);
                    siblings.AddRange(children);
                    return;
                }

                if (!hasSourceControl && target.gameObject.activeSelf && !explicitlyExcluded)
                {
                    siblings.AddRange(children);
                    return;
                }
            }

            if (
                !hasMesh
                && !hasSourceControl
                && !bones.Contains(target)
                && children.Count == 1
                && (children[0].Children.Count > 0 || children[0].Controls.Count > 1)
                && !PrefabUtility.IsAnyPrefabInstanceRoot(target.gameObject)
            )
            {
                children[0].Controls.Add(target.gameObject);
                siblings.AddRange(children);
                return;
            }

            if (
                !hasMesh
                && !hasSourceControl
                && bones.Contains(target)
                && !explicitlyExcluded
                && target.gameObject.activeSelf
            )
            {
                siblings.AddRange(children);
                return;
            }

            var controls = new List<GameObject> { target.gameObject };

            if (
                !hasMesh
                && !bones.Contains(target)
                && children.Count == 1
                && children[0].Children.Count == 0
                && children[0].MeshTarget != null
                && (hasSourceControl || PrefabUtility.IsAnyPrefabInstanceRoot(target.gameObject))
                && !NdmfCaptureBridge.HasSourceControl(target.gameObject, false, captureRenderers)
            )
            {
                controls.AddRange(children[0].Controls);
                children.Clear();
            }

            siblings.Add(
                new CaptureSelectionNode
                {
                    Id = target.gameObject.GetInstanceID(),
                    Target = target.gameObject,
                    Controls = controls,
                    Children = children,
                }
            );
        }

        private static void ApplySelectionState(
            IEnumerable<CaptureSelectionNode> nodes,
            HashSet<GameObject> excluded,
            HashSet<GameObject> enabled,
            bool inheritedExclusion
        )
        {
            foreach (var node in nodes)
            {
                node.InheritedExclusion = inheritedExclusion;
                node.Included =
                    !inheritedExclusion
                    && node.Controls.All(target =>
                        !excluded.Contains(target)
                        && (target.activeSelf || enabled.Contains(target))
                    );

                if (node.Children.Count > 0)
                {
                    ApplySelectionState(node.Children, excluded, enabled, !node.Included);
                }
            }
        }
    }
}
