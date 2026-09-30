using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed partial class IconGeneratorWindow
    {
        private IEnumerable<GameObject> SourceTargets =>
            _selectionStore.Sources.Select(source => source.Target);

        internal IReadOnlyList<int> SelectedSourceIds => _selectedSourceIds;

        internal IEnumerable<GameObject> SelectedSourceObjects =>
            _selectedSourceIds
                .Select(id => EditorUtility.InstanceIDToObject(id) as GameObject)
                .Where(IsSourceObjectAvailable);

        internal SourceListFilter SourceFilter
        {
            get => _sourceFilter;
            set => _sourceFilter = value;
        }

        internal HashSet<int> SourceExpansionBeforeSearch { get; set; }

        internal string SourceSearch
        {
            get => _sourceSearch;
            set => _sourceSearch = value ?? "";
        }

        internal float SourceTreeHeight
        {
            get => EditorPrefs.GetFloat(SourceTreeHeightKey, 220);
            set => EditorPrefs.SetFloat(SourceTreeHeightKey, value);
        }

        internal bool UsesSelectedMeshWeights =>
            _settings.blendShapeWeightMode == BlendShapeWeightMode.SelectedMeshes;

        internal void SetSelectedSourceIds(IEnumerable<int> ids)
        {
            var selected = ids.Distinct().ToList();

            if (_selectedSourceIds.SequenceEqual(selected))
            {
                return;
            }

            _selectedSourceIds = selected;
            _view?.RefreshCamera();

            if (UsesSelectedSaveScope)
            {
                _previewRebuildPending = _previewRenderPending = true;
            }
        }

        private ResolvedCaptureSelection ResolveCaptureSelection(out CaptureSelectionTree tree)
        {
            var selection = _selectionStore.Resolve(out tree);

            return UsesSelectedSaveScope
                ? selection.WithCaptureTargets(SelectedSourceObjects)
                : selection;
        }

        private bool IsSourceObjectAvailable(GameObject target) =>
            target != null
            && !ResolvedCaptureSelection.IsEditorOnly(target.transform)
            && IsInsideSources(target);

        private bool IsInsideSources(GameObject target) =>
            SourceTargets.Any(source =>
                source != null && target.transform.IsChildOf(source.transform)
            );

        internal bool CanCaptureTarget(GameObject target)
        {
            if (!IsSourceObjectAvailable(target))
            {
                return false;
            }

            if (CaptureHierarchy.FindSourceRenderers(new[] { target }).Count > 0)
            {
                return true;
            }

            var (meshParts, materialTargets) = FindEffectTargets(target);

            return meshParts.Count > 0 || materialTargets.Count > 0;
        }

        private (
            IReadOnlyList<CaptureMeshPart> MeshParts,
            IReadOnlyList<Renderer> MaterialTargets
        ) FindEffectTargets(GameObject target)
        {
            var controls = target.GetComponentsInChildren<Transform>(true);
            var sourceRenderers = CaptureHierarchy.FindSourceRenderers(SourceTargets);

            return (
                NdmfCaptureBridge.GetMeshParts(controls, sourceRenderers),
                NdmfCaptureBridge.GetMaterialTargets(controls, sourceRenderers)
            );
        }

        internal string GetCaptureTargetTooltip(GameObject target, bool includePreviewHint = false)
        {
            if (target == null)
            {
                return "";
            }

            return string.Join(
                "\n\n",
                new[]
                {
                    SelectionReference.HierarchyPath(target.transform),
                    GetCapturePartDescription(target),
                    includePreviewHint ? _localization.Text("tooltips.PreviewSource") : "",
                }.Where(text => !string.IsNullOrEmpty(text))
            );
        }

        internal string GetCapturePartDescription(GameObject target)
        {
            if (target == null || CaptureHierarchy.FindSourceRenderers(new[] { target }).Count > 0)
            {
                return "";
            }

            var (meshParts, materialTargets) = FindEffectTargets(target);
            var partNames = meshParts.Select(part => part.Renderer.name).Distinct().ToArray();
            var materialNames = materialTargets
                .Select(renderer => renderer.name)
                .Distinct()
                .ToArray();
            var descriptions = new List<string>();

            if (partNames.Length > 0)
            {
                descriptions.Add(
                    _localization.Text("tooltips.CaptureMeshPart", string.Join(", ", partNames))
                );
            }

            if (materialNames.Length > 0)
            {
                descriptions.Add(
                    _localization.Text(
                        "tooltips.CaptureMaterialChange",
                        string.Join(", ", materialNames)
                    )
                );
            }

            return descriptions.Count == 0
                ? _localization.Text("tooltips.NoIndividualPreview")
                : string.Join("\n\n", descriptions);
        }

        internal void AddSource(GameObject target) => AddSources(new[] { target });

        internal void AddSources(IEnumerable<GameObject> targets)
        {
            var objects = targets.Where(target => target != null).Distinct().ToArray();

            if (objects.Length == 0)
            {
                return;
            }

            var sources = _selectionStore.Sources;
            var before = sources.ToArray();
            Undo.RecordObject(_settings, "Icon Generator add sources");

            try
            {
                foreach (var target in objects)
                {
                    _selectionStore.AddSource(target);
                }

                SelectionChanged();
            }
            catch (Exception exception)
            {
                sources.Clear();
                sources.AddRange(before);
                _view.RefreshSourceTree();
                ShowError(exception);
            }
        }

        internal void ReplaceSource(SelectionReference entry, GameObject target)
        {
            var sources = _selectionStore.Sources;
            var before = sources.ToArray();
            var excludedBefore = _selectionStore.Excluded.ToArray();
            var enabledBefore = _selectionStore.Enabled.ToArray();
            var ignoredBefore = _selectionStore.IgnoredBlendShapeObjects.ToArray();

            Undo.RecordObject(_settings, "Icon Generator replace source");
            sources.Remove(entry);

            try
            {
                if (target != null)
                {
                    _selectionStore.AddSource(target);
                }

                if (entry.Target != target)
                {
                    _selectionStore.ClearSourceOverrides(entry.Target);
                }

                SelectionChanged();
            }
            catch (Exception exception)
            {
                sources.Clear();
                sources.AddRange(before);
                _selectionStore.Excluded.Clear();
                _selectionStore.Excluded.AddRange(excludedBefore);
                _selectionStore.Enabled.Clear();
                _selectionStore.Enabled.AddRange(enabledBefore);
                _selectionStore.IgnoredBlendShapeObjects.Clear();
                _selectionStore.IgnoredBlendShapeObjects.AddRange(ignoredBefore);
                _view.RefreshSourceTree();
                ShowError(exception);
            }
        }

        internal void RemoveSource(SelectionReference entry)
        {
            Undo.RecordObject(_settings, "Icon Generator remove source");

            if (_selectionStore.Sources.Remove(entry))
            {
                _selectionStore.ClearSourceOverrides(entry.Target);
            }

            SelectionChanged();
        }

        internal void SetObjectIncluded(GameObject target, bool included) =>
            SetObjectsIncluded(new[] { target }, included);

        internal void SetBranchIncluded(GameObject target, bool? included)
        {
            var branch = FindSourceNodes(new[] { target }).FirstOrDefault();

            if (branch == null)
            {
                return;
            }

            SetObjectsIncluded(
                branch
                    .DescendantsAndSelf()
                    .Where(node => included != false || node.Source == null)
                    .SelectMany(node => node.Controls),
                item => included ?? item.activeSelf
            );
        }

        internal void SetSelectedObjectsIncluded(IEnumerable<GameObject> targets, bool? included) =>
            SetObjectsIncluded(
                FindSourceNodes(targets).SelectMany(node => node.Controls),
                item => included ?? item.activeSelf
            );

        private IEnumerable<CaptureSelectionNode> FindSourceNodes(IEnumerable<GameObject> targets)
        {
            var selected = targets.Where(target => target != null).ToHashSet();

            return CaptureSelectionTree
                .Build(_selectionStore)
                .Roots.SelectMany(root => root.DescendantsAndSelf())
                .Where(node => node.Target != null && selected.Contains(node.Target));
        }

        internal IReadOnlyList<Component> GetRelatedComponents(IEnumerable<GameObject> targets) =>
            NdmfCaptureBridge.GetRelatedComponents(
                SourceTargets,
                CaptureHierarchy.FindSourceRenderers(
                    FindSourceNodes(targets).Select(node => node.Target)
                )
            );

        internal void SetObjectsIncluded(IEnumerable<GameObject> targets, bool included) =>
            SetObjectsIncluded(targets, _ => included);

        private void SetObjectsIncluded(
            IEnumerable<GameObject> targets,
            Func<GameObject, bool> isIncluded
        )
        {
            var validTargets = targets
                .Where(target => target != null && IsInsideSources(target))
                .Distinct()
                .ToArray();

            if (validTargets.Length == 0)
            {
                return;
            }

            Undo.RecordObject(_settings, "Icon Generator object inclusion");

            foreach (var target in validTargets)
            {
                if (isIncluded(target))
                {
                    _selectionStore.Excluded.RemoveAll(entry => entry.Target == target);

                    if (
                        !target.activeSelf
                        && !_selectionStore.Sources.Any(source => source.Target == target)
                        && !_selectionStore.Enabled.Any(entry => entry.Target == target)
                    )
                    {
                        _selectionStore.Enabled.Add(SelectionReference.For(target));
                    }
                }
                else
                {
                    _selectionStore.Enabled.RemoveAll(entry => entry.Target == target);
                    _selectionStore.AddExclusion(target);
                }
            }

            SelectionChanged();
        }

        internal void SetBlendShapeWeightMode(BlendShapeWeightMode value) =>
            ChangeSettings(
                "Icon Generator BlendShape weight mode",
                () => _settings.blendShapeWeightMode = value
            );

        internal void SetBlendShapeWeightsIgnored(GameObject target, bool ignored) =>
            SetBlendShapeWeightsIgnored(new[] { target }, ignored);

        internal void SetBlendShapeWeightsIgnored(IEnumerable<GameObject> targets, bool ignored)
        {
            var meshes = FindBlendShapeTargets(targets).ToHashSet();

            if (meshes.Count == 0)
            {
                return;
            }

            Undo.RecordObject(_settings, "Icon Generator mesh BlendShape weights");
            var entries = _selectionStore.IgnoredBlendShapeObjects;
            entries.RemoveAll(entry => meshes.Contains(entry.Target));

            if (ignored)
            {
                entries.AddRange(meshes.Select(SelectionReference.For));
            }

            SelectionChanged();
        }

        internal bool CanIgnoreBlendShapeWeights(GameObject target) =>
            FindBlendShapeTargets(new[] { target }).Any();

        private IEnumerable<GameObject> FindBlendShapeTargets(IEnumerable<GameObject> targets)
        {
            var requested = targets.Where(target => target != null).ToArray();

            return FindSourceNodes(requested)
                .Select(node => node.MeshTarget)
                .Concat(requested)
                .Where(target =>
                    IsSourceObjectAvailable(target) && CaptureHierarchy.HasBlendShapes(target)
                )
                .Distinct();
        }

        private void SelectionChanged()
        {
            _previewRebuildPending = _previewValidationPending = true;
            _selectionStore.RemoveNonSelectableOverrides();
            _selectionStore.Store();
            _view.RefreshSourceTree();
            SettingsChanged();
            _referenceRefreshPending = true;
        }
    }
}
