using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal enum MenuIconRelation
    {
        Direct,
        Toggle,
        ShapeDeletion,
        CommonParent,
        Hide,
    }

    internal sealed class MenuIconCandidate
    {
        internal Component MenuItem;
        internal GameObject Target;
        internal string IconPath;
        internal Texture2D CurrentIcon;
        internal MenuIconRelation Relation;
        internal bool Selected;
    }

    internal static class MenuIconLinker
    {
        private const string IconProperty = "Control.icon";
        private const string TypeProperty = "Control.type";
        private const int ButtonControl = 101;
        private const int ToggleControl = 102;

        internal static bool IsAvailable => NdmfCaptureBridge.IsModularAvatarInstalled;

        internal static IReadOnlyList<MenuIconCandidate> FindCandidates(
            IReadOnlyList<GameObject> sources,
            CaptureSelectionTree tree,
            IReadOnlyList<(GameObject Target, string Path)> icons
        )
        {
            var iconPaths = new Dictionary<GameObject, string>();

            foreach (var (target, path) in icons.OrderBy(item => item.Target == null))
            {
                var key =
                    target != null ? target
                    : sources.Count == 1 ? sources[0]
                    : null;

                if (
                    key != null
                    && !iconPaths.ContainsKey(key)
                    && IconFileOutput.TryGetAssetPath(path, out var assetPath)
                )
                {
                    iconPaths.Add(key, assetPath);
                }
            }

            if (iconPaths.Count == 0)
            {
                return Array.Empty<MenuIconCandidate>();
            }

            var owners = new Dictionary<GameObject, GameObject>();

            foreach (var node in tree.Roots.SelectMany(root => root.DescendantsAndSelf()))
            {
                if (node.Target == null)
                {
                    continue;
                }

                foreach (var control in node.Controls.Where(control => control != null))
                {
                    owners.TryAdd(control, node.Target);
                }

                owners[node.Target] = node.Target;
            }

            GameObject IconTarget(GameObject item) =>
                item == null ? null
                : iconPaths.ContainsKey(item) ? item
                : owners.TryGetValue(item, out var owner) && iconPaths.ContainsKey(owner) ? owner
                : null;

            var components = sources
                .Where(source => source != null)
                .Select(source => source.transform.root)
                .Distinct()
                .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component =>
                    NdmfCaptureBridge.IsModularAvatarComponent(component)
                    && !ResolvedCaptureSelection.IsEditorOnly(component.transform)
                )
                .ToArray();
            var proposals =
                new Dictionary<Component, List<(GameObject Target, MenuIconRelation Relation)>>();
            void Propose(Component menu, GameObject target, MenuIconRelation relation)
            {
                if (menu == null || target == null)
                {
                    return;
                }

                if (!proposals.TryGetValue(menu, out var list))
                {
                    proposals.Add(menu, list = new());
                }

                list.Add((target, relation));
            }

            foreach (var menu in components.Where(NdmfCaptureBridge.IsMenuItem))
            {
                Propose(menu, IconTarget(menu.gameObject), MenuIconRelation.Direct);
            }

            var toggled =
                new Dictionary<Component, (List<GameObject> Shown, List<GameObject> Hidden)>();

            foreach (
                var toggle in components.Where(item =>
                    NdmfCaptureBridge.IsModularAvatarComponent(
                        item,
                        NdmfCaptureBridge.ObjectToggleName
                    )
                )
            )
            {
                var menu = FindControllingMenu(toggle);

                if (menu == null)
                {
                    continue;
                }

                if (!toggled.TryGetValue(menu, out var entry))
                {
                    toggled.Add(menu, entry = (new List<GameObject>(), new List<GameObject>()));
                }

                using var serialized = new SerializedObject(toggle);
                var inverted = serialized.FindProperty("m_inverted")?.boolValue == true;
                var objects = serialized.FindProperty("m_objects");

                for (var i = 0; objects != null && i < objects.arraySize; i++)
                {
                    var item = objects.GetArrayElementAtIndex(i);
                    var target = NdmfCaptureBridge.ResolveObjectReference(
                        item.FindPropertyRelative("Object")
                    );

                    if (target == null)
                    {
                        continue;
                    }

                    var shownWhenEnabled =
                        item.FindPropertyRelative("Active").boolValue != inverted;
                    (shownWhenEnabled ? entry.Shown : entry.Hidden).Add(target);
                }
            }

            foreach (var pair in toggled)
            {
                var shownTargets = VisibleTargets(pair.Value.Shown);
                var shown = shownTargets.Length > 0;
                var targets = shown ? shownTargets : VisibleTargets(pair.Value.Hidden);

                if (targets.Length == 0)
                {
                    continue;
                }

                var resolved = targets.Select(IconTarget).Distinct().ToArray();

                if (resolved.Length == 1 && resolved[0] != null)
                {
                    Propose(
                        pair.Key,
                        resolved[0],
                        shown ? MenuIconRelation.Toggle : MenuIconRelation.Hide
                    );
                    continue;
                }

                var ancestor = FindCommonAncestor(targets);

                if (ancestor != null && !sources.Contains(ancestor))
                {
                    Propose(
                        pair.Key,
                        IconTarget(ancestor),
                        shown ? MenuIconRelation.CommonParent : MenuIconRelation.Hide
                    );
                }
            }

            foreach (
                var changer in components.Where(item =>
                    NdmfCaptureBridge.IsModularAvatarComponent(
                        item,
                        NdmfCaptureBridge.ShapeChangerName
                    )
                )
            )
            {
                using var serialized = new SerializedObject(changer);
                var shapes = serialized.FindProperty("m_shapes");
                var deletes = false;

                for (var i = 0; shapes != null && i < shapes.arraySize; i++)
                {
                    deletes |=
                        shapes.GetArrayElementAtIndex(i).FindPropertyRelative("ChangeType").intValue
                        == 0;
                }

                if (deletes)
                {
                    Propose(
                        FindControllingMenu(changer),
                        IconTarget(changer.gameObject),
                        MenuIconRelation.ShapeDeletion
                    );
                }
            }

            var candidates = new List<MenuIconCandidate>();

            foreach (var pair in proposals)
            {
                var best = pair.Value.Min(item => item.Relation);
                var targets = pair
                    .Value.Where(item => item.Relation == best)
                    .Select(item => item.Target)
                    .Distinct()
                    .ToArray();

                if (targets.Length != 1)
                {
                    continue;
                }

                var candidate = CreateCandidate(pair.Key, targets[0], iconPaths[targets[0]], best);

                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates
                .OrderBy(
                    candidate => SelectionReference.HierarchyPath(candidate.MenuItem.transform),
                    StringComparer.Ordinal
                )
                .ToArray();
        }

        private static MenuIconCandidate CreateCandidate(
            Component menu,
            GameObject target,
            string iconPath,
            MenuIconRelation relation
        )
        {
            using var serialized = new SerializedObject(menu);
            var icon = serialized.FindProperty(IconProperty);
            var type = serialized.FindProperty(TypeProperty);

            if (
                icon == null
                || type == null
                || (type.intValue != ToggleControl && type.intValue != ButtonControl)
            )
            {
                return null;
            }

            var current = icon.objectReferenceValue as Texture2D;
            var currentPath = current != null ? AssetDatabase.GetAssetPath(current) : null;

            if (string.Equals(currentPath, iconPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new MenuIconCandidate
            {
                MenuItem = menu,
                Target = target,
                IconPath = iconPath,
                CurrentIcon = current,
                Relation = relation,
                Selected =
                    relation != MenuIconRelation.Hide
                    && (
                        current == null
                        || string.Equals(
                            Path.GetDirectoryName(currentPath),
                            Path.GetDirectoryName(iconPath),
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),
            };
        }

        private static GameObject[] VisibleTargets(IEnumerable<GameObject> targets) =>
            targets
                .Distinct()
                .Where(target =>
                    target.GetComponentsInChildren<Renderer>(true).Any(CaptureHierarchy.HasMesh)
                )
                .ToArray();

        private static Component FindControllingMenu(Component component)
        {
            for (var current = component.transform; current != null; current = current.parent)
            {
                var menu = current
                    .GetComponents<Component>()
                    .FirstOrDefault(NdmfCaptureBridge.IsMenuItem);

                if (menu != null)
                {
                    return menu;
                }
            }

            return null;
        }

        private static GameObject FindCommonAncestor(IReadOnlyList<GameObject> items)
        {
            var ancestor = items[0].transform;

            foreach (var item in items.Skip(1))
            {
                while (ancestor != null && !item.transform.IsChildOf(ancestor))
                {
                    ancestor = ancestor.parent;
                }
            }

            return ancestor != null ? ancestor.gameObject : null;
        }

        internal static int Apply(IEnumerable<MenuIconCandidate> candidates)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Icon Generator link menu icons");
            var linked = 0;

            foreach (var candidate in candidates)
            {
                if (candidate.MenuItem == null)
                {
                    continue;
                }

                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(candidate.IconPath);

                if (icon == null)
                {
                    continue;
                }

                using var serialized = new SerializedObject(candidate.MenuItem);
                var property = serialized.FindProperty(IconProperty);

                if (property == null)
                {
                    continue;
                }

                property.objectReferenceValue = icon;

                if (serialized.ApplyModifiedProperties())
                {
                    linked++;
                }
            }

            Undo.CollapseUndoOperations(group);

            return linked;
        }
    }
}
