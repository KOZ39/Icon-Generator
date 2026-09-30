using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal sealed class NdmfCaptureBridge : ICaptureEffects
    {
        private const string RuntimeNamespace = "nadena.dev.modular_avatar.core.";
        private const string EditorNamespace = RuntimeNamespace + "editor.";
        private const string MaterialPrefix = "m_Materials.Array.data[";

        private const BindingFlags ReflectionFlags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        internal const string EffectsSkippedKey = "ModularAvatarEffectsSkipped";
        internal const string ShapeChangerName = "ModularAvatarShapeChanger";
        internal const string ObjectToggleName = "ModularAvatarObjectToggle";
        private const string MaterialSetterName = "ModularAvatarMaterialSetter";
        private const string MaterialSwapName = "ModularAvatarMaterialSwap";
        private const string BlendshapeSyncName = "ModularAvatarBlendshapeSync";
        private const string MenuItemName = "ModularAvatarMenuItem";
        private static readonly Api _api;
        private static readonly string _apiError;
        private static bool? _modularAvatarInstalled;

        private readonly Dictionary<
            Renderer,
            List<(string Property, object Value)>
        > _reactiveProperties = new();

        static NdmfCaptureBridge()
        {
            try
            {
                _api = Api.Find();
            }
            catch (Exception exception)
            {
                _api = null;
                _apiError = exception.Message;
            }
        }

        internal static bool IsAvailable => _api != null;

        internal static bool IsModularAvatarInstalled =>
            _modularAvatarInstalled ??= FindType(RuntimeNamespace + ShapeChangerName) != null;

        private static Type FindType(string name) =>
            AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name, false))
                .FirstOrDefault(type => type != null);

        private static MethodInfo FindMethod(Type type, string name, int parameterCount) =>
            type
                ?.GetMethods(ReflectionFlags)
                .FirstOrDefault(method =>
                    method.Name == name && method.GetParameters().Length == parameterCount
                );

        internal static bool IsModularAvatarComponent(Component component) =>
            component != null
            && component.GetType().FullName.StartsWith(RuntimeNamespace, StringComparison.Ordinal);

        internal static bool IsModularAvatarComponent(Component component, string typeName) =>
            component != null && component.GetType().FullName == RuntimeNamespace + typeName;

        internal static bool IsMenuItem(Component component) =>
            IsModularAvatarComponent(component, MenuItemName);

        private static bool IsReactive(Component component) =>
            component != null
            && component.GetType().FullName
                is RuntimeNamespace + ShapeChangerName
                    or RuntimeNamespace + MaterialSetterName
                    or RuntimeNamespace + MaterialSwapName;

        internal static bool IsMenuContainer(GameObject target) =>
            target
                .GetComponents<Component>()
                .Any(component =>
                    component != null
                    && component.GetType().FullName
                        is RuntimeNamespace + "ModularAvatarMenuInstaller"
                            or RuntimeNamespace + "ModularAvatarMenuGroup"
                            or RuntimeNamespace + MenuItemName
                );

        internal static bool HasSourceControl(
            GameObject target,
            bool affectsMesh,
            IReadOnlyCollection<Renderer> captureRenderers = null
        )
        {
            foreach (var component in target.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                if (
                    IsAvailable
                    && IsReactive(component)
                    && (captureRenderers == null || AffectsCapture(component, captureRenderers))
                )
                {
                    return true;
                }

                if (!affectsMesh)
                {
                    continue;
                }

                switch (component.GetType().FullName)
                {
                    case RuntimeNamespace + "ModularAvatarBoneProxy":
                    case RuntimeNamespace + "ModularAvatarMoveTo":
                    case RuntimeNamespace + "ModularAvatarMergeArmature":
                    case RuntimeNamespace + BlendshapeSyncName:
                        return true;
                }
            }

            return false;
        }

        private static Transform FindAvatarTransform(Transform target) =>
            _api.FindAvatar.Invoke(null, new object[] { target }) as Transform;

        private static GameObject ResolveReferencedObject(SerializedProperty reference) =>
            reference == null
                ? null
                : _api.ResolveReference.Invoke(null, new object[] { reference }) as GameObject;

        internal static IEnumerable<Material> FindReferencedMaterials(Transform root)
        {
            var materials = new List<Material>();

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (!IsModularAvatarComponent(component))
                {
                    continue;
                }

                using var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();

                while (property.Next(true))
                {
                    if (
                        property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue is Material material
                    )
                    {
                        materials.Add(material);
                    }
                }
            }

            return materials;
        }

        internal static GameObject ResolveObjectReference(SerializedProperty reference) =>
            _api?.ResolveReference == null ? null : ResolveReferencedObject(reference);

        private static SkinnedMeshRenderer FindSkinWithMesh(GameObject target) =>
            target != null
            && target.TryGetComponent<SkinnedMeshRenderer>(out var skin)
            && skin.sharedMesh != null
                ? skin
                : null;

        internal static IReadOnlyList<CaptureMeshPart> GetMeshParts(
            IEnumerable<Transform> controls,
            IReadOnlyCollection<Renderer> sourceRenderers
        )
        {
            if (_api?.ResolveReference == null)
            {
                return Array.Empty<CaptureMeshPart>();
            }

            var parts = new Dictionary<SkinnedMeshRenderer, CaptureMeshPart>();

            foreach (var control in controls)
            {
                if (ResolvedCaptureSelection.IsEditorOnly(control))
                {
                    continue;
                }

                foreach (var component in control.GetComponents<Behaviour>())
                {
                    if (
                        !IsModularAvatarComponent(component, ShapeChangerName) || !component.enabled
                    )
                    {
                        continue;
                    }

                    using var serialized = new SerializedObject(component);
                    var shapes = serialized.FindProperty("m_shapes");
                    var threshold = serialized.FindProperty("m_threshold");

                    if (shapes == null || threshold == null)
                    {
                        continue;
                    }

                    for (var i = 0; i < shapes.arraySize; i++)
                    {
                        var shape = shapes.GetArrayElementAtIndex(i);

                        if (shape.FindPropertyRelative("ChangeType").intValue != 0)
                        {
                            continue;
                        }

                        var skin = FindSkinWithMesh(
                            ResolveReferencedObject(shape.FindPropertyRelative("Object"))
                        );
                        var name = shape.FindPropertyRelative("ShapeName").stringValue;

                        if (
                            skin == null
                            || !sourceRenderers.Contains(skin)
                            || skin.sharedMesh.GetBlendShapeIndex(name) < 0
                        )
                        {
                            continue;
                        }

                        if (!parts.TryGetValue(skin, out var part))
                        {
                            parts.Add(skin, part = new CaptureMeshPart(skin));
                        }

                        part.Shapes.Add((name, threshold.floatValue));
                    }
                }
            }

            return parts.Values.ToArray();
        }

        private static IEnumerable<Renderer> FindSwapTargets(
            Component component,
            SerializedObject serialized,
            IEnumerable<Renderer> renderers
        )
        {
            var referencedRoot = ResolveReferencedObject(serialized.FindProperty("m_root"));
            var avatarRoot = FindAvatarTransform(component.transform);
            var root =
                referencedRoot != null ? referencedRoot.transform
                : avatarRoot != null ? avatarRoot
                : component.transform.root;
            var swaps = serialized.FindProperty("m_swaps");
            var sources = new List<Material>();

            for (var i = 0; swaps != null && i < swaps.arraySize; i++)
            {
                var from =
                    swaps
                        .GetArrayElementAtIndex(i)
                        .FindPropertyRelative("From")
                        .objectReferenceValue as Material;

                if (from != null)
                {
                    sources.Add(from);
                }
            }

            return renderers.Where(renderer =>
                renderer.transform.IsChildOf(root)
                && renderer.sharedMaterials.Any(material => sources.Contains(material))
            );
        }

        internal static Component FindControllingMenuItem(GameObject target)
        {
            if (target == null || !IsAvailable)
            {
                return null;
            }

            var avatar = FindAvatarTransform(target.transform);

            for (var cursor = target.transform; cursor != null && cursor != avatar; )
            {
                var menuItem = cursor.GetComponents<Component>().FirstOrDefault(IsMenuItem);

                if (menuItem != null)
                {
                    return ResolvedCaptureSelection.IsEditorOnly(cursor) ? null : menuItem;
                }

                cursor = cursor.parent;
            }

            return null;
        }

        internal static IReadOnlyList<Renderer> GetMaterialTargets(
            IEnumerable<Transform> controls,
            IReadOnlyCollection<Renderer> sourceRenderers
        )
        {
            if (_api?.ResolveReference == null)
            {
                return Array.Empty<Renderer>();
            }

            var targets = new List<Renderer>();

            foreach (var control in controls)
            {
                if (ResolvedCaptureSelection.IsEditorOnly(control))
                {
                    continue;
                }

                foreach (var component in control.GetComponents<Behaviour>())
                {
                    if (component == null || !component.enabled)
                    {
                        continue;
                    }

                    var isSwap = IsModularAvatarComponent(component, MaterialSwapName);

                    if (!isSwap && !IsModularAvatarComponent(component, MaterialSetterName))
                    {
                        continue;
                    }

                    using var serialized = new SerializedObject(component);

                    if (isSwap)
                    {
                        targets.AddRange(FindSwapTargets(component, serialized, sourceRenderers));
                        continue;
                    }

                    var objects = serialized.FindProperty("m_objects");

                    for (var i = 0; objects != null && i < objects.arraySize; i++)
                    {
                        var entry = objects.GetArrayElementAtIndex(i);
                        var target = ResolveReferencedObject(entry.FindPropertyRelative("Object"));
                        var renderer = target != null ? target.GetComponent<Renderer>() : null;
                        var slot = entry.FindPropertyRelative("MaterialIndex").intValue;

                        if (
                            renderer != null
                            && sourceRenderers.Contains(renderer)
                            && slot >= 0
                            && slot < renderer.sharedMaterials.Length
                        )
                        {
                            targets.Add(renderer);
                        }
                    }
                }
            }

            return targets.Distinct().ToArray();
        }

        private static bool AffectsCapture(
            Component component,
            IReadOnlyCollection<Renderer> renderers
        )
        {
            if (ResolvedCaptureSelection.IsEditorOnly(component.transform))
            {
                return false;
            }

            if (_api.ResolveReference == null)
            {
                return true;
            }

            using var serialized = new SerializedObject(component);

            if (IsModularAvatarComponent(component, MaterialSwapName))
            {
                return FindSwapTargets(component, serialized, renderers).Any();
            }

            var shapes = IsModularAvatarComponent(component, ShapeChangerName);
            var entries = serialized.FindProperty(shapes ? "m_shapes" : "m_objects");

            for (var i = 0; entries != null && i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var target = ResolveReferencedObject(entry.FindPropertyRelative("Object"));

                if (target == null)
                {
                    continue;
                }

                if (shapes)
                {
                    var skin = FindSkinWithMesh(target);
                    var shape = entry.FindPropertyRelative("ShapeName").stringValue;

                    if (skin == null || skin.sharedMesh.GetBlendShapeIndex(shape) < 0)
                    {
                        continue;
                    }

                    if (renderers.Contains(skin))
                    {
                        return true;
                    }

                    if (
                        entry.FindPropertyRelative("ChangeType").intValue != 0
                        && renderers
                            .OfType<SkinnedMeshRenderer>()
                            .Any(destination =>
                                HasSyncedShape(destination, null, skin, shape, new())
                            )
                    )
                    {
                        return true;
                    }
                }
                else
                {
                    var renderer = target.GetComponent<Renderer>();
                    var slot = entry.FindPropertyRelative("MaterialIndex").intValue;

                    if (
                        renderer != null
                        && renderers.Contains(renderer)
                        && slot >= 0
                        && slot < renderer.sharedMaterials.Length
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<(
            string Local,
            SkinnedMeshRenderer Source,
            string Remote
        )> EnumerateSyncedShapes(SkinnedMeshRenderer destination)
        {
            if (destination == null || destination.sharedMesh == null)
            {
                yield break;
            }

            var sync = destination
                .GetComponents<Component>()
                .FirstOrDefault(component =>
                    IsModularAvatarComponent(component, BlendshapeSyncName)
                );

            if (sync == null)
            {
                yield break;
            }

            using var serialized = new SerializedObject(sync);
            var bindings = serialized.FindProperty("Bindings");

            for (var i = 0; bindings != null && i < bindings.arraySize; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                var remote = binding.FindPropertyRelative("Blendshape").stringValue;
                var local = binding.FindPropertyRelative("LocalBlendshape").stringValue;

                if (string.IsNullOrEmpty(local))
                {
                    local = remote;
                }

                if (destination.sharedMesh.GetBlendShapeIndex(local) < 0)
                {
                    continue;
                }

                var referenced = FindSkinWithMesh(
                    ResolveReferencedObject(binding.FindPropertyRelative("ReferenceMesh"))
                );

                if (referenced == null || referenced.sharedMesh.GetBlendShapeIndex(remote) < 0)
                {
                    continue;
                }

                yield return (local, referenced, remote);
            }
        }

        private static bool HasSyncedShape(
            SkinnedMeshRenderer destination,
            string localShape,
            SkinnedMeshRenderer source,
            string sourceShape,
            HashSet<(SkinnedMeshRenderer, string)> visited
        ) =>
            destination != null
            && visited.Add((destination, localShape))
            && EnumerateSyncedShapes(destination)
                .Any(binding =>
                    (localShape == null || binding.Local == localShape)
                    && (
                        (binding.Source == source && binding.Remote == sourceShape)
                        || HasSyncedShape(
                            binding.Source,
                            binding.Remote,
                            source,
                            sourceShape,
                            visited
                        )
                    )
                );

        internal static IReadOnlyList<Component> GetRelatedComponents(
            IEnumerable<GameObject> sources,
            IReadOnlyCollection<Renderer> renderers
        )
        {
            if (!IsAvailable || _api.ResolveReference == null || renderers.Count == 0)
            {
                return Array.Empty<Component>();
            }

            return FindAvatarRoots(sources)
                .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component =>
                    component != null
                    && !ResolvedCaptureSelection.IsEditorOnly(component.transform)
                    && IsRelatedComponent(component, renderers)
                )
                .OrderBy(
                    component => SelectionReference.HierarchyPath(component.transform),
                    StringComparer.Ordinal
                )
                .ThenBy(component => component.GetType().Name, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsRelatedComponent(
            Component component,
            IReadOnlyCollection<Renderer> renderers
        )
        {
            if (IsReactive(component))
            {
                return AffectsCapture(component, renderers);
            }

            if (!IsModularAvatarComponent(component, BlendshapeSyncName))
            {
                return false;
            }

            var skin = component.TryGetComponent<SkinnedMeshRenderer>(out var found) ? found : null;

            return EnumerateSyncedShapes(skin)
                .Any(binding =>
                    renderers.Contains(skin)
                    || renderers
                        .OfType<SkinnedMeshRenderer>()
                        .Any(destination =>
                            HasSyncedShape(destination, null, skin, binding.Local, new())
                        )
                );
        }

        private static List<GameObject> FindAvatarRoots(IEnumerable<GameObject> sources) =>
            ResolvedCaptureSelection.Normalize(
                sources
                    .Where(source => source != null)
                    .Select(source =>
                    {
                        var avatar = FindAvatarTransform(source.transform);

                        return avatar != null ? avatar.gameObject : source;
                    })
            );

        internal static ICaptureEffects Create(
            ResolvedCaptureSelection selection,
            IReadOnlyCollection<Component> forcedMenuItems,
            out CaptureValidationException warning
        )
        {
            warning = null;

            if (!IsAvailable)
            {
                if (
                    IsModularAvatarInstalled
                    && selection
                        .Sources.Select(source => source.transform.root)
                        .Distinct()
                        .Any(HasReactiveComponents)
                )
                {
                    warning = new CaptureValidationException(
                        EffectsSkippedKey,
                        _apiError ?? "Unsupported Modular Avatar/NDMF API."
                    );
                }

                return null;
            }

            try
            {
                var roots = FindAvatarRoots(selection.Sources);

                if (!roots.Any(root => HasReactiveComponents(root.transform)))
                {
                    return null;
                }

                return new NdmfCaptureBridge(selection, roots, forcedMenuItems);
            }
            catch (Exception exception)
            {
                warning = new CaptureValidationException(
                    EffectsSkippedKey,
                    InnerMessage(exception)
                );

                return null;
            }
        }

        private static bool HasReactiveComponents(Transform root) =>
            root.GetComponentsInChildren<Component>(true).Any(IsReactive);

        private static string InnerMessage(Exception exception) =>
            (
                exception is TargetInvocationException invocation
                    ? invocation.InnerException ?? exception
                    : exception
            ).Message;

        private NdmfCaptureBridge(
            ResolvedCaptureSelection selection,
            List<GameObject> roots,
            IReadOnlyCollection<Component> forcedMenuItems
        )
        {
            var container = new GameObject("IconGenerator_AvatarEffects")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var copies = new Dictionary<Object, Object>();
            var originals = new Dictionary<Object, Object>();

            try
            {
                foreach (var root in roots)
                {
                    CopyHierarchy(root.transform, container.transform, copies);
                    ((GameObject)copies[root]).AddComponent(_api.AvatarRoot);
                }

                foreach (var original in copies.Keys.OfType<GameObject>().ToArray())
                {
                    foreach (var component in original.GetComponents<Component>())
                    {
                        if (component == null || !ShouldCopy(component))
                        {
                            continue;
                        }

                        var target = (GameObject)copies[original];
                        var copy = target.AddComponent(component.GetType());
                        EditorUtility.CopySerialized(component, copy);
                        copy.hideFlags = HideFlags.HideAndDontSave;

                        if (copy is Renderer renderer)
                        {
                            renderer.forceRenderingOff = true;
                        }

                        copies.Add(component, copy);
                    }
                }

                foreach (var pair in copies)
                {
                    originals.Add(pair.Value, pair.Key);

                    if (pair.Value is not Component component || component is Transform)
                    {
                        continue;
                    }

                    using var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();

                    while (property.Next(true))
                    {
                        if (
                            property.propertyType == SerializedPropertyType.ObjectReference
                            && property.objectReferenceValue != null
                            && copies.TryGetValue(
                                property.objectReferenceValue,
                                out var replacement
                            )
                        )
                        {
                            property.objectReferenceValue = replacement;
                        }
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                void SetCopyActive(GameObject original, bool active)
                {
                    if (copies.TryGetValue(original, out var copy))
                    {
                        ((GameObject)copy).SetActive(active);
                    }
                }

                foreach (var source in selection.Sources)
                {
                    for (var current = source.transform; current != null; current = current.parent)
                    {
                        SetCopyActive(current.gameObject, true);
                    }
                }

                foreach (var enabled in selection.Enabled)
                {
                    SetCopyActive(enabled, true);
                }

                foreach (var excluded in selection.Excluded)
                {
                    SetCopyActive(excluded, false);
                }

                foreach (var original in copies.Keys.OfType<GameObject>())
                {
                    if (ResolvedCaptureSelection.IsEditorOnly(original.transform))
                    {
                        SetCopyActive(original, false);
                    }
                }

                foreach (var pair in copies)
                {
                    if (
                        pair.Value is not Component menuItem
                        || !IsMenuItem(menuItem)
                        || !menuItem
                            .gameObject.GetComponentsInChildren<Renderer>(false)
                            .Any(CaptureHierarchy.HasMesh)
                    )
                    {
                        continue;
                    }

                    using var serialized = new SerializedObject(menuItem);
                    var isDefault = serialized.FindProperty("isDefault");

                    if (isDefault != null)
                    {
                        isDefault.boolValue = true;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                foreach (var root in roots)
                {
                    var analyzer = _api.Constructor.Invoke(new[] { _api.NullContext });
                    ForceMenuItems(analyzer, (GameObject)copies[root], forcedMenuItems, copies);
                    var result = _api.Analyze.Invoke(analyzer, new[] { copies[root] });
                    var states = (IDictionary)_api.InitialStates.GetValue(result);

                    foreach (DictionaryEntry state in states)
                    {
                        var target = (Object)_api.TargetObject.GetValue(state.Key);

                        if (
                            target == null
                            || !originals.TryGetValue(target, out var original)
                            || original is not Renderer renderer
                        )
                        {
                            continue;
                        }

                        var property = (string)_api.PropertyName.GetValue(state.Key);

                        if (!_reactiveProperties.TryGetValue(renderer, out var properties))
                        {
                            _reactiveProperties.Add(renderer, properties = new());
                        }

                        properties.Add((property, state.Value));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(container);
            }
        }

        private static void ForceMenuItems(
            object analyzer,
            GameObject rootCopy,
            IReadOnlyCollection<Component> forcedMenuItems,
            Dictionary<Object, Object> copies
        )
        {
            if (
                forcedMenuItems == null
                || forcedMenuItems.Count == 0
                || _api.ForceMenuItems == null
                || _api.GetMenuItemProperty == null
            )
            {
                return;
            }

            var type = _api.ForceMenuItems.PropertyType;
            var setItem = type.GetMethod("SetItem");
            var dictionary = type.GetField("Empty")?.GetValue(null);

            if (setItem == null || dictionary == null)
            {
                return;
            }

            var changed = false;

            foreach (var forced in forcedMenuItems)
            {
                if (
                    forced == null
                    || !copies.TryGetValue(forced, out var copy)
                    || copy is not Component copyComponent
                    || !copyComponent.transform.IsChildOf(rootCopy.transform)
                )
                {
                    continue;
                }

                var parameter =
                    _api.GetMenuItemProperty.Invoke(
                        analyzer,
                        new object[] { copyComponent.gameObject }
                    ) as string;

                if (parameter == null)
                {
                    continue;
                }

                dictionary = setItem.Invoke(dictionary, new[] { parameter, (object)copyComponent });
                changed = true;
            }

            if (changed)
            {
                _api.ForceMenuItems.SetValue(analyzer, dictionary);
            }
        }

        private static bool ShouldCopy(Component component) =>
            component is MeshFilter or MeshRenderer or SkinnedMeshRenderer
            || IsReactive(component)
            || IsMenuItem(component)
            || IsModularAvatarComponent(component, BlendshapeSyncName);

        private static void CopyHierarchy(
            Transform original,
            Transform parent,
            Dictionary<Object, Object> copies
        )
        {
            var copy = new GameObject(original.name) { hideFlags = HideFlags.HideAndDontSave };
            copy.SetActive(original.gameObject.activeSelf);
            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = original.localPosition;
            copy.transform.localRotation = original.localRotation;
            copy.transform.localScale = original.localScale;
            copies.Add(original.gameObject, copy);
            copies.Add(original, copy.transform);

            foreach (Transform child in original)
            {
                CopyHierarchy(child, copy.transform, copies);
            }
        }

        public void Apply(
            Renderer original,
            Renderer copy,
            bool skipShapeDeletion,
            ICollection<Mesh> ownedMeshes,
            ICollection<CaptureValidationException> warnings
        )
        {
            if (!_reactiveProperties.TryGetValue(original, out var properties))
            {
                return;
            }

            var materials = copy.sharedMaterials;
            var selectors = new List<object>();

            foreach (var (property, value) in properties)
            {
                if (
                    property.StartsWith("blendShape.", StringComparison.Ordinal)
                    && value is float weight
                    && copy is SkinnedMeshRenderer skin
                    && skin.sharedMesh != null
                )
                {
                    var index = skin.sharedMesh.GetBlendShapeIndex(
                        property.Substring("blendShape.".Length)
                    );

                    if (index >= 0)
                    {
                        skin.SetBlendShapeWeight(index, Mathf.Clamp(weight, 0, 100));
                    }
                }
                else if (
                    property.StartsWith(MaterialPrefix, StringComparison.Ordinal)
                    && value is Material material
                )
                {
                    var slotText = property.Substring(MaterialPrefix.Length).TrimEnd(']');

                    if (
                        int.TryParse(slotText, out var slot)
                        && slot >= 0
                        && slot < materials.Length
                    )
                    {
                        materials[slot] = material;
                    }
                }
                else if (
                    !skipShapeDeletion
                    && value != null
                    && _api.MeshSelector.IsInstanceOfType(value)
                )
                {
                    selectors.Add(value);
                }
            }

            copy.sharedMaterials = materials;

            if (
                selectors.Count == 0
                || copy is not SkinnedMeshRenderer meshRenderer
                || meshRenderer.sharedMesh == null
            )
            {
                return;
            }

            var array = Array.CreateInstance(_api.MeshSelector, selectors.Count);

            for (var i = 0; i < selectors.Count; i++)
            {
                array.SetValue(selectors[i], i);
            }

            Mesh mesh;

            try
            {
                mesh =
                    _api.FilterMesh.Invoke(
                        null,
                        new object[] { copy, meshRenderer.sharedMesh, array }
                    ) as Mesh;
            }
            catch (Exception exception)
            {
                warnings?.Add(
                    new CaptureValidationException(EffectsSkippedKey, InnerMessage(exception))
                );
                return;
            }

            if (mesh == null || mesh == meshRenderer.sharedMesh || EditorUtility.IsPersistent(mesh))
            {
                return;
            }

            ownedMeshes.Add(mesh);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            meshRenderer.sharedMesh = mesh;
        }

        private sealed class Api
        {
            internal Type AvatarRoot;
            internal Type MeshSelector;
            internal ConstructorInfo Constructor;
            internal MethodInfo FindAvatar;
            internal MethodInfo ResolveReference;
            internal MethodInfo Analyze;
            internal MethodInfo FilterMesh;
            internal FieldInfo InitialStates;
            internal FieldInfo TargetObject;
            internal FieldInfo PropertyName;
            internal PropertyInfo ForceMenuItems;
            internal MethodInfo GetMenuItemProperty;
            internal object NullContext;

            internal static Api Find()
            {
                var analyzer = FindType(EditorNamespace + "ReactiveObjectAnalyzer");
                var context = FindType("nadena.dev.ndmf.preview.ComputeContext");

                if (analyzer == null || context == null)
                {
                    return null;
                }

                var analysisResult = analyzer.GetNestedType("AnalysisResult", ReflectionFlags);
                var targetProp = FindType(EditorNamespace + "TargetProp");
                var api = new Api
                {
                    AvatarRoot = FindType("nadena.dev.ndmf.runtime.components.NDMFAvatarRoot"),
                    MeshSelector = FindType(EditorNamespace + "IMeshSelector"),
                    Constructor = analyzer.GetConstructor(new[] { context }),
                    FindAvatar = FindMethod(
                        FindType("nadena.dev.ndmf.runtime.RuntimeUtil"),
                        "FindAvatarInParents",
                        1
                    ),
                    ResolveReference = FindType(RuntimeNamespace + "AvatarObjectReference")
                        ?.GetMethod(
                            "Get",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(SerializedProperty) },
                            null
                        ),
                    Analyze = FindMethod(analyzer, "Analyze", 1),
                    FilterMesh = FindMethod(
                        FindType(EditorNamespace + "RemoveVerticesFromMesh"),
                        "FilterPrimitivesOnly",
                        3
                    ),
                    InitialStates = analysisResult?.GetField("InitialStates", ReflectionFlags),
                    TargetObject = targetProp?.GetField("TargetObject", ReflectionFlags),
                    PropertyName = targetProp?.GetField("PropertyName", ReflectionFlags),
                    ForceMenuItems = analyzer.GetProperty("ForceMenuItems", ReflectionFlags),
                    GetMenuItemProperty = FindMethod(analyzer, "GetMenuItemProperty", 1),
                    NullContext =
                        context.GetField("NullContext", ReflectionFlags)?.GetValue(null)
                        ?? context.GetProperty("NullContext", ReflectionFlags)?.GetValue(null),
                };

                return
                    api.AvatarRoot != null
                    && api.MeshSelector != null
                    && api.Constructor != null
                    && api.FindAvatar != null
                    && api.Analyze != null
                    && api.FilterMesh != null
                    && api.InitialStates != null
                    && api.TargetObject != null
                    && api.PropertyName != null
                    && api.NullContext != null
                    ? api
                    : null;
            }
        }
    }
}
