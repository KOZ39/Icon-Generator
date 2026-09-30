using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal enum ExistingFileAction
    {
        Overwrite,
        Ask,
        AddNumber,
    }

    internal enum IconGenerationMode
    {
        Combined,
        Individual,
        Both,
    }

    internal enum BlendShapeWeightMode
    {
        None,
        SingleMesh,
        SelectedMeshes,
        AllMeshes,
    }

    internal enum IconSaveScope
    {
        CheckedItems,
        AllItems,
        SelectedItems,
    }

    internal enum MenuIconLinkMode
    {
        Disabled,
        Ask,
        EmptyOnly,
    }

    [FilePath(
        "ProjectSettings/IconGeneratorSettings.asset",
        FilePathAttribute.Location.ProjectFolder
    )]
    internal sealed class IconGeneratorSettings : ScriptableSingleton<IconGeneratorSettings>
    {
        internal const float DefaultZoom = 1;
        internal const float MinZoom = 0.01f;
        internal const float SliderMinZoom = 0.5f;
        internal const float SliderMaxZoom = 5;
        internal const float DefaultPadding = 0.1f;
        internal const float MinPadding = 0;
        internal const float MaxPadding = 0.5f;
        internal const int DefaultResolution = 256;
        internal const int DefaultPreviewResolution = 1024;
        internal const IconSaveScope DefaultSaveScope = IconSaveScope.AllItems;

        internal const BlendShapeWeightMode DefaultBlendShapeWeightMode =
            BlendShapeWeightMode.SingleMesh;

        internal const int MinOutlineWidth = 1;
        internal const int MaxOutlineWidth = 8;

        internal static readonly IReadOnlyList<int> IconResolutions = Array.AsReadOnly(
            new[] { 32, 64, 128, 256 }
        );

        internal static readonly IReadOnlyList<int> PreviewResolutions = Array.AsReadOnly(
            new[] { 256, 512, 1024, 2048 }
        );

        internal const string DefaultFileNameTemplate = "{name}";

        internal static readonly IReadOnlyList<(string Name, Vector3 Rotation)> CameraPresets =
            Array.AsReadOnly(
                new[]
                {
                    ("Front", new Vector3(0, 180, 0)),
                    ("Back", Vector3.zero),
                    ("Left", new Vector3(0, 90, 0)),
                    ("Right", new Vector3(0, 270, 0)),
                    ("Top", new Vector3(90, 180, 0)),
                    ("Bottom", new Vector3(-90, 180, 0)),
                    ("DiagonalLeft", new Vector3(35, 135, 0)),
                    ("DiagonalRight", new Vector3(35, 225, 0)),
                }
            );

        internal static string GetCameraPresetName(Vector3 angles)
        {
            var rotation = Quaternion.Euler(angles);

            return CameraPresets
                .FirstOrDefault(item =>
                    Quaternion.Angle(rotation, Quaternion.Euler(item.Rotation)) < 0.01f
                )
                .Name;
        }

        [SerializeField]
        internal int schemaVersion;

        [SerializeField]
        internal List<string> sourceIds = new();

        [SerializeField]
        internal List<string> excludedObjectIds = new();

        [SerializeField]
        internal List<string> enabledObjectIds = new();

        [SerializeField]
        internal BlendShapeWeightMode blendShapeWeightMode = DefaultBlendShapeWeightMode;

        [SerializeField]
        internal List<string> ignoredBlendShapeObjectIds = new();

        [NonSerialized]
        internal bool sessionReferencesLoaded;

        [SerializeField]
        internal List<ItemCameraSettings> itemCameras = new();

        internal IconCameraPose SharedCamera
        {
            get =>
                new(cameraRotation, framingOffset, zoom, keepWholeObject, useVisibleArea, padding);
            set
            {
                cameraRotation = value.rotation;
                framingOffset = value.offset;
                zoom = value.zoom;
                padding = value.padding;
                keepWholeObject = value.keepWholeObject;
                useVisibleArea = value.useVisibleArea;
            }
        }

        internal ItemCameraSettings FindItemCamera(GameObject target) =>
            target == null
                ? null
                : itemCameras.FirstOrDefault(item => item.Reference.Target == target);

        internal IconCameraPose CameraFor(GameObject target) =>
            FindItemCamera(target) is { enabled: true } item ? item.pose : SharedCamera;

        internal void SetCamera(GameObject target, IconCameraPose pose)
        {
            if (FindItemCamera(target) is { enabled: true } item)
            {
                item.pose = pose;
            }
            else
            {
                SharedCamera = pose;
            }
        }

        internal void UseItemCamera(GameObject target, bool enabled)
        {
            if (target == null)
            {
                return;
            }

            var item = FindItemCamera(target);

            if (item == null && enabled)
            {
                item = new ItemCameraSettings
                {
                    objectId = SelectionReference.GetId(target),
                    pose = SharedCamera,
                };
                itemCameras.Add(item);
            }

            if (item != null)
            {
                item.enabled = enabled;
            }
        }

        [SerializeField]
        internal Vector3 cameraRotation = new(0, 180, 0);

        [SerializeField]
        internal Vector2 framingOffset;

        [SerializeField]
        internal float zoom = DefaultZoom;

        [SerializeField]
        internal float padding = DefaultPadding;

        [SerializeField]
        internal bool keepWholeObject;

        [SerializeField]
        internal bool useVisibleArea;

        [SerializeField]
        internal int resolution = DefaultResolution;

        [SerializeField]
        internal IconGenerationMode generationMode = IconGenerationMode.Both;

        [SerializeField]
        internal IconSaveScope saveScope = DefaultSaveScope;

        internal bool ShouldIncludeInactiveObjects(bool individual, IconSaveScope? scope = null)
        {
            var captureScope = scope ?? saveScope;

            return captureScope == IconSaveScope.SelectedItems
                || (individual && captureScope == IconSaveScope.AllItems);
        }

        [SerializeField]
        internal bool transparentBackground = true;

        [SerializeField]
        internal Color backgroundColor = Color.white;

        [SerializeField]
        internal bool outline;

        [SerializeField]
        internal Color outlineColor = Color.white;

        [SerializeField]
        internal int outlineWidth = 2;

        [SerializeField]
        internal string outputDirectory = "Assets/KOZ39/IconGenerator/Icons";

        [SerializeField]
        internal string fileName = DefaultFileNameTemplate;

        [SerializeField]
        internal ExistingFileAction existingFileAction = ExistingFileAction.Overwrite;

        [SerializeField]
        internal MenuIconLinkMode menuIconLinkMode = MenuIconLinkMode.Ask;

        internal void Initialize()
        {
            if (schemaVersion != 0)
            {
                Normalize();
                return;
            }

            cameraRotation = LegacySettingsMigration.ReadRotation();
            var legacyZoom = EditorPrefs.GetInt("IconGenZoomLevel", 100);
            zoom = legacyZoom > 0 ? legacyZoom / 100f : DefaultZoom;
            resolution = EditorPrefs.GetInt("IconGenIconSize", DefaultResolution);
            outputDirectory = EditorPrefs.GetString("IconGenOutputPath", outputDirectory);
            saveScope = EditorPrefs.GetBool(
                "IconGenCaptureInactiveObjects",
                DefaultSaveScope == IconSaveScope.AllItems
            )
                ? IconSaveScope.AllItems
                : IconSaveScope.CheckedItems;
            LegacySettingsMigration.MigrateLocale();
            schemaVersion = 1;
            Persist();
        }

        internal void Normalize()
        {
            sourceIds ??= new();
            excludedObjectIds ??= new();
            enabledObjectIds ??= new();
            ignoredBlendShapeObjectIds ??= new();
            itemCameras ??= new();
            itemCameras.RemoveAll(item => item == null || string.IsNullOrEmpty(item.objectId));
            SharedCamera = NormalizeCamera(SharedCamera);

            foreach (var item in itemCameras)
            {
                item.pose = NormalizeCamera(item.pose, item.enabled);
            }

            resolution = NormalizeResolution(resolution, IconResolutions);
            outlineWidth = Mathf.Clamp(outlineWidth, MinOutlineWidth, MaxOutlineWidth);
            existingFileAction = DefinedOrFallback(
                existingFileAction,
                ExistingFileAction.Overwrite
            );
            menuIconLinkMode = DefinedOrFallback(menuIconLinkMode, MenuIconLinkMode.Ask);
            blendShapeWeightMode = DefinedOrFallback(
                blendShapeWeightMode,
                DefaultBlendShapeWeightMode
            );
            generationMode = DefinedOrFallback(generationMode, IconGenerationMode.Both);
            saveScope = DefinedOrFallback(saveScope, DefaultSaveScope);
        }

        private static T DefinedOrFallback<T>(T value, T fallback)
            where T : struct, Enum => Enum.IsDefined(typeof(T), value) ? value : fallback;

        private static IconCameraPose NormalizeCamera(
            IconCameraPose pose,
            bool applyFramingLimit = true
        )
        {
            pose.rotation = NormalizeRotation(pose.rotation);
            pose.offset = new Vector2(
                FiniteOrFallback(pose.offset.x, 0),
                FiniteOrFallback(pose.offset.y, 0)
            );
            pose.padding = NormalizePadding(pose.padding);
            pose.zoom = NormalizeZoom(
                pose.zoom,
                pose.padding,
                applyFramingLimit && pose.keepWholeObject
            );

            return pose;
        }

        private static float NormalizePadding(float value) =>
            Mathf.Clamp(FiniteOrFallback(value, DefaultPadding), MinPadding, MaxPadding);

        internal static float NormalizeZoom(float value, float padding, bool keepWholeObject)
        {
            var result = Mathf.Max(MinZoom, FiniteOrFallback(value, DefaultZoom));

            return keepWholeObject ? Mathf.Min(result, 1 + padding) : result;
        }

        internal static int NormalizePreviewResolution(int value) =>
            NormalizeResolution(value, PreviewResolutions);

        private static int NormalizeResolution(int value, IReadOnlyList<int> choices) =>
            Mathf.ClosestPowerOfTwo(Mathf.Clamp(value, choices[0], choices[choices.Count - 1]));

        internal static Vector3 NormalizeRotation(Vector3 value) =>
            new(
                WrapAngle(FiniteOrFallback(value.x, 0), -180, 180),
                WrapAngle(FiniteOrFallback(value.y, 180), 0, 360),
                WrapAngle(FiniteOrFallback(value.z, 0), -180, 180)
            );

        private static float WrapAngle(float value, float minimum, float maximum) =>
            value >= minimum && value <= maximum
                ? value
                : Mathf.Repeat(value - minimum, maximum - minimum) + minimum;

        private static float FiniteOrFallback(float value, float fallback) =>
            float.IsFinite(value) ? value : fallback;

        internal void Persist()
        {
            Normalize();
            var references = (
                sourceIds,
                excludedObjectIds,
                enabledObjectIds,
                ignoredBlendShapeObjectIds
            );
            var cameras = itemCameras;

            try
            {
                sourceIds = PersistentReferences(sourceIds);
                excludedObjectIds = PersistentReferences(excludedObjectIds);
                enabledObjectIds = PersistentReferences(enabledObjectIds);
                ignoredBlendShapeObjectIds = PersistentReferences(ignoredBlendShapeObjectIds);
                itemCameras = cameras
                    .Where(item => !SelectionReference.IsSessionId(item.objectId))
                    .ToList();
                Save(true);
            }
            finally
            {
                (sourceIds, excludedObjectIds, enabledObjectIds, ignoredBlendShapeObjectIds) =
                    references;
                itemCameras = cameras;
            }
        }

        private static List<string> PersistentReferences(IEnumerable<string> ids) =>
            ids.Where(id => !string.IsNullOrEmpty(id) && !SelectionReference.IsSessionId(id))
                .ToList();
    }
}
