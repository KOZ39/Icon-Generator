using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.Linq;

public class IconGenerator : EditorWindow
{
    [SerializeField] private VisualTreeAsset _rootVisualTreeAsset;
    [SerializeField] private StyleSheet _rootStyleSheet;

    private string defaultDirPath = "Assets/KOZ39/IconGenerator/Icons/";
    private Localization localization;

    private PopupField<Data.UILanguage> languageField;
    private Image iconThumbnailPreview;
    private ObjectField objectField;
    private TextField outputPathField;
    private IntegerField tempCaptureResolutionField;
    private IntegerField zoomLevelField;
    private IntegerField iconSizeField;
    private EnumField captureDirectionField;
    private Toggle useCustomAngleToggle;
    private Vector3Field customCameraAngleField;
    private Button runButton;
    private Toggle goToOutputDirectory;

    [MenuItem("Tools/3D Obj to Icon")]
    private static void ShowWindow()
    {
        var window = GetWindow<IconGenerator>("Icon Generator");
        window.titleContent = new GUIContent("3D Obj to Icon");
        window.minSize = new Vector2(350, 670);
        window.Show();
    }

    private void OnEnable()
    {
        localization = new Localization();
        localization.SetupLocalization();
        CreateGUI();
    }

    private void OnDisable()
    {
        if (iconThumbnailPreview != null && iconThumbnailPreview.image != null)
        {
            DestroyImmediate(iconThumbnailPreview.image);
            iconThumbnailPreview.image = null;
        }
    }

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.Clear();

        Data.UILanguage savedLanguage = EditorPrefs.HasKey(Data.LANGUAGE_PREF_KEY) ? (Data.UILanguage)EditorPrefs.GetInt(Data.LANGUAGE_PREF_KEY) : Data.UILanguage.English;
        languageField = new PopupField<Data.UILanguage>(localization.GetLocalizedText("UILanguage"));
        languageField.choices = System.Enum.GetValues(typeof(Data.UILanguage)).Cast<Data.UILanguage>().ToList();
        languageField.formatListItemCallback = lang => Data.LanguageMapping.TryGetValue(lang, out var info) ? info.DisplayName : lang.ToString();
        languageField.formatSelectedValueCallback = lang => Data.LanguageMapping.TryGetValue(lang, out var info) ? info.DisplayName : lang.ToString();
        languageField.value = savedLanguage;
        languageField.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetInt(Data.LANGUAGE_PREF_KEY, (int)evt.newValue);
            GameObject currentSourceObject = objectField?.value as GameObject;
            CreateGUI();
            if (objectField != null && currentSourceObject != null)
            {
                objectField.value = currentSourceObject;
            }
        });
        root.Add(languageField);

        var iconThumbnailLabelContainer = new VisualElement()
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                marginTop = 10,
                marginBottom = 2,
                marginLeft = 2
            }
        };
        root.Add(iconThumbnailLabelContainer);

        var iconThumbnailLabel = new Label(localization.GetLocalizedText("IconThumbnailPreview"))
        {
            style =
            {
                fontSize = 12,
                unityFontStyleAndWeight = FontStyle.Bold,
                marginRight = 2
            }
        };
        iconThumbnailLabelContainer.Add(iconThumbnailLabel);

        iconThumbnailPreview = new Image();
        iconThumbnailPreview.style.width = iconThumbnailPreview.style.height = Data.ICON_THUMBNAIL_DISPLAY_RESOLUTION;
        iconThumbnailPreview.style.alignSelf = Align.Center;
        iconThumbnailPreview.style.marginTop = 5;
        iconThumbnailPreview.style.marginBottom = 10;
        iconThumbnailPreview.style.borderBottomColor = iconThumbnailPreview.style.borderLeftColor = iconThumbnailPreview.style.borderRightColor = iconThumbnailPreview.style.borderTopColor = Color.gray;
        iconThumbnailPreview.style.borderBottomWidth = iconThumbnailPreview.style.borderLeftWidth = iconThumbnailPreview.style.borderRightWidth = iconThumbnailPreview.style.borderTopWidth = 1;
        root.Add(iconThumbnailPreview);

        Utils.AddSectionTitle(root, localization.GetLocalizedText("SourceObject"));
        objectField = new ObjectField() { objectType = typeof(GameObject), label = "" };
        Utils.SetStyleMarginBottom(objectField);
        objectField.RegisterValueChangedCallback(evt => UpdateIconThumbnailPreview());
        root.Add(objectField);

        Utils.AddSectionTitle(root, localization.GetLocalizedText("IconSettings"));
        outputPathField = new TextField(localization.GetLocalizedText("OutputDirectory")) { value = EditorPrefs.GetString(Data.OUTPUT_PATH_PREF_KEY, defaultDirPath) };
        outputPathField.RegisterValueChangedCallback(evt => EditorPrefs.SetString(Data.OUTPUT_PATH_PREF_KEY, evt.newValue));
        root.Add(outputPathField);

        tempCaptureResolutionField = new IntegerField(localization.GetLocalizedText("TempCaptureResolution")) { value = EditorPrefs.GetInt(Data.TEMP_RESOLUTION_PREF_KEY, Data.DEFAULT_TEMP_CAPTURE_RESOLUTION) };
        tempCaptureResolutionField.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetInt(Data.TEMP_RESOLUTION_PREF_KEY, evt.newValue);
            UpdateIconThumbnailPreview();
        });
        root.Add(tempCaptureResolutionField);

        zoomLevelField = new IntegerField(localization.GetLocalizedText("ZoomLevel"));
        zoomLevelField.value = EditorPrefs.GetInt(Data.ZOOM_LEVEL_PREF_KEY, Data.DEFAULT_ZOOM_LEVEL);
        zoomLevelField.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetInt(Data.ZOOM_LEVEL_PREF_KEY, evt.newValue);
            UpdateIconThumbnailPreview();
        });
        root.Add(zoomLevelField);

        iconSizeField = new IntegerField(localization.GetLocalizedText("OutputIconSize")) { value = EditorPrefs.GetInt(Data.ICON_SIZE_PREF_KEY, 256) };
        iconSizeField.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetInt(Data.ICON_SIZE_PREF_KEY, evt.newValue);
            UpdateIconThumbnailPreview();
        });
        Utils.SetStyleMarginBottom(iconSizeField);
        root.Add(iconSizeField);

        Utils.AddSectionTitle(root, localization.GetLocalizedText("CameraSettings"));
        Data.CaptureDirection savedDirection = EditorPrefs.HasKey(Data.CAPTURE_DIRECTION_PREF_KEY) ? (Data.CaptureDirection)EditorPrefs.GetInt(Data.CAPTURE_DIRECTION_PREF_KEY) : Data.CaptureDirection.Front;
        captureDirectionField = new EnumField(localization.GetLocalizedText("CaptureDirection"), savedDirection);
        captureDirectionField.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetInt(Data.CAPTURE_DIRECTION_PREF_KEY, (int)(Data.CaptureDirection)evt.newValue);
            UpdateIconThumbnailPreview();
        });
        root.Add(captureDirectionField);

        useCustomAngleToggle = new Toggle(localization.GetLocalizedText("UseCustomAngle"));
        useCustomAngleToggle.value = EditorPrefs.GetBool(Data.USE_CUSTOM_ANGLE_PREF_KEY, false);
        useCustomAngleToggle.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetBool(Data.USE_CUSTOM_ANGLE_PREF_KEY, evt.newValue);
            customCameraAngleField.SetEnabled(evt.newValue);
            captureDirectionField.SetEnabled(!evt.newValue);
            UpdateIconThumbnailPreview();
        });
        root.Add(useCustomAngleToggle);

        customCameraAngleField = new Vector3Field();
        customCameraAngleField.value = new Vector3(EditorPrefs.GetFloat(Data.CUSTOM_ANGLE_PREF_KEY_X, 0), EditorPrefs.GetFloat(Data.CUSTOM_ANGLE_PREF_KEY_Y, 180), EditorPrefs.GetFloat(Data.CUSTOM_ANGLE_PREF_KEY_Z, 0));
        customCameraAngleField.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetFloat(Data.CUSTOM_ANGLE_PREF_KEY_X, evt.newValue.x);
            EditorPrefs.SetFloat(Data.CUSTOM_ANGLE_PREF_KEY_Y, evt.newValue.y);
            EditorPrefs.SetFloat(Data.CUSTOM_ANGLE_PREF_KEY_Z, evt.newValue.z);
            UpdateIconThumbnailPreview();
        });
        Utils.SetStyleMarginBottom(customCameraAngleField);
        root.Add(customCameraAngleField);

        customCameraAngleField.SetEnabled(useCustomAngleToggle.value);
        captureDirectionField.SetEnabled(!useCustomAngleToggle.value);

        runButton = new Button(ProcessIconGeneration) { text = localization.GetLocalizedText("GenerateButton") };
        runButton.style.marginTop = 10;
        runButton.style.height = 25;
        Utils.SetStyleMarginBottom(runButton);
        root.Add(runButton);

        goToOutputDirectory = new Toggle(localization.GetLocalizedText("GoToOutputDirectory"));
        goToOutputDirectory.value = EditorPrefs.GetBool(Data.PING_ASSET_PREF_KEY, true);
        goToOutputDirectory.RegisterValueChangedCallback(evt => EditorPrefs.SetBool(Data.PING_ASSET_PREF_KEY, evt.newValue));
        root.Add(goToOutputDirectory);

        UpdateIconThumbnailPreview();
    }

    private void UpdateIconThumbnailPreview()
    {
        if (iconThumbnailPreview != null && iconThumbnailPreview.image != null)
        {
            DestroyImmediate(iconThumbnailPreview.image);
            iconThumbnailPreview.image = null;
        }

        GameObject sourceObject = objectField.value as GameObject;
        if (sourceObject == null)
        {
            return;
        }

        if (sourceObject.CompareTag("EditorOnly"))
        {
            Debug.LogWarning(localization.GetLocalizedText("SourceObjectExcludedWarning", sourceObject.name));
            return;
        }

        List<GameObject> objectsToProcess = Processor.FindObjectsToProcess(sourceObject, includeInactive: false);

        if (objectsToProcess.Count == 0)
        {
            Debug.LogWarning(localization.GetLocalizedText("NoActiveProcessableObjectsCombined"));
            return;
        }

        bool useCustom = useCustomAngleToggle.value;
        Vector3 customAngle = customCameraAngleField.value;
        Data.CaptureDirection selectedDirection = (Data.CaptureDirection)captureDirectionField.value;

        int currentTempResolution = tempCaptureResolutionField.value > 0 ? tempCaptureResolutionField.value : Data.DEFAULT_TEMP_CAPTURE_RESOLUTION;
        int currentIconSize = iconSizeField.value > 0 ? iconSizeField.value : 256;
        int currentZoomLevel = zoomLevelField.value;

        GameObject tempCombinedParent = null;
        Texture2D iconThumbnailTexture = null;

        try
        {
            tempCombinedParent = Core.CreateTemporaryCombinedObject(sourceObject, objectsToProcess);
            if (tempCombinedParent == null)
            {
                Debug.LogWarning(localization.GetLocalizedText("NoActiveProcessableObjectsCombined"));
                return;
            }

            iconThumbnailTexture = Core.GenerateIconInternal(tempCombinedParent, currentIconSize, selectedDirection, useCustom, customAngle, currentTempResolution, currentZoomLevel, localization);

            if (iconThumbnailTexture != null)
            {
                if (iconThumbnailPreview != null)
                {
                    iconThumbnailPreview.image = Utils.ResizeTexture(iconThumbnailTexture, Data.ICON_THUMBNAIL_DISPLAY_RESOLUTION, Data.ICON_THUMBNAIL_DISPLAY_RESOLUTION);
                    DestroyImmediate(iconThumbnailTexture);
                }
            }
            else
            {
                Debug.LogError(localization.GetLocalizedText("GenerationFailedError", sourceObject.name) + " (" + localization.GetLocalizedText("IconThumbnail") + ")");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError(localization.GetLocalizedText("GenerationError", sourceObject.name, e.Message, e.StackTrace) + " (" + localization.GetLocalizedText("IconThumbnail") + ")");
        }
        finally
        {
            if (tempCombinedParent != null)
            {
                DestroyImmediate(tempCombinedParent);
            }
        }
    }

    private void ProcessIconGeneration()
    {
        GameObject sourceObject = objectField.value as GameObject;
        if (sourceObject == null)
        {
            Debug.LogError(localization.GetLocalizedText("NoObjectSelected"));
            return;
        }

        if (sourceObject.CompareTag("EditorOnly"))
        {
            Debug.LogWarning(localization.GetLocalizedText("SourceObjectExcludedWarning", sourceObject.name));
            return;
        }

        int targetSize = iconSizeField.value > 0 ? iconSizeField.value : 256;
        string outputDirectory = string.IsNullOrWhiteSpace(outputPathField.value) ? defaultDirPath : outputPathField.value;

        if (!Processor.EnsureOutputDirectoryExists(outputDirectory, localization))
        {
            return;
        }

        bool useCustom = useCustomAngleToggle.value;
        Vector3 customAngle = customCameraAngleField.value;
        Data.CaptureDirection selectedDirection = (Data.CaptureDirection)captureDirectionField.value;

        List<GameObject> objectsToProcessForIndividual = Processor.FindObjectsToProcess(sourceObject, includeInactive: true);
        List<GameObject> objectsToProcessForCombined = Processor.FindObjectsToProcess(sourceObject, includeInactive: false);

        if (objectsToProcessForIndividual.Count == 0 && objectsToProcessForCombined.Count == 0)
        {
            Debug.LogWarning(localization.GetLocalizedText("NoProcessableObjects", sourceObject.name));
            return;
        }

        bool generateCombined = objectsToProcessForCombined.Count > 1;

        int currentTempResolution = tempCaptureResolutionField.value > 0 ? tempCaptureResolutionField.value : Data.DEFAULT_TEMP_CAPTURE_RESOLUTION;
        int currentZoomLevel = zoomLevelField.value;

        if (generateCombined)
        {
            Processor.GenerateCombinedIcon(sourceObject, objectsToProcessForCombined, targetSize, selectedDirection, useCustom, customAngle, outputDirectory, currentTempResolution, currentZoomLevel, goToOutputDirectory.value, localization);
        }

        Processor.GenerateIndividualIcons(objectsToProcessForIndividual, targetSize, selectedDirection, useCustom, customAngle, outputDirectory, generateCombined, currentTempResolution, currentZoomLevel, goToOutputDirectory.value, localization);

        EditorUtility.ClearProgressBar();
    }
}
