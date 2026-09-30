using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed class ResolvedCaptureSelection
    {
        internal IReadOnlyList<GameObject> Sources { get; }
        internal IReadOnlyCollection<GameObject> Excluded { get; }
        internal IReadOnlyCollection<GameObject> Enabled { get; }
        internal IReadOnlyCollection<GameObject> IgnoredBlendShapeObjects { get; }
        internal IReadOnlyList<GameObject> CaptureTargets { get; }
        private readonly List<GameObject> _explicitExclusions;
        private readonly HashSet<GameObject> _enabledLookup;

        internal ResolvedCaptureSelection(
            IEnumerable<GameObject> sources,
            IEnumerable<GameObject> excluded,
            IEnumerable<GameObject> enabled = null,
            IEnumerable<GameObject> ignoredBlendShapeObjects = null,
            IReadOnlyList<GameObject> captureTargets = null
        )
        {
            Sources = Normalize(sources).AsReadOnly();

            if (Sources.Count == 0)
            {
                throw new CaptureValidationException("NoSources");
            }

            _explicitExclusions = excluded
                .Where(item => item != null && BelongsToSources(item))
                .Distinct()
                .ToList();
            Excluded = Normalize(_explicitExclusions).AsReadOnly();
            Enabled = FilterObjects(enabled);
            _enabledLookup = new HashSet<GameObject>(Enabled);
            IgnoredBlendShapeObjects = FilterObjects(ignoredBlendShapeObjects);
            CaptureTargets = captureTargets;
        }

        private IReadOnlyCollection<GameObject> FilterObjects(IEnumerable<GameObject> objects) =>
            (objects ?? Enumerable.Empty<GameObject>())
                .Where(item => item != null && BelongsToSources(item))
                .Distinct()
                .ToList()
                .AsReadOnly();

        internal bool BelongsToSources(GameObject item) =>
            Sources.Any(source => item.transform.IsChildOf(source.transform));

        internal HashSet<Transform> FindSourceAncestors()
        {
            var ancestors = new HashSet<Transform>();

            foreach (var source in Sources)
            {
                for (var current = source.transform; current != null; current = current.parent)
                {
                    if (!ancestors.Add(current))
                    {
                        break;
                    }
                }
            }

            return ancestors;
        }

        internal bool IsEnabled(GameObject item) => _enabledLookup.Contains(item);

        internal bool IsExcluded(Transform item) =>
            IsEditorOnly(item) || Excluded.Any(root => item.IsChildOf(root.transform));

        internal ResolvedCaptureSelection WithTargetBranchEnabled(
            GameObject target,
            bool includeDescendants = true
        )
        {
            if (!BelongsToSources(target))
            {
                throw new ArgumentException(
                    "The capture target must belong to one of the sources.",
                    nameof(target)
                );
            }

            var enabledObjects = new HashSet<GameObject>();

            for (var current = target.transform; current != null; current = current.parent)
            {
                enabledObjects.Add(current.gameObject);
            }

            if (includeDescendants)
            {
                foreach (var child in target.GetComponentsInChildren<Transform>(true))
                {
                    if (!IsEditorOnly(child))
                    {
                        enabledObjects.Add(child.gameObject);
                    }
                }
            }

            return new ResolvedCaptureSelection(
                Sources,
                _explicitExclusions.Where(item => !enabledObjects.Contains(item)),
                Enabled.Concat(enabledObjects),
                IgnoredBlendShapeObjects,
                CaptureTargets
            );
        }

        internal ResolvedCaptureSelection WithCaptureTargets(IEnumerable<GameObject> targets)
        {
            var selected = FilterObjects(targets)
                .Where(item => !IsEditorOnly(item.transform))
                .ToList();

            if (selected.Count == 0)
            {
                throw new CaptureValidationException("NoSelectedItems");
            }

            return new ResolvedCaptureSelection(
                Sources,
                _explicitExclusions,
                Enabled,
                IgnoredBlendShapeObjects,
                selected.AsReadOnly()
            );
        }

        internal static bool IsEditorOnly(Transform item)
        {
            for (var current = item; current != null; current = current.parent)
            {
                if (current.CompareTag("EditorOnly"))
                {
                    return true;
                }
            }

            return false;
        }

        internal static List<GameObject> Normalize(IEnumerable<GameObject> items)
        {
            var unique = items.Where(item => item != null).Distinct().ToList();

            return unique
                .Where(item =>
                    !unique.Any(parent =>
                        parent != item && item.transform.IsChildOf(parent.transform)
                    )
                )
                .ToList();
        }
    }
}
