using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KOZ39.IconGenerator
{
    internal sealed class IconGeneratorView
    {
        private const string DecimalFormat = "0.##";

        private static readonly BlendShapeWeightMode[] _blendShapeWeightChoices =
        {
            BlendShapeWeightMode.SingleMesh,
            BlendShapeWeightMode.SelectedMeshes,
            BlendShapeWeightMode.AllMeshes,
            BlendShapeWeightMode.None,
        };

        private readonly IconGeneratorWindow _window;
        private readonly IconGeneratorLocalization _localization;
        private readonly IconGeneratorSettings _settings;
        private readonly SerializedObject _serializedSettings;
        private readonly CaptureSelectionStore _selectionStore;
        private readonly List<(Button Button, Func<bool> IsModified)> _resetButtons = new();
        private readonly Dictionary<string, Button> _cameraPresetButtons = new();
        private readonly List<(Slider Slider, int Axis)> _rotationSliders = new();
        private Toggle _useItemCameraToggle;
        private SourceTreeView _sourceTree;
        private Button _generateButton;
        private Button _openFolderButton;
        private Slider _zoomSlider;
        private Slider _paddingSlider;
        private FloatField _zoomField;
        private FloatField _horizontalPositionField;
        private FloatField _verticalPositionField;
        private ColorField _backgroundColorField;
        private ColorField _outlineColorField;
        private VisualElement _outlineOptions;
        private PopupField<IconSaveScope> _saveScopeField;
        private PopupField<BlendShapeWeightMode> _blendShapeWeightModeField;
        private PopupField<int> _previewResolutionField;
        private Toggle _previewOutputSizeToggle;
        private Toggle _keepWholeObjectToggle;
        private Toggle _useVisibleAreaToggle;
        private Label _previewTargetLabel;
        private Label _combinedPreviewLabel;
        private VisualElement _previewPages;
        private VisualElement _selectedPreviewPane;
        private VisualElement _combinedPreviewPane;
        private string _cachedOutputDirectory;
        private bool _outputFolderStateInitialized;
        internal IconPreview SelectedPreview { get; private set; }
        internal IconPreview CombinedPreview { get; private set; }

        internal IconGeneratorView(
            IconGeneratorWindow window,
            IconGeneratorLocalization localization,
            IconGeneratorSettings settings,
            SerializedObject serializedSettings,
            CaptureSelectionStore selectionStore
        )
        {
            _window = window;
            _localization = localization;
            _settings = settings;
            _serializedSettings = serializedSettings;
            _selectionStore = selectionStore;
        }

        internal void Build(VisualElement root)
        {
            root.Clear();
            _resetButtons.Clear();
            _cameraPresetButtons.Clear();
            _rotationSliders.Clear();
            root.AddToClassList("icon-generator");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{IconGeneratorPackageInfo.AssetPath}/Editor/UI/IconGeneratorView.uss"
            );

            if (style != null && !root.styleSheets.Contains(style))
            {
                root.styleSheets.Add(style);
            }

            var toolbar = new Toolbar();
            toolbar.AddToClassList("window-toolbar");
            var version = new Label($"Version: {IconGeneratorPackageInfo.Version}");
            version.AddToClassList("toolbar-version");
            toolbar.Add(version);
            var locales = _localization.Locales;
            string LocaleDisplayName(string code) =>
                locales.First(locale => locale.Code == code).DisplayName;
            var language = new PopupField<string>(
                locales.Select(locale => locale.Code).ToList(),
                Mathf.Max(
                    0,
                    locales.FindIndex(locale => locale.Code == _localization.SelectedCode)
                ),
                LocaleDisplayName,
                LocaleDisplayName
            );
            language.AddToClassList("language-picker");
            language.RegisterValueChangedCallback(change =>
            {
                _localization.SelectedCode = change.newValue;
                _window.RebuildView();
            });
            toolbar.Add(language);
            root.Add(toolbar);
            var body = new TwoPaneSplitView(1, 340, TwoPaneSplitViewOrientation.Horizontal)
            {
                name = "preview-settings-split",
                viewDataKey = "preview-settings-split",
            };
            body.AddToClassList("body");
            var controls = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            controls.AddToClassList("controls");
            BuildSelection(controls);
            BuildCamera(controls);
            BuildOutput(controls);
            var previewPanel = new VisualElement();
            previewPanel.AddToClassList("preview-panel");
            var previewOptions = new VisualElement();
            previewOptions.AddToClassList("preview-options");
            _previewResolutionField = new PopupField<int>(
                _localization.Text("labels.PreviewResolution")
            )
            {
                choices = IconGeneratorSettings.PreviewResolutions.ToList(),
                tooltip = _localization.Text("tooltips.PreviewResolution"),
                formatSelectedValueCallback = FormatResolution,
                formatListItemCallback = FormatResolution,
            };
            _previewResolutionField.AddToClassList("preview-resolution");
            _previewResolutionField.SetValueWithoutNotify(_window.PreviewResolution);
            _previewResolutionField.RegisterValueChangedCallback(change =>
                _window.PreviewResolution = change.newValue
            );
            previewOptions.Add(_previewResolutionField);
            var gridToggle = new Toggle
            {
                text = _localization.Text("labels.Grid"),
                value = _window.ShowGrid,
            };
            gridToggle.AddToClassList("grid-toggle");
            gridToggle.RegisterValueChangedCallback(change =>
            {
                _window.ShowGrid = change.newValue;
                UpdatePreviewLayout();
            });
            previewPanel.Add(previewOptions);
            _previewOutputSizeToggle = CreateToggle(
                "preview-output-size",
                "PreviewAtIconResolution",
                value => _window.PreviewAtOutputSize = value
            );
            var previewToggles = new VisualElement();
            previewToggles.AddToClassList("preview-toggles");
            previewToggles.Add(_previewOutputSizeToggle);
            previewToggles.Add(gridToggle);
            previewOptions.Add(previewToggles);
            var previewActions = new VisualElement();
            previewActions.AddToClassList("preview-actions");
            var resetViewButton = new Button(_window.ResetView)
            {
                text = _localization.Text("buttons.ResetCamera"),
                tooltip = _localization.Text("tooltips.ResetCamera"),
            };
            resetViewButton.AddToClassList("reset-view-button");
            _resetButtons.Add(
                (
                    resetViewButton,
                    () =>
                        !_window.EditingCamera.rotation.Equals(new Vector3(0, 180, 0))
                        || !_window.EditingCamera.offset.Equals(Vector2.zero)
                        || _window.EditingCamera.zoom != IconGeneratorSettings.DefaultZoom
                )
            );
            previewActions.Add(resetViewButton);
            previewOptions.Add(previewActions);
            _previewPages = new VisualElement { name = "preview-pages" };
            SelectedPreview = CreatePreview("selected-preview");
            _selectedPreviewPane = CreatePreviewPane(
                SelectedPreview,
                "selected-preview-pane",
                out _previewTargetLabel
            );
            _previewTargetLabel.name = "preview-target-name";
            RegisterElementTooltip(
                _selectedPreviewPane,
                _previewTargetLabel,
                () =>
                    _window.PreviewTarget != null
                        ? _window.GetCaptureTargetTooltip(_window.PreviewTarget)
                        : _previewTargetLabel.tooltip
            );
            _previewPages.Add(_selectedPreviewPane);
            CombinedPreview = CreatePreview("combined-preview");
            _combinedPreviewPane = CreatePreviewPane(
                CombinedPreview,
                "combined-preview-pane",
                out _combinedPreviewLabel
            );
            _previewPages.Add(_combinedPreviewPane);
            _previewPages.RegisterCallback<GeometryChangedEvent>(_ => UpdatePreviewLayout());
            _previewTargetLabel.AddManipulator(
                new ContextualMenuManipulator(evt =>
                {
                    var target = _window.PreviewTarget;

                    if (target != null)
                    {
                        evt.menu.AppendAction(
                            _localization.Text("menus.SaveItem"),
                            _ => _window.GenerateTarget(target),
                            _window.CanCaptureTarget(target)
                                ? DropdownMenuAction.Status.Normal
                                : DropdownMenuAction.Status.Disabled
                        );
                    }
                })
            );
            previewPanel.Add(_previewPages);
            var previewControlsHint = new VisualElement { name = "preview-controls-hint" };

            foreach (var line in _localization.Text("labels.PreviewControls").Split('\n'))
            {
                previewControlsHint.Add(CreatePreviewControlsLine(line));
            }

            previewPanel.Add(previewControlsHint);
            body.Add(previewPanel);
            body.Add(controls);
            root.Add(body);
            var footer = new VisualElement();
            footer.AddToClassList("footer");
            _generateButton = new Button(_window.Generate)
            {
                text = _localization.Text("buttons.GenerateIcon"),
            };
            _generateButton.AddToClassList("generate-button");
            footer.Add(_generateButton);
            root.Add(footer);

            foreach (var group in controls.Query(className: "settings-group").ToList())
            {
                foreach (var field in group.Children())
                {
                    if (field.ClassListContains("unity-base-field"))
                    {
                        field.AddToClassList("settings-field");
                    }
                }
            }

            root.Bind(_serializedSettings);
            root.TrackSerializedObjectValue(_serializedSettings, _ => _window.SettingsChanged());
            RefreshSourceTree();
            RefreshSettings();
        }

        private static string FormatResolution(int value) => $"{value} × {value}";

        internal static Func<T, string> FormatOption<T>(
            IconGeneratorLocalization localization,
            string optionPrefix
        ) => value => localization.Text($"options.{optionPrefix}{value}");

        internal static List<T> EnumChoices<T>()
            where T : Enum => Enum.GetValues(typeof(T)).Cast<T>().ToList();

        internal static void RegisterElementTooltip(
            VisualElement host,
            VisualElement element,
            Func<string> getTooltip
        ) =>
            host.RegisterCallback<TooltipEvent>(
                evt =>
                {
                    if (evt.target != element)
                    {
                        return;
                    }

                    evt.tooltip = getTooltip();
                    element.tooltip = evt.tooltip;
                    evt.rect = element.worldBound;
                    evt.StopImmediatePropagation();
                },
                TrickleDown.TrickleDown
            );

        private Toggle CreateToggle(string name, string key, Action<bool> onChanged)
        {
            var toggle = new Toggle
            {
                name = name,
                text = _localization.Text($"labels.{key}"),
                tooltip = _localization.Text($"tooltips.{key}"),
            };
            toggle.RegisterValueChangedCallback(change => onChanged(change.newValue));

            return toggle;
        }

        private static VisualElement CreatePreviewControlsLine(string text)
        {
            var line = new VisualElement();
            line.AddToClassList("preview-controls-line");
            var actions = text.Split('·').Select(action => new Label(action.Trim())).ToArray();

            foreach (var action in actions)
            {
                action.AddToClassList("preview-control-action");
                line.Add(action);
            }

            for (var index = 1; index < actions.Length; index++)
            {
                var previous = actions[index - 1];
                var next = actions[index];
                var separator = new Label("·") { pickingMode = PickingMode.Ignore };
                separator.AddToClassList("preview-controls-separator");
                line.Add(separator);
                line.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var before = previous.layout;
                    var after = next.layout;
                    var sameRow = Mathf.Abs(before.y - after.y) < 1;
                    separator.style.visibility = sameRow ? Visibility.Visible : Visibility.Hidden;

                    if (sameRow)
                    {
                        separator.style.left = before.xMax;
                        separator.style.top = before.y;
                        separator.style.width = Mathf.Max(0, after.xMin - before.xMax);
                        separator.style.height = before.height;
                    }
                });
            }

            return line;
        }

        private VisualElement CreatePreviewPane(IconPreview preview, string name, out Label title)
        {
            var pane = new VisualElement { name = name };
            pane.AddToClassList("preview-pane");
            pane.Add(preview);
            var caption = new VisualElement();
            caption.AddToClassList("preview-caption");
            var foldIndicator = new Label { pickingMode = PickingMode.Ignore };
            foldIndicator.AddToClassList("preview-fold-indicator");
            title = new Label();
            title.AddToClassList("preview-pane-title");
            caption.Add(title);
            caption.Add(foldIndicator);
            pane.Insert(0, caption);
            caption.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button == 0 && pane.ClassListContains("preview-inset"))
                {
                    _window.InsetPreviewCollapsed = !_window.InsetPreviewCollapsed;
                    evt.StopPropagation();
                }
            });
            pane.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button == 0 && pane.ClassListContains("preview-inset"))
                {
                    _window.SwapPreviews();
                    evt.StopPropagation();
                }
            });

            return pane;
        }

        private void UpdatePreviewLayout()
        {
            var combinedPrimary = _window.CombinedPreviewPrimary;
            var showInset = _window.ShowCombinedPreview;
            var inset = combinedPrimary ? _selectedPreviewPane : _combinedPreviewPane;
            var primary = combinedPrimary ? _combinedPreviewPane : _selectedPreviewPane;
            _selectedPreviewPane.EnableInClassList("preview-inset", combinedPrimary);
            _combinedPreviewPane.EnableInClassList("preview-inset", !combinedPrimary);
            _selectedPreviewPane.style.display =
                !combinedPrimary || showInset ? DisplayStyle.Flex : DisplayStyle.None;
            _combinedPreviewPane.style.display =
                combinedPrimary || showInset ? DisplayStyle.Flex : DisplayStyle.None;
            primary.style.width = StyleKeyword.Null;
            primary.style.height = StyleKeyword.Null;
            var size = Mathf.Clamp(
                Mathf.Min(_previewPages.contentRect.width, _previewPages.contentRect.height)
                    * 0.32f,
                96,
                160
            );
            inset.style.width = size;
            inset.Q<Label>(className: "preview-fold-indicator").text = _window.InsetPreviewCollapsed
                ? "▲"
                : "▼";
            inset.style.height = _window.InsetPreviewCollapsed
                ? EditorGUIUtility.singleLineHeight + 2
                : size + EditorGUIUtility.singleLineHeight + 4;
            SelectedPreview.style.display =
                combinedPrimary && _window.InsetPreviewCollapsed
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            CombinedPreview.style.display =
                !combinedPrimary && _window.InsetPreviewCollapsed
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;

            if (_previewPages[_previewPages.childCount - 1] != inset)
            {
                inset.BringToFront();
            }

            SelectedPreview.ShowAtOutputSize = _window.PreviewAtOutputSize && !combinedPrimary;
            CombinedPreview.ShowAtOutputSize = _window.PreviewAtOutputSize && combinedPrimary;
            var showGrid = _window.ShowGrid;
            SelectedPreview.ShowGrid = showGrid && !combinedPrimary;
            CombinedPreview.ShowGrid = showGrid && combinedPrimary;
            SelectedPreview.CameraControlsEnabled = !combinedPrimary;
            CombinedPreview.CameraControlsEnabled = combinedPrimary;
        }

        private IconPreview CreatePreview(string name)
        {
            var preview = new IconPreview { name = name };
            preview.InteractionStarted += _window.BeginViewInteraction;
            preview.InteractionEnded += _window.EndViewInteraction;
            preview.OrbitDragged += _window.OrbitPreview;
            preview.PanDragged += _window.PanPreview;
            preview.ZoomScrolled += _window.ZoomPreview;
            preview.FrameRequested += _window.FramePreview;

            return preview;
        }

        private VisualElement AddSection(VisualElement root, string key)
        {
            var section = new VisualElement { name = $"section-{key.ToLowerInvariant()}" };
            section.AddToClassList("settings-section");
            section.EnableInClassList("first-section", root.childCount == 0);
            var heading = new Label(_localization.Text($"titles.{key}"));
            heading.AddToClassList("section-title");
            section.Add(heading);
            root.Add(section);

            return section;
        }

        private static VisualElement AddSettingsGroup(VisualElement section, string name)
        {
            var group = new VisualElement { name = name };
            group.AddToClassList("settings-group");
            group.EnableInClassList(
                "first-group",
                !section.Children().Any(child => child.ClassListContains("settings-group"))
            );
            section.Add(group);

            return group;
        }

        private VisualElement AddResettableGroup(
            VisualElement section,
            string name,
            string resetName,
            string resetKey,
            Action reset,
            Func<bool> isModified
        )
        {
            var group = AddSettingsGroup(section, name);
            group.AddToClassList("resettable-settings-group");
            var fields = new VisualElement();
            fields.AddToClassList("group-fields");
            group.Add(fields);
            var button = new Button(reset)
            {
                name = resetName,
                text = _localization.Text("buttons.Reset"),
                tooltip = _localization.Text($"tooltips.{resetKey}"),
            };
            group.Add(button);
            _resetButtons.Add((button, isModified));

            return fields;
        }

        private void BuildSelection(VisualElement root)
        {
            var section = AddSection(root, "Sources");
            var sources = AddSettingsGroup(section, "source-settings");
            _sourceTree = new SourceTreeView(_window, _localization, _selectionStore);
            var headingRow = new VisualElement();
            headingRow.AddToClassList("section-heading-row");
            headingRow.Add(section.Q<Label>(className: "section-title"));
            headingRow.Add(_sourceTree.Q<ObjectField>("add-source-row"));
            _sourceTree.Insert(0, headingRow);
            sources.Add(_sourceTree);
            var formatBlendShapeWeightMode = FormatOption<BlendShapeWeightMode>(
                _localization,
                "BlendShapeWeightMode"
            );
            _blendShapeWeightModeField = new PopupField<BlendShapeWeightMode>(
                _localization.Text("labels.BlendShapeWeightMode"),
                _blendShapeWeightChoices.ToList(),
                _settings.blendShapeWeightMode,
                formatBlendShapeWeightMode,
                formatBlendShapeWeightMode
            )
            {
                name = "blend-shape-weight-mode",
                tooltip = _localization.Text("tooltips.BlendShapeWeightMode"),
            };
            _blendShapeWeightModeField.RegisterValueChangedCallback(change =>
                _window.SetBlendShapeWeightMode(change.newValue)
            );
            sources.Add(_blendShapeWeightModeField);
        }

        private void BuildCamera(VisualElement root)
        {
            var section = AddSection(root, "Camera");
            var heading = section.Q<Label>(className: "section-title");
            heading.AddToClassList("camera-heading");
            var headingRow = new VisualElement();
            headingRow.AddToClassList("section-heading-row");
            headingRow.Add(heading);
            section.Add(headingRow);
            _useItemCameraToggle = CreateToggle(
                "use-item-camera",
                "UseItemCamera",
                _window.SetItemCameraEnabled
            );
            headingRow.Add(_useItemCameraToggle);
            var directions = AddSettingsGroup(section, "camera-presets");
            var presets = IconGeneratorSettings.CameraPresets;

            for (var start = 0; start < presets.Count; start += 4)
            {
                var row = new VisualElement();
                row.AddToClassList("preset-row");

                for (var i = start; i < Mathf.Min(start + 4, presets.Count); i++)
                {
                    var preset = presets[i];
                    var cell = new VisualElement();
                    cell.AddToClassList("preset-cell");
                    var button = new Button(() => _window.SetRotation(preset.Rotation))
                    {
                        name = $"camera-preset-{preset.Name}",
                        text = _localization.Text($"buttons.Camera{preset.Name}"),
                    };
                    _cameraPresetButtons.Add(preset.Name, button);
                    cell.Add(button);
                    row.Add(cell);
                }

                directions.Add(row);
            }

            var position = AddResettableGroup(
                section,
                "camera-position",
                "reset-position",
                "ResetPosition",
                () => _window.SetFramingPositionPercent(Vector2.zero),
                () => !_window.EditingCamera.offset.Equals(Vector2.zero)
            );
            _horizontalPositionField = CreatePositionField(
                "horizontal-position",
                "HorizontalPosition"
            );
            _verticalPositionField = CreatePositionField("vertical-position", "VerticalPosition");
            _horizontalPositionField.RegisterValueChangedCallback(change =>
                _window.SetFramingPositionPercent(
                    new Vector2(change.newValue, -_window.EditingCamera.offset.y * 100)
                )
            );
            _verticalPositionField.RegisterValueChangedCallback(change =>
                _window.SetFramingPositionPercent(
                    new Vector2(-_window.EditingCamera.offset.x * 100, change.newValue)
                )
            );
            position.Add(_horizontalPositionField);
            position.Add(_verticalPositionField);
            var rotation = AddSettingsGroup(section, "camera-rotation");
            AddResettableSlider(
                rotation,
                "Pitch",
                -180,
                180,
                nameof(_settings.cameraRotation) + ".x",
                0,
                () =>
                    _window.SetRotation(
                        new Vector3(
                            0,
                            _window.EditingCamera.rotation.y,
                            _window.EditingCamera.rotation.z
                        )
                    )
            );
            AddResettableSlider(
                rotation,
                "Yaw",
                0,
                360,
                nameof(_settings.cameraRotation) + ".y",
                180,
                () =>
                    _window.SetRotation(
                        new Vector3(
                            _window.EditingCamera.rotation.x,
                            180,
                            _window.EditingCamera.rotation.z
                        )
                    )
            );
            AddResettableSlider(
                rotation,
                "Roll",
                -180,
                180,
                nameof(_settings.cameraRotation) + ".z",
                0,
                () =>
                    _window.SetRotation(
                        new Vector3(
                            _window.EditingCamera.rotation.x,
                            _window.EditingCamera.rotation.y,
                            0
                        )
                    )
            );
            var framing = AddSettingsGroup(section, "camera-framing");
            var zoomRow = new VisualElement();
            zoomRow.AddToClassList("setting-with-reset");
            _zoomSlider = new Slider(
                _localization.Text("labels.Zoom"),
                IconGeneratorSettings.SliderMinZoom,
                IconGeneratorSettings.SliderMaxZoom
            )
            {
                name = "zoom-slider",
                tooltip = _localization.Text("tooltips.Zoom"),
            };
            _zoomSlider.AddToClassList("settings-field");
            _zoomField = new FloatField
            {
                name = nameof(_settings.zoom),
                isDelayed = true,
                formatString = DecimalFormat,
            };
            _zoomField.AddToClassList("unity-base-slider__text-field");
            _zoomField.RegisterValueChangedCallback(change => _window.SetZoom(change.newValue));
            _zoomSlider.Q(className: Slider.inputUssClassName).Add(_zoomField);
            _zoomSlider.RegisterValueChangedCallback(change =>
            {
                if (change.target == _zoomSlider)
                {
                    _zoomField.value = change.newValue;
                }
            });
            zoomRow.Add(_zoomSlider);
            AddResetButton(
                zoomRow,
                "Zoom",
                _window.ResetZoom,
                () => _window.EditingCamera.zoom != IconGeneratorSettings.DefaultZoom
            );
            framing.Add(zoomRow);
            _paddingSlider = AddResettableSlider(
                framing,
                "Padding",
                IconGeneratorSettings.MinPadding,
                IconGeneratorSettings.MaxPadding,
                nameof(_settings.padding),
                IconGeneratorSettings.DefaultPadding,
                _window.ResetPadding
            );
            _paddingSlider.tooltip = _localization.Text("tooltips.Padding");
            var framingOptions = new VisualElement { name = "framing-options" };
            framing.Add(framingOptions);
            _keepWholeObjectToggle = CreateToggle(
                "keep-whole-object",
                "KeepWholeObject",
                _window.SetKeepWholeObject
            );
            framingOptions.Add(_keepWholeObjectToggle);
            _useVisibleAreaToggle = CreateToggle(
                "use-visible-area",
                "UseVisibleArea",
                _window.SetUseVisibleArea
            );
            framingOptions.Add(_useVisibleAreaToggle);
        }

        private FloatField CreatePositionField(string name, string key)
        {
            var field = new FloatField(_localization.Text($"labels.{key}"))
            {
                name = name,
                isDelayed = true,
                formatString = DecimalFormat,
                tooltip = _localization.Text($"tooltips.{key}"),
            };
            field.AddToClassList("settings-field");

            return field;
        }

        private Slider AddSlider(
            VisualElement root,
            string key,
            float minimum,
            float maximum,
            string path
        )
        {
            var slider = new TwoDecimalSlider(_localization.Text($"labels.{key}"), minimum, maximum)
            {
                name = path,
            };
            slider.AddToClassList("settings-field");
            root.Add(slider);

            return slider;
        }

        private Slider AddResettableSlider(
            VisualElement root,
            string key,
            float minimum,
            float maximum,
            string path,
            float defaultValue,
            Action reset
        )
        {
            var row = new VisualElement();
            row.AddToClassList("setting-with-reset");
            var slider = AddSlider(row, key, minimum, maximum, path);
            Func<bool> isModified;

            if (path.StartsWith(nameof(_settings.cameraRotation) + ".", StringComparison.Ordinal))
            {
                var axis = "xyz".IndexOf(path[path.Length - 1]);
                _rotationSliders.Add((slider, axis));
                slider.RegisterValueChangedCallback(change =>
                {
                    if (change.target != slider)
                    {
                        return;
                    }

                    var rotation = _window.EditingCamera.rotation;
                    rotation[axis] = change.newValue;
                    _window.SetRotation(rotation);
                });
                isModified = () => _window.EditingCamera.rotation[axis] != defaultValue;
            }
            else
            {
                slider.RegisterValueChangedCallback(change =>
                {
                    if (change.target == slider)
                    {
                        _window.SetPadding(change.newValue);
                    }
                });
                isModified = () => _window.EditingCamera.padding != defaultValue;
            }

            AddResetButton(row, key, reset, isModified, $"reset-{path}");
            root.Add(row);

            return slider;
        }

        private void AddResetButton(
            VisualElement row,
            string key,
            Action reset,
            Func<bool> isModified,
            string name = null
        )
        {
            var button = new Button(reset)
            {
                name = name ?? $"reset-{key.ToLowerInvariant()}",
                text = _localization.Text("buttons.Reset"),
                tooltip = _localization.Text($"tooltips.Reset{key}"),
            };
            row.Add(button);
            _resetButtons.Add((button, isModified));
        }

        private void BuildOutput(VisualElement root)
        {
            var section = AddSection(root, "Icon");
            var image = AddSettingsGroup(section, "output-image");
            var iconResolutions = IconGeneratorSettings.IconResolutions.ToList();
            var iconResolutionField = new PopupField<int>(
                _localization.Text("labels.IconResolution")
            )
            {
                choices = iconResolutions,
                tooltip = _localization.Text("tooltips.IconResolution"),
                formatSelectedValueCallback = FormatResolution,
                formatListItemCallback = FormatResolution,
            };
            iconResolutionField.SetValueWithoutNotify(
                iconResolutions.Contains(_settings.resolution)
                    ? _settings.resolution
                    : IconGeneratorSettings.DefaultResolution
            );
            iconResolutionField.RegisterValueChangedCallback(change =>
                _window.SetResolution(change.newValue)
            );
            image.Add(iconResolutionField);
            var generation = AddSettingsGroup(section, "output-generation");
            var formatGenerationMode = FormatOption<IconGenerationMode>(
                _localization,
                "GenerationMode"
            );
            var generationModeField = new PopupField<IconGenerationMode>(
                _localization.Text("labels.GenerationMode"),
                new List<IconGenerationMode>
                {
                    IconGenerationMode.Both,
                    IconGenerationMode.Combined,
                    IconGenerationMode.Individual,
                },
                _settings.generationMode,
                formatGenerationMode,
                formatGenerationMode
            )
            {
                name = "generation-mode",
                tooltip = _localization.Text("tooltips.GenerationMode"),
            };
            generationModeField.RegisterValueChangedCallback(change =>
                _window.SetGenerationMode(change.newValue)
            );
            generation.Add(generationModeField);
            var formatSaveScope = FormatOption<IconSaveScope>(_localization, "SaveScope");
            _saveScopeField = new PopupField<IconSaveScope>(
                _localization.Text("labels.SaveScope"),
                new List<IconSaveScope>
                {
                    IconSaveScope.AllItems,
                    IconSaveScope.CheckedItems,
                    IconSaveScope.SelectedItems,
                },
                _settings.saveScope,
                formatSaveScope,
                formatSaveScope
            )
            {
                name = "save-scope",
                tooltip = _localization.Text("tooltips.SaveScope"),
            };
            _saveScopeField.RegisterValueChangedCallback(change =>
                _window.SetSaveScope(change.newValue)
            );
            generation.Add(_saveScopeField);
            var appearance = AddSettingsGroup(section, "output-appearance");
            var backgroundRow = new VisualElement { name = "background-row" };
            backgroundRow.AddToClassList("color-toggle-row");
            _backgroundColorField = new ColorField(_localization.Text("labels.BackgroundColor"))
            {
                name = "background-color",
                bindingPath = nameof(_settings.backgroundColor),
                showAlpha = false,
                hdr = false,
                tooltip = _localization.Text("tooltips.BackgroundColor"),
            };
            _backgroundColorField.AddToClassList("settings-field");
            backgroundRow.Add(_backgroundColorField);
            var transparent = new Toggle
            {
                name = "transparent-background",
                text = _localization.Text("labels.TransparentBackground"),
                bindingPath = nameof(_settings.transparentBackground),
                tooltip = _localization.Text("tooltips.TransparentBackground"),
            };
            backgroundRow.Add(transparent);
            appearance.Add(backgroundRow);
            var outlineRow = new VisualElement { name = "outline-row" };
            outlineRow.AddToClassList("color-toggle-row");
            _outlineColorField = new ColorField(_localization.Text("labels.OutlineColor"))
            {
                name = "outline-color",
                bindingPath = nameof(_settings.outlineColor),
                showAlpha = false,
                hdr = false,
                tooltip = _localization.Text("tooltips.OutlineColor"),
            };
            _outlineColorField.AddToClassList("settings-field");
            outlineRow.Add(_outlineColorField);
            outlineRow.Add(
                new Toggle
                {
                    name = "outline-enabled",
                    text = _localization.Text("labels.Outline"),
                    bindingPath = nameof(_settings.outline),
                    tooltip = _localization.Text("tooltips.Outline"),
                }
            );
            appearance.Add(outlineRow);
            _outlineOptions = new VisualElement { name = "outline-options" };
            _outlineOptions.Add(
                new SliderInt(
                    _localization.Text("labels.OutlineWidth"),
                    IconGeneratorSettings.MinOutlineWidth,
                    IconGeneratorSettings.MaxOutlineWidth
                )
                {
                    bindingPath = nameof(_settings.outlineWidth),
                    showInputField = true,
                }
            );

            foreach (var field in _outlineOptions.Children())
            {
                field.AddToClassList("settings-field");
            }

            appearance.Add(_outlineOptions);
            var saveSettings = new Foldout
            {
                name = "save-settings",
                text = _localization.Text("labels.SaveSettings"),
                value = _window.SaveSettingsExpanded,
            };
            saveSettings.AddToClassList("settings-group");
            saveSettings.RegisterValueChangedCallback(change =>
                _window.SaveSettingsExpanded = change.newValue
            );
            section.Add(saveSettings);
            var file = AddSettingsGroup(saveSettings, "output-file");
            var fileName = new TextField(_localization.Text("labels.FileName"))
            {
                bindingPath = nameof(_settings.fileName),
                isDelayed = true,
                tooltip = _localization.Text("tooltips.FileName"),
            };
            fileName.RegisterCallback<TooltipEvent>(
                evt =>
                {
                    var sources = ResolvedCaptureSelection.Normalize(
                        _window.UsesSelectedSaveScope
                            ? _window.SelectedSourceObjects
                            : _selectionStore.Sources.Select(source => source.Target)
                    );
                    evt.tooltip = fileName.tooltip;

                    if (sources.Count > 0)
                    {
                        var target =
                            _settings.generationMode == IconGenerationMode.Combined
                            || (
                                _settings.generationMode == IconGenerationMode.Both
                                && _window.CombinedPreviewPrimary
                            )
                                ? null
                                : _window.PreviewTarget;
                        var expanded = IconFileNameTemplate.Expand(
                            fileName.value,
                            sources,
                            _settings.resolution,
                            DateTime.Now,
                            _settings.CameraFor(target).rotation,
                            target
                        );
                        var example = $"{IconFileOutput.SanitizeName(expanded)}.png";
                        evt.tooltip =
                            $"{_localization.Text("tooltips.FileNameExample", example)}\n\n{evt.tooltip}";
                    }

                    var warnings = IconFileNameTemplate
                        .FindUnknownTokens(fileName.value)
                        .Select(item =>
                        {
                            var warning = _localization.Text(
                                "tooltips.UnknownFileNameToken",
                                item.Token
                            );

                            return item.Suggestion == null
                                ? warning
                                : $"{warning}\n{_localization.Text("tooltips.FileNameTokenSuggestion", item.Suggestion)}";
                        });
                    var notice = string.Join("\n", warnings);

                    if (!string.IsNullOrEmpty(notice))
                    {
                        evt.tooltip = $"{notice}\n\n{evt.tooltip}";
                    }

                    evt.rect = fileName.worldBound;
                    evt.StopImmediatePropagation();
                },
                TrickleDown.TrickleDown
            );
            file.Add(fileName);
            string FormatFileConflict(ExistingFileAction action) =>
                _localization.Text(
                    action switch
                    {
                        ExistingFileAction.Overwrite => "options.FileConflictOverwrite",
                        ExistingFileAction.Ask => "options.FileConflictConfirmOverwrite",
                        ExistingFileAction.AddNumber => "options.FileConflictAddNumber",
                        ExistingFileAction.Skip => "options.FileConflictSkip",
                        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
                    }
                );
            var fileConflictField = new PopupField<ExistingFileAction>(
                _localization.Text("labels.FileConflictHandling"),
                EnumChoices<ExistingFileAction>(),
                _settings.existingFileAction,
                FormatFileConflict,
                FormatFileConflict
            );
            fileConflictField.RegisterValueChangedCallback(change =>
                _window.SetExistingFileAction(change.newValue)
            );
            file.Add(fileConflictField);

            if (MenuIconLinker.IsAvailable)
            {
                var formatMenuIconLink = FormatOption<MenuIconLinkMode>(
                    _localization,
                    "MenuIconLink"
                );
                var menuIconLink = new PopupField<MenuIconLinkMode>(
                    _localization.Text("labels.MenuIconLink"),
                    EnumChoices<MenuIconLinkMode>(),
                    _settings.menuIconLinkMode,
                    formatMenuIconLink,
                    formatMenuIconLink
                )
                {
                    name = "menu-icon-link",
                    tooltip = _localization.Text("tooltips.MenuIconLink"),
                };
                menuIconLink.RegisterValueChangedCallback(change =>
                    _window.SetMenuIconLinkMode(change.newValue)
                );
                file.Add(menuIconLink);
            }

            var destination = AddSettingsGroup(saveSettings, "output-directory");
            var outputDirectoryField = new TextField(_localization.Text("labels.SaveFolder"))
            {
                bindingPath = nameof(_settings.outputDirectory),
                isDelayed = true,
                tooltip = _localization.Text("tooltips.SaveFolder"),
            };
            destination.Add(outputDirectoryField);
            var folderButtons = new VisualElement();
            folderButtons.AddToClassList("button-row");
            folderButtons.Add(
                new Button(_window.BrowseOutput)
                {
                    text = _localization.Text("buttons.BrowseSaveFolder"),
                }
            );
            _openFolderButton = new Button(_window.OpenOutput)
            {
                name = "show-save-folder",
                text = _localization.Text("buttons.ShowSaveFolderInProject"),
            };
            var saveHeading = new VisualElement();
            saveHeading.AddToClassList("save-settings-heading");
            saveHeading.Add(saveSettings.Q<Toggle>());
            saveHeading.Add(_openFolderButton);
            saveSettings.hierarchy.Insert(0, saveHeading);
            destination.Add(folderButtons);
        }

        internal void RefreshSourceTree(CaptureSelectionTree tree = null)
        {
            tree ??= CaptureSelectionTree.Build(_selectionStore);
            _sourceTree.Refresh(tree);
            _blendShapeWeightModeField.SetEnabled(
                tree.Roots.SelectMany(root => root.DescendantsAndSelf())
                    .SelectMany(node => node.Controls)
                    .Any(target => target != null && CaptureHierarchy.HasBlendShapes(target))
            );
        }

        internal void RefreshSettings()
        {
            RefreshPreviewOptions();
            _blendShapeWeightModeField.SetValueWithoutNotify(_settings.blendShapeWeightMode);
            _sourceTree.ShowBlendShapeWeights =
                _settings.blendShapeWeightMode == BlendShapeWeightMode.SelectedMeshes;
            _saveScopeField.SetValueWithoutNotify(_settings.saveScope);
            _backgroundColorField.SetEnabled(!_settings.transparentBackground);
            _outlineColorField.SetEnabled(_settings.outline);
            _outlineOptions.style.display = _settings.outline
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (
                !_outputFolderStateInitialized
                || _cachedOutputDirectory != _settings.outputDirectory
            )
            {
                RefreshOutputFolder();
            }
        }

        internal void RefreshCamera()
        {
            if (_useItemCameraToggle == null)
            {
                return;
            }

            var target = _window.ItemCameraTarget;
            _useItemCameraToggle.SetEnabled(target != null);
            _useItemCameraToggle.SetValueWithoutNotify(_window.UsesItemCamera(target));
            var camera = _window.EditingCamera;

            foreach (var (slider, axis) in _rotationSliders)
            {
                slider.SetValueWithoutNotify(camera.rotation[axis]);
            }

            var preset = IconGeneratorSettings.GetCameraPresetName(camera.rotation);

            foreach (var (name, button) in _cameraPresetButtons)
            {
                button.EnableInClassList("selected", name == preset);
            }

            _horizontalPositionField.SetValueWithoutNotify(-camera.offset.x * 100);
            _verticalPositionField.SetValueWithoutNotify(-camera.offset.y * 100);
            var maximumZoom = camera.SliderZoomMaximum;

            if (_zoomSlider.highValue != maximumZoom)
            {
                _zoomSlider.SetValueWithoutNotify(_zoomSlider.lowValue);
                _zoomSlider.highValue = maximumZoom;
            }

            _zoomSlider.SetValueWithoutNotify(camera.zoom);
            _zoomField.SetValueWithoutNotify(camera.zoom);
            _paddingSlider.SetValueWithoutNotify(camera.padding);
            _keepWholeObjectToggle.SetValueWithoutNotify(camera.keepWholeObject);
            _useVisibleAreaToggle.SetValueWithoutNotify(camera.useVisibleArea);
            _useVisibleAreaToggle.SetEnabled(camera.keepWholeObject);

            foreach (var (button, isModified) in _resetButtons)
            {
                button.SetEnabled(isModified());
            }
        }

        internal void RefreshPreviewOptions()
        {
            var target = _window.PreviewTarget;
            var sourceNames = string.Join(
                ", ",
                (
                    _window.UsesSelectedSaveScope
                        ? ResolvedCaptureSelection.Normalize(_window.SelectedSourceObjects)
                        : _selectionStore
                            .Sources.Where(source =>
                                source.Target != null
                                && !ResolvedCaptureSelection.IsEditorOnly(source.Target.transform)
                                && !_selectionStore.Excluded.Any(excluded =>
                                    excluded.Target == source.Target
                                )
                            )
                            .Select(source => source.Target)
                ).Select(source => source.name)
            );
            var combinedName = string.IsNullOrEmpty(sourceNames)
                ? _localization.Text("options.GenerationModeCombined")
                : _localization.Text("labels.CombinedPreviewName", sourceNames);
            _previewTargetLabel.text = target != null ? target.name : combinedName;
            _combinedPreviewLabel.text = combinedName;
            _combinedPreviewLabel.tooltip = sourceNames;
            _previewTargetLabel.tooltip =
                target != null ? SelectionReference.HierarchyPath(target.transform) : sourceNames;
            _previewOutputSizeToggle.SetValueWithoutNotify(_window.PreviewAtOutputSize);
            _previewResolutionField.SetEnabled(!_window.PreviewAtOutputSize);
            UpdatePreviewLayout();
            RefreshCamera();
            _sourceTree.SyncPreviewSelection(target);
        }

        internal void RefreshOutputFolder()
        {
            _cachedOutputDirectory = _settings.outputDirectory;
            _outputFolderStateInitialized = true;
            var valid = false;
            var hint = "tooltips.SaveFolderMissing";

            try
            {
                var directory = IconFileOutput.ResolveDirectoryPath(_settings.outputDirectory);
                valid = IconFileOutput.GetProjectFolderPath(directory) != null;

                if (valid)
                {
                    hint = "tooltips.ShowSaveFolderInProject";
                }
                else if (Directory.Exists(directory))
                {
                    hint = "tooltips.SaveFolderNotInProject";
                }
            }
            catch (CaptureValidationException)
            {
                hint = "messages.InvalidSaveFolder";
            }

            _openFolderButton.SetEnabled(valid);
            _openFolderButton.tooltip = _localization.Text(hint);
        }

        internal void SetStatus(string message, bool error)
        {
            _generateButton.SetEnabled(!error);
            _generateButton.tooltip = message ?? "";
            _generateButton.parent.tooltip = _generateButton.tooltip;
        }

        private sealed class TwoDecimalSlider : Slider
        {
            private readonly TextField _input;
            private bool _editing;

            internal TwoDecimalSlider(string label, float minimum, float maximum)
                : base(label, minimum, maximum)
            {
                showInputField = true;
                _input = this.Q<TextField>();
                _input.RegisterCallback<FocusInEvent>(_ => _editing = true);
                _input.RegisterCallback<FocusOutEvent>(_ =>
                {
                    _editing = false;
                    FormatValue();
                });
                FormatValue();
            }

            public override void SetValueWithoutNotify(float newValue)
            {
                base.SetValueWithoutNotify(newValue);
                FormatValue();
            }

            private void FormatValue()
            {
                if (_input != null && !_editing)
                {
                    _input.SetValueWithoutNotify(
                        value.ToString(DecimalFormat, CultureInfo.InvariantCulture)
                    );
                }
            }
        }
    }
}
