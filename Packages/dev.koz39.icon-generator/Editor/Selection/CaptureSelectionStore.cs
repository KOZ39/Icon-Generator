using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal sealed class CaptureSelectionStore
    {
        [Serializable]
        private sealed class IdList
        {
            public List<string> items = new();
        }

        [Serializable]
        private sealed class CameraList
        {
            public List<ItemCameraSettings> items = new();
        }

        internal readonly List<SelectionReference> Sources = new();
        internal readonly List<SelectionReference> Excluded = new();
        internal readonly List<SelectionReference> Enabled = new();
        internal readonly List<SelectionReference> IgnoredBlendShapeObjects = new();
        private readonly IconGeneratorSettings _settings;

        private string SessionKey =>
            $"{IconGeneratorPackageInfo.PreferencesPrefix}{Application.dataPath}";

        private bool HasOnlyUnavailableSources =>
            Sources.Count > 0 && Sources.All(item => item.State != ReferenceState.Available);

        internal CaptureSelectionStore(IconGeneratorSettings settings)
        {
            _settings = settings;
            Load(Sources, settings.sourceIds, "Sources");
            Load(Excluded, settings.excludedObjectIds, "Excluded");
            Load(Enabled, settings.enabledObjectIds, "Enabled");
            Load(
                IgnoredBlendShapeObjects,
                settings.ignoredBlendShapeObjectIds,
                "IgnoredBlendShapes"
            );

            if (!_settings.sessionReferencesLoaded)
            {
                var cameras = JsonUtility.FromJson<CameraList>(
                    SessionState.GetString($"{SessionKey}ItemCameras", "{}")
                );

                foreach (var camera in cameras?.items ?? new List<ItemCameraSettings>())
                {
                    if (!_settings.itemCameras.Any(item => item.objectId == camera.objectId))
                    {
                        _settings.itemCameras.Add(camera);
                    }
                }
            }

            _settings.sessionReferencesLoaded = true;
            Refresh();
            Store();
        }

        private void Load(
            List<SelectionReference> references,
            IEnumerable<string> storedIds,
            string suffix
        )
        {
            var ids = storedIds ?? Enumerable.Empty<string>();

            if (!_settings.sessionReferencesLoaded)
            {
                var temporary = JsonUtility.FromJson<IdList>(
                    SessionState.GetString($"{SessionKey}{suffix}", "{}")
                );
                ids = ids.Concat(temporary?.items ?? Enumerable.Empty<string>());
            }

            foreach (var id in ids.Where(id => !string.IsNullOrEmpty(id)).Distinct())
            {
                references.Add(SelectionReference.Resolve(id));
            }
        }

        internal int Refresh()
        {
            RefreshItemCameras();
            var removed = _settings.itemCameras.RemoveAll(item =>
                item.Reference.State == ReferenceState.Deleted
            );

            foreach (var list in new[] { Sources, Excluded, Enabled, IgnoredBlendShapeObjects })
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var entry = list[i];
                    entry.Refresh();

                    if (entry.State == ReferenceState.Deleted)
                    {
                        list.RemoveAt(i);
                        removed++;
                    }
                }
            }

            var roots = ResolvedCaptureSelection
                .Normalize(Sources.Select(entry => entry.Target))
                .ToHashSet();
            var ids = new HashSet<string>();
            removed += Sources.RemoveAll(entry =>
                !ids.Add(entry.Id) || (entry.Target != null && !roots.Contains(entry.Target))
            );

            return removed + RemoveNonSelectableOverrides();
        }

        private void RefreshItemCameras()
        {
            foreach (var camera in _settings.itemCameras)
            {
                camera.Refresh();
            }
        }

        internal int RemoveNonSelectableOverrides()
        {
            if (Excluded.Count == 0 && Enabled.Count == 0)
            {
                return 0;
            }

            var nonSelectable = new HashSet<GameObject>();

            foreach (var source in Sources.Where(item => item.Target != null))
            {
                nonSelectable.UnionWith(CaptureHierarchy.FindHiddenBones(source.Target.transform));
            }

            return new[] { Excluded, Enabled }.Sum(list =>
                list.RemoveAll(entry =>
                    entry.Target != null && nonSelectable.Contains(entry.Target)
                )
            );
        }

        internal void ClearSourceOverrides(GameObject source)
        {
            if (source == null)
            {
                return;
            }

            _settings.itemCameras.RemoveAll(item =>
                item.Reference.Target != null
                && item.Reference.Target.transform.IsChildOf(source.transform)
            );

            foreach (var list in new[] { Excluded, Enabled, IgnoredBlendShapeObjects })
            {
                list.RemoveAll(entry =>
                    entry.Target != null && entry.Target.transform.IsChildOf(source.transform)
                );
            }
        }

        internal void AddExclusion(GameObject item)
        {
            if (item != null && !Excluded.Any(entry => entry.Target == item))
            {
                Excluded.Add(SelectionReference.For(item));
            }
        }

        internal void AddSource(GameObject item)
        {
            if (
                item == null
                || Sources.Any(entry =>
                    entry.Target != null && item.transform.IsChildOf(entry.Target.transform)
                )
            )
            {
                return;
            }

            Sources.RemoveAll(entry =>
                entry.Target != null && entry.Target.transform.IsChildOf(item.transform)
            );
            Sources.Add(SelectionReference.For(item));
        }

        internal ResolvedCaptureSelection Resolve(out CaptureSelectionTree tree)
        {
            if (HasOnlyUnavailableSources)
            {
                throw new CaptureValidationException(
                    "UnavailableSourceReferences",
                    string.Join("\n", Sources.Select(item => item.Label))
                );
            }

            tree = CaptureSelectionTree.Build(this);
            var mergedExclusions = tree.UncheckedMergedControls();

            return new ResolvedCaptureSelection(
                Sources.Select(item => item.Target),
                Excluded.Select(item => item.Target).Concat(mergedExclusions),
                Enabled.Select(item => item.Target),
                IgnoredBlendShapeObjects.Select(item => item.Target)
            );
        }

        internal void Store()
        {
            RefreshItemCameras();
            SessionState.SetString(
                $"{SessionKey}ItemCameras",
                JsonUtility.ToJson(
                    new CameraList
                    {
                        items = _settings
                            .itemCameras.Where(item =>
                                SelectionReference.IsSessionId(item.objectId)
                            )
                            .ToList(),
                    }
                )
            );
            StoreList(Sources, _settings.sourceIds, "Sources");
            StoreList(Excluded, _settings.excludedObjectIds, "Excluded");
            StoreList(Enabled, _settings.enabledObjectIds, "Enabled");
            StoreList(
                IgnoredBlendShapeObjects,
                _settings.ignoredBlendShapeObjectIds,
                "IgnoredBlendShapes"
            );
        }

        private void StoreList(
            List<SelectionReference> references,
            List<string> destinationIds,
            string suffix
        )
        {
            destinationIds.Clear();
            destinationIds.AddRange(references.Select(item => item.Id));
            SessionState.SetString(
                $"{SessionKey}{suffix}",
                JsonUtility.ToJson(
                    new IdList
                    {
                        items = references
                            .Where(item => SelectionReference.IsSessionId(item.Id))
                            .Select(item => item.Id)
                            .ToList(),
                    }
                )
            );
        }
    }
}
