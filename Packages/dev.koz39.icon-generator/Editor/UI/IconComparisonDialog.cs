using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KOZ39.IconGenerator
{
    internal abstract class IconComparisonDialog<T> : EditorWindow
    {
        private const float IconSize = 32;
        private const float RowHeight = IconSize + 8;
        private IReadOnlyList<T> _items;
        private bool[] _selected;
        private List<int> _visibleIndices;
        private Button _selectAllButton;
        private Button _clearAllButton;
        private Label _selectionCount;
        private Label _noSearchResults;
        private ListView _list;
        protected IconGeneratorLocalization Localization { get; private set; }
        internal IReadOnlyList<T> SelectedItems { get; private set; }

        protected abstract string ElementPrefix { get; }
        protected abstract string ListName { get; }
        protected abstract string TitleKey { get; }
        protected abstract string MessageKey { get; }
        protected virtual bool ElideNameAtStart => false;
        protected abstract bool IsInitiallySelected(T item);
        protected abstract Texture CurrentIcon(T item);
        protected abstract Texture NewIcon(T item);
        protected abstract string Name(T item);
        protected abstract string Detail(T item);
        protected abstract IEnumerable<string> SearchTexts(T item);

        protected static TDialog ShowDialog<TDialog>(
            IReadOnlyList<T> items,
            IconGeneratorLocalization localization
        )
            where TDialog : IconComparisonDialog<T>
        {
            var parent = focusedWindow;
            var dialog = CreateInstance<TDialog>();
            dialog.Initialize(items, localization);
            var bounds = EditorGUIUtility.GetMainWindowPosition();
            var size = new Vector2(
                Mathf.Min(620, bounds.width - 40),
                Mathf.Min(Mathf.Clamp(200 + items.Count * RowHeight, 280, 520), bounds.height - 80)
            );
            var origin = (parent != null ? parent.position.center : bounds.center) - size / 2;
            dialog.position = new Rect(
                Mathf.Clamp(origin.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x)),
                Mathf.Clamp(origin.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - size.y)),
                size.x,
                size.y
            );
            dialog.ShowModalUtility();

            return dialog;
        }

        internal void Initialize(IReadOnlyList<T> items, IconGeneratorLocalization localization)
        {
            _items = items;
            _selected = items.Select(IsInitiallySelected).ToArray();
            _visibleIndices = Enumerable.Range(0, items.Count).ToList();
            Localization = localization;
            SelectedItems = null;
            titleContent = new GUIContent(localization.Text(TitleKey));
            minSize = new Vector2(460, 280);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();

            if (_items == null || Localization == null)
            {
                root.schedule.Execute(Close);
                return;
            }

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{IconGeneratorPackageInfo.AssetPath}/Editor/UI/IconComparisonDialog.uss"
            );

            if (style != null && !root.styleSheets.Contains(style))
            {
                root.styleSheets.Add(style);
            }

            root.AddToClassList("comparison-dialog");
            var message = new Label(Localization.Text(MessageKey, _items.Count));
            message.AddToClassList("comparison-message");
            root.Add(message);
            var search = new ToolbarSearchField { name = $"{ElementPrefix}-search" };
            search.AddToClassList("comparison-search");
            search.RegisterValueChangedCallback(change => Filter(change.newValue));
            root.Add(search);
            var selection = new VisualElement();
            selection.AddToClassList("comparison-selection");
            root.Add(selection);
            _selectAllButton = new Button(() => SetVisibleSelection(true))
            {
                name = $"{ElementPrefix}-select-all",
            };
            _clearAllButton = new Button(() => SetVisibleSelection(false))
            {
                name = $"{ElementPrefix}-clear-all",
            };
            selection.Add(_selectAllButton);
            selection.Add(_clearAllButton);
            _selectionCount = new Label { name = $"{ElementPrefix}-selection-count" };
            _selectionCount.AddToClassList("comparison-selection-count");
            selection.Add(_selectionCount);
            _list = new ListView
            {
                name = ListName,
                fixedItemHeight = RowHeight,
                selectionType = SelectionType.None,
                makeItem = CreateRow,
                bindItem = (element, index) => BindRow(element, _visibleIndices[index]),
                unbindItem = (element, _) => element.userData = null,
            };
            _list.AddToClassList("comparison-list");
            root.Add(_list);
            var scroll = _list.Q<ScrollView>();
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            _noSearchResults = new Label(Localization.Text("messages.NoSearchResults"))
            {
                name = $"{ElementPrefix}-search-empty",
            };
            _noSearchResults.AddToClassList("comparison-search-empty");
            root.Insert(root.IndexOf(_list), _noSearchResults);
            var actions = new VisualElement();
            actions.AddToClassList("comparison-actions");
            root.Add(actions);
            actions.Add(
                new Button(() =>
                {
                    SelectedItems = _items.Where((_, index) => _selected[index]).ToArray();
                    Close();
                })
                {
                    name = $"{ElementPrefix}-apply",
                    text = Localization.Text("buttons.Confirm"),
                }
            );
            var cancel = new Button(Close)
            {
                name = $"{ElementPrefix}-cancel",
                text = Localization.Text("buttons.Cancel"),
            };
            actions.Add(cancel);
            Refresh();
            root.schedule.Execute(cancel.Focus);
            root.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode != KeyCode.Escape)
                    {
                        return;
                    }

                    evt.StopImmediatePropagation();
                    evt.PreventDefault();

                    if (
                        !string.IsNullOrEmpty(search.value)
                        && (
                            root.focusController.focusedElement == search
                            || search.Contains(root.focusController.focusedElement as VisualElement)
                        )
                    )
                    {
                        search.value = "";
                    }
                    else
                    {
                        Close();
                    }
                },
                TrickleDown.TrickleDown
            );
        }

        private void RefreshSelectionControls()
        {
            var selectedCount = _selected.Count(selected => selected);
            var visibleSelectedCount = _visibleIndices.Count(index => _selected[index]);
            var isFiltered = _visibleIndices.Count < _items.Count;
            _selectAllButton.text = Localization.Text(
                isFiltered ? "buttons.SelectMatches" : "buttons.SelectAll"
            );
            _clearAllButton.text = Localization.Text(
                isFiltered ? "buttons.DeselectMatches" : "buttons.DeselectAll"
            );
            _selectAllButton.SetEnabled(visibleSelectedCount < _visibleIndices.Count);
            _clearAllButton.SetEnabled(visibleSelectedCount > 0);
            _selectionCount.text = isFiltered
                ? Localization.Text(
                    "labels.FilteredSelectionCount",
                    visibleSelectedCount,
                    _visibleIndices.Count,
                    selectedCount - visibleSelectedCount
                )
                : Localization.Text("labels.SelectionCount", selectedCount, _items.Count);
        }

        private void SetVisibleSelection(bool selected)
        {
            foreach (var index in _visibleIndices)
            {
                _selected[index] = selected;
            }

            _list.RefreshItems();
            RefreshSelectionControls();
        }

        private void Filter(string query)
        {
            query = query.Trim();
            _visibleIndices = Enumerable
                .Range(0, _items.Count)
                .Where(index =>
                    query.Length == 0
                    || SearchTexts(_items[index])
                        .Any(text =>
                            text != null
                            && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        )
                )
                .ToList();
            Refresh();
            _list.Q<ScrollView>().scrollOffset = Vector2.zero;
        }

        private void Refresh()
        {
            _list.itemsSource = _visibleIndices;
            _list.Rebuild();
            _noSearchResults.style.display =
                _visibleIndices.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshSelectionControls();
        }

        private VisualElement CreateRow()
        {
            var row = new VisualElement();
            row.AddToClassList("comparison-row");
            var toggle = new Toggle { name = $"{ElementPrefix}-toggle" };
            toggle.RegisterValueChangedCallback(change =>
            {
                if (row.userData is int index)
                {
                    _selected[index] = change.newValue;
                    RefreshSelectionControls();
                }
            });
            row.Add(toggle);
            row.Add(CreateIcon($"{ElementPrefix}-current"));
            var arrow = new Label("→");
            arrow.AddToClassList("comparison-arrow");
            row.Add(arrow);
            row.Add(CreateIcon($"{ElementPrefix}-new"));
            var text = new VisualElement();
            text.AddToClassList("comparison-text");
            var name = new Label { name = $"{ElementPrefix}-name" };
            name.AddToClassList("comparison-name");
            name.EnableInClassList("elide-start", ElideNameAtStart);
            name.displayTooltipWhenElided = false;
            text.Add(name);
            var detail = new Label { name = $"{ElementPrefix}-detail" };
            detail.AddToClassList("comparison-detail");
            detail.displayTooltipWhenElided = false;
            text.Add(detail);
            row.Add(text);
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement target && toggle.Contains(target))
                {
                    return;
                }

                toggle.value = !toggle.value;
            });

            return row;
        }

        private static Image CreateIcon(string name)
        {
            var image = new Image { name = name, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = image.style.height = IconSize;
            image.AddToClassList("comparison-icon");

            return image;
        }

        private void BindRow(VisualElement row, int index)
        {
            var item = _items[index];
            row.userData = index;
            row.Q<Toggle>($"{ElementPrefix}-toggle").SetValueWithoutNotify(_selected[index]);
            row.Q<Image>($"{ElementPrefix}-current").image = CurrentIcon(item);
            row.Q<Image>($"{ElementPrefix}-new").image = NewIcon(item);
            row.Q<Label>($"{ElementPrefix}-name").text = Name(item);
            row.Q<Label>($"{ElementPrefix}-detail").text = Detail(item);
        }
    }
}
