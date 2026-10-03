using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal sealed class CaptureClone : IDisposable
    {
        internal const string RootName = "IconGenerator_CaptureRoot";
        internal const int Layer = 21;
        internal readonly GameObject Root;
        internal readonly List<Renderer> Renderers = new();
        internal readonly List<Renderer> OriginalRenderers = new();
        internal readonly List<Vector3> WorldCorners = new();
        internal readonly List<string> Unsupported = new();
        internal readonly List<CaptureValidationException> Warnings = new();
        internal ResolvedCaptureSelection Selection => _selection;
        internal bool HasEffects => _effects != null;

        internal IReadOnlyList<Component> ForcedMenuItems { get; private set; } =
            Array.Empty<Component>();

        internal readonly Dictionary<Renderer, CaptureMeshPart> MeshParts = new();
        private readonly Dictionary<Transform, Transform> _transforms = new();
        private readonly HashSet<Transform> _sourceAncestors;
        private readonly List<Mesh> _meshes = new();
        private readonly List<(Renderer Renderer, List<Vector3> Corners)> _meshCorners = new();
        private ResolvedCaptureSelection _selection;
        private readonly GameObject _captureTarget;
        private readonly bool _includeInactiveObjects;
        private readonly BlendShapeWeightMode _blendShapeWeightMode;
        private readonly CaptureRootMarker _marker;
        private ICaptureEffects _effects;
        private IReadOnlyList<Vector3> _visibleCorners;
        private Vector3 _visibleCornersRotation;

        internal IReadOnlyList<Vector3> GetFramingCorners(
            IconGeneratorSettings settings,
            IconCameraPose? pose = null
        )
        {
            var camera = pose ?? settings.SharedCamera;

            if (!camera.ShouldUseVisibleArea)
            {
                return WorldCorners;
            }

            var rotation = IconGeneratorSettings.NormalizeRotation(camera.rotation);

            if (_visibleCorners == null || _visibleCornersRotation != rotation)
            {
                _visibleCorners = CaptureVisibleArea.Measure(this, rotation);
                _visibleCornersRotation = rotation;
            }

            return _visibleCorners;
        }

        private CaptureClone(
            ResolvedCaptureSelection selection,
            GameObject captureTarget,
            BlendShapeWeightMode blendShapeWeightMode,
            bool includeInactiveObjects
        )
        {
            _selection = selection;
            _sourceAncestors = selection.FindSourceAncestors();

            if (includeInactiveObjects)
            {
                var targets =
                    captureTarget != null
                        ? new[] { captureTarget }
                        : selection.CaptureTargets ?? selection.Sources;

                foreach (var target in ResolvedCaptureSelection.Normalize(targets))
                {
                    _selection = _selection.WithTargetBranchEnabled(target);
                }
            }

            _captureTarget = captureTarget;
            _includeInactiveObjects = includeInactiveObjects;
            _blendShapeWeightMode = blendShapeWeightMode;
            Root = CreateObject(RootName);

            try
            {
                _marker = CaptureRootMarker.Create(Root);
            }
            catch
            {
                Object.DestroyImmediate(Root);
                throw;
            }
        }

        internal static CaptureClone Build(
            ResolvedCaptureSelection selection,
            GameObject captureTarget = null,
            BlendShapeWeightMode blendShapeWeightMode = BlendShapeWeightMode.None,
            bool includeInactiveObjects = false,
            CaptureEffectsCache effectsCache = null
        )
        {
            if (captureTarget != null && !selection.BelongsToSources(captureTarget))
            {
                throw new ArgumentException(
                    "The capture target must belong to one of the sources.",
                    nameof(captureTarget)
                );
            }

            var clone = new CaptureClone(
                selection,
                captureTarget,
                blendShapeWeightMode,
                includeInactiveObjects
            );

            try
            {
                clone.Build(effectsCache);

                return clone;
            }
            catch
            {
                clone.Dispose();
                throw;
            }
        }

        private void Build(CaptureEffectsCache effectsCache)
        {
            var included = new HashSet<Transform>();

            var targets =
                _captureTarget != null
                    ? new[] { _captureTarget }
                    : _selection.CaptureTargets ?? _selection.Sources;

            foreach (var target in targets)
            {
                CollectIncludedTransforms(target.transform, included);
            }

            if (_captureTarget != null || _selection.CaptureTargets != null)
            {
                var controls = targets
                    .Where(target =>
                        CaptureHierarchy.FindSourceRenderers(new[] { target }).Count == 0
                    )
                    .SelectMany(target => target.GetComponentsInChildren<Transform>(true))
                    .Where(transform =>
                        included.Contains(transform) && IsActiveInCapture(transform)
                    )
                    .ToArray();
                ForcedMenuItems = targets
                    .Select(NdmfCaptureBridge.FindControllingMenuItem)
                    .Where(item => item != null)
                    .Distinct()
                    .ToArray();
                var sourceRenderers =
                    controls.Length > 0
                        ? CaptureHierarchy.FindSourceRenderers(_selection.Sources)
                        : null;
                var parts =
                    controls.Length > 0
                        ? NdmfCaptureBridge.GetMeshParts(controls, sourceRenderers)
                        : Array.Empty<CaptureMeshPart>();

                foreach (var part in parts)
                {
                    if (included.Contains(part.Renderer.transform))
                    {
                        continue;
                    }

                    if (_includeInactiveObjects)
                    {
                        _selection = _selection.WithTargetBranchEnabled(part.Renderer.gameObject);
                    }

                    if (!_selection.IsExcluded(part.Renderer.transform))
                    {
                        MeshParts.Add(part.Renderer, part);
                        included.Add(part.Renderer.transform);
                    }
                }

                var materialTargets =
                    controls.Length > 0
                        ? NdmfCaptureBridge.GetMaterialTargets(controls, sourceRenderers)
                        : Array.Empty<Renderer>();

                foreach (var renderer in materialTargets)
                {
                    if (included.Contains(renderer.transform) || MeshParts.ContainsKey(renderer))
                    {
                        continue;
                    }

                    if (_includeInactiveObjects)
                    {
                        _selection = _selection.WithTargetBranchEnabled(renderer.gameObject);
                    }

                    if (!_selection.IsExcluded(renderer.transform))
                    {
                        included.Add(renderer.transform);
                    }
                }
            }

            _effects =
                effectsCache != null
                    ? effectsCache.Get(_selection, ForcedMenuItems, out var effectsWarning)
                    : NdmfCaptureBridge.Create(_selection, ForcedMenuItems, out effectsWarning);

            if (effectsWarning != null)
            {
                Warnings.Add(effectsWarning);
            }

            var lowerLodRenderers = FindLowerLodRenderers(included);

            foreach (var transform in included)
            {
                EnsureTransform(transform);
            }

            var renderers = new List<Renderer>();

            foreach (var transform in included)
            {
                foreach (var original in transform.GetComponents<Renderer>())
                {
                    if (
                        lowerLodRenderers.Contains(original)
                        || !original.enabled
                        || original.forceRenderingOff
                        || !IsActiveInCapture(original.transform)
                    )
                    {
                        continue;
                    }

                    if (!CaptureHierarchy.IsMeshRenderer(original))
                    {
                        if (!IsEffectRenderer(original))
                        {
                            Unsupported.Add($"{original.name} ({original.GetType().Name})");
                        }

                        continue;
                    }

                    if (CaptureHierarchy.HasMesh(original))
                    {
                        renderers.Add(original);
                    }
                }
            }

            foreach (var original in renderers)
            {
                var ignoreBlendShapeWeights = _blendShapeWeightMode switch
                {
                    BlendShapeWeightMode.SingleMesh => renderers.Count == 1,
                    BlendShapeWeightMode.SelectedMeshes =>
                        _selection.IgnoredBlendShapeObjects.Contains(original.gameObject),
                    BlendShapeWeightMode.AllMeshes => true,
                    _ => false,
                };

                MeshParts.TryGetValue(original, out var part);
                CopyRenderer(original, ignoreBlendShapeWeights, part);
            }

            if (Renderers.Count == 0)
            {
                throw new CaptureValidationException("NoRenderers");
            }
        }

        private static bool IsEffectRenderer(Renderer renderer) =>
            renderer is ParticleSystemRenderer or TrailRenderer or LineRenderer;

        private void CollectIncludedTransforms(Transform transform, HashSet<Transform> included)
        {
            if (_selection.IsExcluded(transform))
            {
                return;
            }

            included.Add(transform);

            foreach (Transform child in transform)
            {
                CollectIncludedTransforms(child, included);
            }
        }

        private static HashSet<Renderer> FindLowerLodRenderers(HashSet<Transform> included)
        {
            var groups = new HashSet<LODGroup>();

            foreach (var transform in included)
            {
                foreach (var group in transform.GetComponentsInParent<LODGroup>(true))
                {
                    groups.Add(group);
                }
            }

            var rejected = new HashSet<Renderer>();

            foreach (var group in groups)
            {
                var levels = group.GetLODs();

                if (levels.Length == 0)
                {
                    continue;
                }

                var highestDetailRenderers = new HashSet<Renderer>(levels[0].renderers);

                foreach (var renderer in levels.Skip(1).SelectMany(level => level.renderers))
                {
                    if (renderer != null && !highestDetailRenderers.Contains(renderer))
                    {
                        rejected.Add(renderer);
                    }
                }
            }

            return rejected;
        }

        private bool IsActiveInCapture(Transform item)
        {
            for (var current = item; current != null; current = current.parent)
            {
                if (!IsSelfActiveInCapture(current))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSelfActiveInCapture(Transform item) =>
            _sourceAncestors.Contains(item)
            || _selection.IsEnabled(item.gameObject)
            || item.gameObject.activeSelf;

        internal Transform EnsureTransform(Transform original)
        {
            if (original == null)
            {
                return null;
            }

            if (_transforms.TryGetValue(original, out var existing))
            {
                return existing;
            }

            var parent =
                original.parent != null ? EnsureTransform(original.parent) : Root.transform;
            var copy = CreateObject(original.name);

            copy.transform.SetParent(parent, false);
            copy.transform.localPosition =
                EditorUtility.IsPersistent(original) && original.parent == null
                    ? Vector3.zero
                    : original.localPosition;
            copy.transform.localRotation = original.localRotation;
            copy.transform.localScale = original.localScale;
            copy.SetActive(IsSelfActiveInCapture(original));
            _transforms.Add(original, copy.transform);

            return copy.transform;
        }

        private void CopyRenderer(
            Renderer original,
            bool ignoreBlendShapeWeights,
            CaptureMeshPart part
        )
        {
            Mesh partMesh = null;

            if (part != null)
            {
                partMesh = part.CreateMesh();

                if (partMesh == null)
                {
                    MeshParts.Remove(original);
                    return;
                }

                _meshes.Add(partMesh);
            }

            var target = EnsureTransform(original.transform).gameObject;
            Renderer renderer;
            Bounds localBounds;

            if (original is SkinnedMeshRenderer skin)
            {
                if (skin.sharedMesh == null)
                {
                    return;
                }

                var copy = target.AddComponent<SkinnedMeshRenderer>();

                copy.sharedMesh = partMesh != null ? partMesh : skin.sharedMesh;
                copy.bones = skin.bones.Select(EnsureTransform).ToArray();
                copy.rootBone = EnsureTransform(skin.rootBone);
                copy.quality = skin.quality;
                copy.updateWhenOffscreen = true;
                copy.skinnedMotionVectors = false;

                for (var i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                {
                    copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                }

                renderer = copy;
            }
            else
            {
                var filter = original.GetComponent<MeshFilter>();

                if (filter == null || filter.sharedMesh == null)
                {
                    return;
                }

                target.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                renderer = target.AddComponent<MeshRenderer>();
            }

            var materials = original.sharedMaterials;

            renderer.hideFlags = HideFlags.HideAndDontSave;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.receiveShadows = original.receiveShadows;
            renderer.lightProbeUsage = original.lightProbeUsage;
            renderer.reflectionProbeUsage = original.reflectionProbeUsage;
            renderer.probeAnchor = EnsureTransform(original.probeAnchor);
            renderer.lightmapIndex = original.lightmapIndex;
            renderer.lightmapScaleOffset = original.lightmapScaleOffset;
            renderer.realtimeLightmapIndex = original.realtimeLightmapIndex;
            renderer.realtimeLightmapScaleOffset = original.realtimeLightmapScaleOffset;
            renderer.renderingLayerMask = original.renderingLayerMask;
            renderer.sortingLayerID = original.sortingLayerID;
            renderer.sortingOrder = original.sortingOrder;
            renderer.allowOcclusionWhenDynamic = false;

            var properties = new MaterialPropertyBlock();
            original.GetPropertyBlock(properties);

            if (!properties.isEmpty)
            {
                renderer.SetPropertyBlock(properties);
            }

            for (var i = 0; i < materials.Length; i++)
            {
                original.GetPropertyBlock(properties, i);

                if (!properties.isEmpty)
                {
                    renderer.SetPropertyBlock(properties, i);
                }
            }

            _effects?.Apply(
                original,
                renderer,
                ignoreBlendShapeWeights || part != null,
                _meshes,
                Warnings
            );

            if (renderer is SkinnedMeshRenderer renderedSkin)
            {
                part?.ResetDeletionWeights(renderedSkin);

                if (ignoreBlendShapeWeights)
                {
                    for (var i = 0; i < renderedSkin.sharedMesh.blendShapeCount; i++)
                    {
                        renderedSkin.SetBlendShapeWeight(i, 0);
                    }
                }

                var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };

                try
                {
                    renderedSkin.BakeMesh(baked, true);

                    if (part == null)
                    {
                        baked.RecalculateBounds();
                        localBounds = baked.bounds;
                    }
                    else
                    {
                        localBounds = CaptureMeshPart.CalculateIndexedBounds(baked);
                    }

                    renderedSkin.localBounds = localBounds;
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }
            else
            {
                localBounds = target.GetComponent<MeshFilter>().sharedMesh.bounds;
            }

            Renderers.Add(renderer);
            OriginalRenderers.Add(original);

            var corners = new List<Vector3>(8);
            AddCorners(localBounds, target.transform.localToWorldMatrix, corners);
            _meshCorners.Add((renderer, corners));
            AddExpandedCorners(renderer, corners);
        }

        internal void RefreshBounds()
        {
            WorldCorners.Clear();

            foreach (var (renderer, corners) in _meshCorners)
            {
                AddExpandedCorners(renderer, corners);
            }

            _visibleCorners = null;
        }

        private void AddExpandedCorners(Renderer renderer, List<Vector3> corners)
        {
            var expansion = CalculateShaderExpansion(renderer);

            if (expansion == Vector3.zero)
            {
                WorldCorners.AddRange(corners);
                return;
            }

            foreach (var corner in corners)
            {
                AddCorners(new Bounds(corner, expansion * 2), Matrix4x4.identity, WorldCorners);
            }
        }

        private static Vector3 CalculateShaderExpansion(Renderer renderer)
        {
            var expansion = Vector3.zero;
            var matrix = renderer.transform.localToWorldMatrix;
            var axisScale = new Vector3(
                new Vector3(matrix.m00, matrix.m01, matrix.m02).magnitude,
                new Vector3(matrix.m10, matrix.m11, matrix.m12).magnitude,
                new Vector3(matrix.m20, matrix.m21, matrix.m22).magnitude
            );
            var scaleBound = axisScale.magnitude;
            var common = new MaterialPropertyBlock();
            var perMaterial = new MaterialPropertyBlock();

            renderer.GetPropertyBlock(common);
            var materials = renderer.sharedMaterials;

            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                var shader = material != null ? material.shader : null;
                var name = shader != null ? shader.name : null;

                if (
                    name == null
                    || name.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) < 0
                    || name.IndexOf("Fur", StringComparison.OrdinalIgnoreCase) < 0
                    || !material.HasProperty("_FurVector")
                )
                {
                    continue;
                }

                perMaterial.Clear();
                renderer.GetPropertyBlock(perMaterial, i);
                var properties = perMaterial.isEmpty ? common : perMaterial;
                var vector = properties.HasVector("_FurVector")
                    ? properties.GetVector("_FurVector")
                    : material.GetVector("_FurVector");
                var length = Mathf.Abs(vector.w);
                var cutout = Mathf.Max(1, Mathf.Abs(Value("_FurCutoutLength")));
                var maximumLength = length * scaleBound * cutout;
                var margin =
                    length * (axisScale * cutout + Vector3.one * Mathf.Abs(Value("_FurRandomize")));

                margin.y += Mathf.Abs(Value("_FurGravity")) * maximumLength;
                margin += Vector3.one * (4 * Mathf.Abs(Value("_FurTouchStrength")) * maximumLength);
                expansion = Vector3.Max(expansion, margin);

                float Value(string property) =>
                    properties.HasFloat(property) ? properties.GetFloat(property)
                    : material.HasProperty(property) ? material.GetFloat(property)
                    : 0;
            }

            return expansion;
        }

        internal static void AddCorners(Bounds bounds, Matrix4x4 matrix, List<Vector3> destination)
        {
            for (var i = 0; i < 8; i++)
            {
                destination.Add(
                    matrix.MultiplyPoint3x4(
                        bounds.center
                            + Vector3.Scale(
                                bounds.extents,
                                new Vector3(
                                    (i & 1) == 0 ? -1 : 1,
                                    (i & 2) == 0 ? -1 : 1,
                                    (i & 4) == 0 ? -1 : 1
                                )
                            )
                    )
                );
            }
        }

        private static GameObject CreateObject(string name)
        {
            var result = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = Layer,
            };

            result.transform.hideFlags = HideFlags.HideAndDontSave;

            return result;
        }

        internal static void CleanupAbandoned()
        {
            foreach (var marker in Resources.FindObjectsOfTypeAll<CaptureRootMarker>())
            {
                if (marker == null || marker.name != CaptureRootMarker.MarkerName)
                {
                    continue;
                }

                if (
                    marker.root != null
                    && marker.root.name == RootName
                    && marker.root.hideFlags == HideFlags.HideAndDontSave
                )
                {
                    Object.DestroyImmediate(marker.root);
                }

                if (marker.root == null)
                {
                    Object.DestroyImmediate(marker);
                }
            }
        }

        public void Dispose()
        {
            if (Root != null)
            {
                Object.DestroyImmediate(Root);
            }

            _effects = null;

            foreach (var mesh in _meshes)
            {
                Object.DestroyImmediate(mesh);
            }

            _meshes.Clear();

            if (_marker != null)
            {
                Object.DestroyImmediate(_marker);
            }
        }
    }
}
