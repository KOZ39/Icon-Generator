using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal interface ICaptureEffects
    {
        void Apply(
            Renderer original,
            Renderer copy,
            bool skipShapeDeletion,
            ICollection<Mesh> ownedMeshes,
            ICollection<CaptureValidationException> warnings
        );
    }

    internal sealed class CaptureEffectsCache
    {
        private readonly Dictionary<
            string,
            (ICaptureEffects Effects, CaptureValidationException Warning)
        > _entries = new();

        internal ICaptureEffects Get(
            ResolvedCaptureSelection selection,
            IReadOnlyCollection<Component> forcedMenuItems,
            out CaptureValidationException warning
        )
        {
            var sourceAncestors = selection.FindSourceAncestors();
            var enabled = selection.Enabled.Where(item =>
                !item.activeSelf
                && !sourceAncestors.Contains(item.transform)
                && !selection.IsExcluded(item.transform)
            );
            static string Ids(IEnumerable<GameObject> items) =>
                string.Join(",", items.Select(item => item.GetInstanceID()).OrderBy(id => id));
            var forced = string.Join(
                ",",
                (forcedMenuItems ?? Array.Empty<Component>())
                    .Select(item => item.GetInstanceID())
                    .OrderBy(id => id)
            );
            var key = $"{Ids(selection.Sources)}|{Ids(selection.Excluded)}|{Ids(enabled)}|{forced}";

            if (!_entries.TryGetValue(key, out var entry))
            {
                var effects = NdmfCaptureBridge.Create(selection, forcedMenuItems, out var created);
                entry = (effects, created);
                _entries.Add(key, entry);
            }

            warning = entry.Warning;

            return entry.Effects;
        }
    }
}
