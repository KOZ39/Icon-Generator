using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal enum SourceListFilter
    {
        All,
        Mesh,
        ModularAvatar,
        Unchecked,
        ItemCamera,
    }

    internal sealed class SourceTreeView : VisualElement
    {
        private const int MinVisibleSourceTreeRows = 3;
        private readonly TreeView _tree;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _resizeHandle;
        private readonly VisualElement _stickyRoot;
        private readonly SourceTreeRow _stickyRootRow;
        private readonly Toggle _stickyRootFoldout;
        private readonly IconGeneratorWindow _window;
        private readonly Label _noSearchResults;
        private readonly HashSet<int> _knownSourceIds = new();
        private IReadOnlyList<CaptureSelectionNode> _roots = Array.Empty<CaptureSelectionNode>();
        private bool _showBlendShapeWeights;
        private bool _rebuilding;
        private bool _spaceHeld;
        private int _selectionAnchorId;
        private float _navigationScrollOffset;
        private float _preferredTreeHeight;
        private float _resizeStartHeight;
        private float _resizeStartY;
        private int _resizePointerId = -1;

        internal bool ShowBlendShapeWeights
        {
            get => _showBlendShapeWeights;
            set
            {
                if (_showBlendShapeWeights == value)
                {
                    return;
                }

                _showBlendShapeWeights = value;
                RefreshRows();
            }
        }

        internal SourceTreeView(
            IconGeneratorWindow window,
            IconGeneratorLocalization localization,
            CaptureSelectionStore selectionStore
        )
        {
            name = "source-list";
            _window = window;
            _preferredTreeHeight = window.SourceTreeHeight;
            AddToClassList("selection-editor");
            _tree = new TreeView
            {
                viewDataKey = "source-tree",
                fixedItemHeight = EditorGUIUtility.singleLineHeight + 4,
                reorderable = false,
                selectionType = SelectionType.Multiple,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            _tree.AddToClassList("selection-list");
            SourceTreeRow CreateRow() =>
                new(window, localization, selectionStore, SelectItem, GetContextTargets);

            _tree.makeItem = CreateRow;
            _tree.bindItem = (element, index) =>
            {
                ((SourceTreeRow)element).Bind(
                    _tree.GetItemDataForIndex<CaptureSelectionNode>(index),
                    ShowBlendShapeWeights
                );
                UpdateBranchGuides(element, index);
            };
            _tree.unbindItem = (element, _) => ((SourceTreeRow)element).Bind(null, false);
            _scrollView = _tree.Q<ScrollView>();
            _stickyRoot = new VisualElement { name = "source-sticky-root" };
            _stickyRoot.AddToClassList(BaseTreeView.itemUssClassName);
            _stickyRoot.style.height = _tree.fixedItemHeight;
            _stickyRoot.style.display = DisplayStyle.None;
            _stickyRootFoldout = new Toggle { name = "sticky-root-foldout" };
            _stickyRootFoldout.AddToClassList(Foldout.toggleUssClassName);
            _stickyRootFoldout.AddToClassList(BaseTreeView.itemToggleUssClassName);
            _stickyRootFoldout
                .Q(className: Toggle.inputUssClassName)
                .AddToClassList(Foldout.inputUssClassName);
            _stickyRootFoldout
                .Q(className: Toggle.checkmarkUssClassName)
                .AddToClassList(Foldout.checkmarkUssClassName);
            _stickyRootFoldout.RegisterValueChangedCallback(change =>
            {
                if (!change.newValue && _stickyRootRow.userData is GameObject target)
                {
                    _tree.CollapseItem(target.GetInstanceID());
                    _tree.ScrollToItemById(target.GetInstanceID());
                    UpdateSourceTreeHeight();
                }
            });
            _stickyRootRow = CreateRow();
            _stickyRoot.Add(_stickyRootFoldout);
            _stickyRoot.Add(_stickyRootRow);
            _scrollView.contentViewport.Add(_stickyRoot);
            _scrollView.verticalScroller.valueChanged += _ => UpdateStickyRoot();
            _tree.selectionChanged += items =>
            {
                if (_rebuilding)
                {
                    return;
                }

                var nodes = items.OfType<CaptureSelectionNode>().ToArray();
                RememberSelection(nodes);
                PreviewItem(nodes.LastOrDefault());
                UpdateStickyRoot();
            };
            _tree.itemsSourceChanged += UpdateSourceTreeHeight;
            _scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
                UpdateSourceTreeHeight()
            );
            _scrollView.contentContainer.RegisterCallback<NavigationMoveEvent>(
                evt =>
                {
                    _navigationScrollOffset = _scrollView.scrollOffset.y;
                    NavigateParentOrChild(evt);
                },
                TrickleDown.TrickleDown
            );
            _tree.RegisterCallback<AttachToPanelEvent>(_ =>
                schedule.Execute(() =>
                {
                    _scrollView.contentContainer.UnregisterCallback<NavigationMoveEvent>(
                        FinishNavigation
                    );
                    _scrollView.contentContainer.RegisterCallback<NavigationMoveEvent>(
                        FinishNavigation
                    );
                })
            );
            _scrollView.contentContainer.RegisterCallback<KeyDownEvent>(
                ToggleSelectedItems,
                TrickleDown.TrickleDown
            );
            RegisterCallback<KeyUpEvent>(
                evt =>
                {
                    if (evt.keyCode == KeyCode.Space)
                    {
                        _spaceHeld = false;
                    }
                },
                TrickleDown.TrickleDown
            );
            RegisterCallback<BlurEvent>(_ => _spaceHeld = false, TrickleDown.TrickleDown);
            RegisterCallback<DetachFromPanelEvent>(_ => _spaceHeld = false);
            var sourceInput = new ObjectField
            {
                name = "add-source-row",
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = localization.Text("tooltips.AddSource"),
            };
            sourceInput.RegisterValueChangedCallback(change =>
            {
                sourceInput.SetValueWithoutNotify(null);
                window.AddSource(change.newValue as GameObject);
            });
            Add(sourceInput);
            var search = new ToolbarSearchField
            {
                name = "source-search",
                tooltip = localization.Text("tooltips.SearchSources"),
            };
            search.SetValueWithoutNotify(window.SourceSearch);
            search.RegisterValueChangedCallback(change =>
            {
                window.SourceSearch = change.newValue;
                RebuildTree();
            });
            var searchRow = new VisualElement { name = "source-search-row" };
            searchRow.Add(search);
            var formatFilter = IconGeneratorView.FormatOption<SourceListFilter>(
                localization,
                "SourceFilter"
            );
            var filter = new PopupField<SourceListFilter>(
                IconGeneratorView.EnumChoices<SourceListFilter>(),
                (int)window.SourceFilter,
                formatFilter,
                formatFilter
            )
            {
                name = "source-filter",
                tooltip = localization.Text("tooltips.SourceFilter"),
            };
            filter.RegisterValueChangedCallback(change =>
            {
                window.SourceFilter = change.newValue;
                RebuildTree();
            });
            searchRow.Add(filter);
            Add(searchRow);
            Add(_tree);
            _resizeHandle = new VisualElement { name = "source-list-resize" };
            _resizeHandle.Add(new VisualElement { pickingMode = PickingMode.Ignore });
            _resizeHandle.RegisterCallback<PointerDownEvent>(BeginResize);
            _resizeHandle.RegisterCallback<PointerMoveEvent>(Resize);
            _resizeHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0 && evt.pointerId == _resizePointerId)
                {
                    EndResize(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            _resizeHandle.RegisterCallback<PointerCaptureOutEvent>(evt => EndResize(evt.pointerId));
            _resizeHandle.RegisterCallback<DetachFromPanelEvent>(_ => EndResize(_resizePointerId));
            Add(_resizeHandle);
            _noSearchResults = new Label(localization.Text("messages.NoSearchResults"))
            {
                name = "source-search-empty",
            };
            _noSearchResults.style.display = DisplayStyle.None;
            Add(_noSearchResults);
            RegisterCallback<DragUpdatedEvent>(
                evt =>
                {
                    var objects = GetDraggedObjects();

                    if (!ShouldHandleDrop(evt.target as VisualElement, sourceInput, objects.Length))
                    {
                        return;
                    }

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.StopPropagation();
                    evt.PreventDefault();
                },
                TrickleDown.TrickleDown
            );
            RegisterCallback<DragPerformEvent>(
                evt =>
                {
                    var objects = GetDraggedObjects();

                    if (!ShouldHandleDrop(evt.target as VisualElement, sourceInput, objects.Length))
                    {
                        return;
                    }

                    DragAndDrop.AcceptDrag();
                    window.AddSources(objects);
                    evt.StopPropagation();
                    evt.PreventDefault();
                },
                TrickleDown.TrickleDown
            );
        }

        private static GameObject[] GetDraggedObjects() =>
            DragAndDrop
                .objectReferences.Select(item =>
                    item is Component component ? component.gameObject : item as GameObject
                )
                .Where(item => item != null)
                .Distinct()
                .ToArray();

        private static bool ShouldHandleDrop(
            VisualElement target,
            ObjectField sourceInput,
            int count
        )
        {
            if (count == 0)
            {
                return false;
            }

            var field = target as ObjectField ?? target?.GetFirstAncestorOfType<ObjectField>();

            return count > 1 || field == null || field == sourceInput;
        }

        internal void Refresh(CaptureSelectionTree tree)
        {
            _roots = tree.Roots;
            RebuildTree();
        }

        internal void SyncPreviewSelection(GameObject target)
        {
            if (_window.UsesSelectedSaveScope)
            {
                return;
            }

            if (
                _tree
                    .selectedItems.OfType<CaptureSelectionNode>()
                    .Any(node => target == null ? node.Source != null : node.Target == target)
            )
            {
                return;
            }

            var index =
                target != null ? _tree.viewController.GetIndexForId(target.GetInstanceID()) : -1;
            _tree.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
            RememberSelection(_tree.selectedItems.OfType<CaptureSelectionNode>());
            UpdateStickyRoot();
        }

        private void RememberSelection(IEnumerable<CaptureSelectionNode> nodes) =>
            _window.SetSelectedSourceIds(nodes.Select(node => node.Id));

        private GameObject[] GetContextTargets(GameObject contextTarget)
        {
            var selected = _tree
                .selectedItems.OfType<CaptureSelectionNode>()
                .Select(node => node.Target)
                .ToArray();

            if (selected.Contains(contextTarget))
            {
                return selected;
            }

            var index = _tree.viewController.GetIndexForId(contextTarget.GetInstanceID());

            if (index >= 0)
            {
                _tree.SetSelection(index);
            }

            return new[] { contextTarget };
        }

        private void SelectItem(CaptureSelectionNode node, ClickEvent evt)
        {
            if (node?.Target == null)
            {
                return;
            }

            var index = _tree.viewController.GetIndexForId(node.Id);

            if (index < 0)
            {
                return;
            }

            var additive = evt.ctrlKey || evt.commandKey;
            var anchor = _tree.viewController.GetIndexForId(_selectionAnchorId);

            if (evt.shiftKey && anchor >= 0)
            {
                var range = Enumerable.Range(Math.Min(index, anchor), Math.Abs(index - anchor) + 1);
                _tree.SetSelection(
                    additive ? _tree.selectedIndices.Union(range).ToArray() : range.ToArray()
                );
            }
            else
            {
                _selectionAnchorId = node.Id;

                if (!additive)
                {
                    _tree.SetSelection(index);
                }
                else if (_tree.selectedIndices.Contains(index))
                {
                    _tree.RemoveFromSelection(index);
                }
                else
                {
                    _tree.AddToSelection(index);
                }
            }

            if (_tree.selectedIndices.Contains(index))
            {
                PreviewItem(node);
            }

            _scrollView.contentContainer.Focus();
        }

        private void PreviewItem(CaptureSelectionNode node)
        {
            if (node?.Target != null)
            {
                _window.SetPreviewTarget(
                    node.Source != null && !_window.UsesItemCamera(node.Target) ? null : node.Target
                );
            }
            else if (_window.UsesSelectedSaveScope)
            {
                _window.SetPreviewTarget(null);
            }
        }

        private void ToggleSelectedItems(KeyDownEvent evt)
        {
            if (
                evt.keyCode != KeyCode.Space
                || evt.altKey
                || evt.ctrlKey
                || evt.commandKey
                || evt.shiftKey
                || evt.target != evt.currentTarget
            )
            {
                return;
            }

            evt.StopImmediatePropagation();
            evt.PreventDefault();

            if (_spaceHeld)
            {
                return;
            }

            _spaceHeld = true;
            var nodes = _tree
                .selectedItems.OfType<CaptureSelectionNode>()
                .Where(node => node.Target != null && !node.InheritedExclusion)
                .ToArray();

            if (nodes.Length == 0)
            {
                return;
            }

            _window.SetObjectsIncluded(
                nodes.SelectMany(node => node.Controls),
                nodes.Any(node => !node.Included)
            );
        }

        private void NavigateParentOrChild(NavigationMoveEvent evt)
        {
            var index = _tree.selectedIndex;

            if (index < 0 || evt.altKey || evt.ctrlKey || evt.commandKey || evt.shiftKey)
            {
                return;
            }

            var controller = _tree.viewController;
            var expanded =
                controller.HasChildrenByIndex(index) && controller.IsExpandedByIndex(index);
            int next;

            if (evt.direction == NavigationMoveEvent.Direction.Right)
            {
                if (!expanded)
                {
                    return;
                }

                next = index + 1;
            }
            else if (evt.direction == NavigationMoveEvent.Direction.Left)
            {
                if (expanded)
                {
                    return;
                }

                next = controller.GetIndexForId(_tree.GetParentIdForIndex(index));
            }
            else
            {
                return;
            }

            if (next >= 0 && next < controller.GetItemsCount())
            {
                _tree.SetSelection(next);
                KeepSelectionVisible(_navigationScrollOffset);
            }

            evt.StopImmediatePropagation();
            evt.PreventDefault();
        }

        private void RebuildTree()
        {
            var query = _window.SourceSearch.Trim();
            var searching = query.Length > 0 || _window.SourceFilter != SourceListFilter.All;
            var selectedIds = _window.SelectedSourceIds.ToArray();
            _rebuilding = true;

            if (searching && _window.SourceExpansionBeforeSearch == null)
            {
                _window.SourceExpansionBeforeSearch =
                    _tree.viewController == null
                        ? _roots.Select(node => node.Id).ToHashSet()
                        : EnumerateNodes(_roots)
                            .Where(node => _tree.IsExpanded(node.Id))
                            .Select(node => node.Id)
                            .ToHashSet();
            }

            var roots = searching
                ? FilterNodes(
                    _roots,
                    query,
                    _window.SourceFilter,
                    _window.SourceFilter == SourceListFilter.ModularAvatar
                        ? CaptureHierarchy.FindSourceRenderers(_roots.Select(root => root.Target))
                        : null
                )
                : _roots.Select(ToTreeItem).ToList();
            _tree.SetRootItems(roots);

            foreach (var root in roots)
            {
                if (_knownSourceIds.Add(root.id))
                {
                    _tree.ExpandItem(root.id);
                }
            }

            _knownSourceIds.IntersectWith(_roots.Select(root => root.Id));

            if (searching)
            {
                _tree.ExpandAll();
            }
            else if (_window.SourceExpansionBeforeSearch != null)
            {
                _tree.CollapseAll();

                foreach (var node in EnumerateNodes(_roots))
                {
                    if (_window.SourceExpansionBeforeSearch.Contains(node.Id))
                    {
                        _tree.ExpandItem(node.Id);
                    }
                }

                _window.SourceExpansionBeforeSearch = null;
            }

            RefreshRows();
            _tree.SetSelectionWithoutNotify(
                selectedIds
                    .Select(id => _tree.viewController.GetIndexForId(id))
                    .Where(index => index >= 0)
                    .ToArray()
            );
            var selected = _tree.selectedItems.OfType<CaptureSelectionNode>().ToArray();
            RememberSelection(selected);
            _rebuilding = false;

            if (
                _window.UsesSelectedSaveScope
                && !selected.Any(node =>
                    _window.PreviewTarget == null
                        ? node.Source != null
                        : node.Target == _window.PreviewTarget
                )
            )
            {
                PreviewItem(selected.LastOrDefault());
            }

            _noSearchResults.style.display =
                searching && roots.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateSourceTreeHeight();
        }

        private void RefreshRows()
        {
            _tree.EnableInClassList(
                "root-blend-shape-controls",
                _showBlendShapeWeights
                    && _roots.Any(root =>
                        root.MeshTarget != null && CaptureHierarchy.HasBlendShapes(root.MeshTarget)
                    )
            );
            _tree.RefreshItems();
            UpdateStickyRoot(true);
        }

        private void UpdateStickyRoot(bool refresh = false)
        {
            var controller = _tree.viewController;
            var count = controller.GetItemsCount();
            var height = _tree.fixedItemHeight;
            var offset = _scrollView.scrollOffset.y;
            var index = Mathf.Clamp(Mathf.FloorToInt(offset / height), 0, count - 1);
            var rootId = count > 0 ? _tree.GetIdForIndex(index) : -1;

            while (rootId != -1 && controller.GetParentId(rootId) != -1)
            {
                rootId = controller.GetParentId(rootId);
            }

            var rootIndex = rootId != -1 ? controller.GetIndexForId(rootId) : -1;
            var visible = rootIndex >= 0 && offset > rootIndex * height;
            _stickyRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible)
            {
                if (_stickyRootRow.userData != null)
                {
                    _stickyRootRow.Bind(null, false);
                }

                return;
            }

            var node = _tree.GetItemDataForIndex<CaptureSelectionNode>(rootIndex);

            if (refresh || !ReferenceEquals(_stickyRootRow.userData, node.Target))
            {
                _stickyRootRow.Bind(node, ShowBlendShapeWeights);
            }

            var nextRootIndex = count;

            foreach (var id in controller.GetRootItemIds())
            {
                var next = controller.GetIndexForId(id);

                if (next > rootIndex && next < nextRootIndex)
                {
                    nextRootIndex = next;
                }
            }

            _stickyRoot.style.top = Mathf.Min(0, nextRootIndex * height - offset - height);
            _stickyRoot.EnableInClassList(
                BaseVerticalCollectionView.itemSelectedVariantUssClassName,
                _tree.selectedIndices.Contains(rootIndex)
            );
            _stickyRootFoldout.SetValueWithoutNotify(_tree.IsExpanded(rootId));
        }

        private void FinishNavigation(NavigationMoveEvent evt) =>
            KeepSelectionVisible(_navigationScrollOffset);

        private void KeepSelectionVisible(float offset)
        {
            var index = _tree.selectedIndex;

            if (index < 0)
            {
                return;
            }

            var height = _tree.fixedItemHeight;
            var itemTop = index * height;
            var topInset = _tree.GetParentIdForIndex(index) != -1 ? height : 0;
            _scrollView.scrollOffset = new Vector2(
                _scrollView.scrollOffset.x,
                Mathf.Clamp(
                    offset,
                    Mathf.Max(0, itemTop + height - _scrollView.contentViewport.layout.height),
                    Mathf.Max(0, itemTop - topInset)
                )
            );
        }

        private void UpdateBranchGuides(VisualElement row, int index)
        {
            var indent = row.parent.parent.Q(BaseTreeView.itemIndentUssClassName);
            var guides = indent.Q<BranchGuides>();

            if (guides == null)
            {
                guides = new BranchGuides();
                indent.Add(guides);
            }

            guides.Bind(row, _tree, index);
        }

        private sealed class BranchGuides : VisualElement
        {
            private readonly List<bool> _continuations = new();
            private readonly List<VisualElement> _verticals = new();
            private readonly VisualElement _horizontal = new() { pickingMode = PickingMode.Ignore };
            private VisualElement _leafRow;

            internal BranchGuides()
            {
                name = "source-branch-guides";
                pickingMode = PickingMode.Ignore;
                _horizontal.AddToClassList("source-branch-horizontal");
                _horizontal.RegisterCallback<GeometryChangedEvent>(_ => UpdateLeafLength());
                Add(_horizontal);
            }

            internal void Bind(VisualElement row, TreeView tree, int index)
            {
                _continuations.Clear();
                var controller = tree.viewController;
                var id = tree.GetIdForIndex(index);

                for (
                    var ancestor = controller.GetParentId(id);
                    ancestor != -1;
                    ancestor = controller.GetParentId(id)
                )
                {
                    _continuations.Add(controller.GetChildrenIds(ancestor).Last() != id);
                    id = ancestor;
                }

                _continuations.Reverse();

                while (_verticals.Count < _continuations.Count)
                {
                    var line = new VisualElement { pickingMode = PickingMode.Ignore };
                    line.AddToClassList("source-branch-vertical");
                    _verticals.Add(line);
                    Add(line);
                }

                for (var level = 0; level < _verticals.Count; level++)
                {
                    var current = level == _continuations.Count - 1;
                    var visible =
                        level < _continuations.Count && (current || _continuations[level]);
                    var line = _verticals[level];
                    line.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                    if (!visible)
                    {
                        continue;
                    }

                    var left = Length.Percent(100f * (level + 0.5f) / _continuations.Count);
                    line.style.left = left;
                    line.style.bottom = Length.Percent(_continuations[level] ? 0 : 50);

                    if (current)
                    {
                        _horizontal.style.left = left;
                    }
                }

                _horizontal.style.display =
                    _continuations.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _leafRow = controller.HasChildrenByIndex(index) ? null : row;
                UpdateLeafLength();
            }

            private void UpdateLeafLength()
            {
                _horizontal.style.right =
                    _leafRow?.parent != null && parent != null
                        ? parent.layout.xMax - _leafRow.parent.layout.xMin
                        : 0;
            }
        }

        private static IEnumerable<CaptureSelectionNode> EnumerateNodes(
            IEnumerable<CaptureSelectionNode> nodes
        ) => nodes.SelectMany(node => node.DescendantsAndSelf());

        private List<TreeViewItemData<CaptureSelectionNode>> FilterNodes(
            IEnumerable<CaptureSelectionNode> nodes,
            string query,
            SourceListFilter filter,
            IReadOnlyCollection<Renderer> captureRenderers
        )
        {
            var matches = new List<TreeViewItemData<CaptureSelectionNode>>();

            foreach (var node in nodes)
            {
                var name = node.Target != null ? node.Target.name : node.Source?.Label ?? "";
                var nameMatches = name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                if (nameMatches && filter == SourceListFilter.All)
                {
                    matches.Add(ToTreeItem(node));
                    continue;
                }

                var children = FilterNodes(
                    node.Children,
                    nameMatches ? "" : query,
                    filter,
                    captureRenderers
                );
                var matchesFilter =
                    node.Target != null
                    && (
                        filter switch
                        {
                            SourceListFilter.Mesh => node.MeshTarget != null,
                            SourceListFilter.ModularAvatar => !captureRenderers.Any(renderer =>
                                renderer.transform.IsChildOf(node.Target.transform)
                            )
                                && node.Controls.Any(control =>
                                    control != null
                                    && NdmfCaptureBridge.HasSourceControl(
                                        control,
                                        false,
                                        captureRenderers
                                    )
                                ),
                            SourceListFilter.Unchecked => !node.Included || node.InheritedExclusion,
                            SourceListFilter.ItemCamera => _window.UsesItemCamera(node.Target),
                            _ => true,
                        }
                    );

                if ((nameMatches && matchesFilter) || children.Count > 0)
                {
                    matches.Add(
                        new TreeViewItemData<CaptureSelectionNode>(node.Id, node, children)
                    );
                }
            }

            return matches;
        }

        private static TreeViewItemData<CaptureSelectionNode> ToTreeItem(
            CaptureSelectionNode node
        ) => new(node.Id, node, node.Children.Select(ToTreeItem).ToList());

        private void BeginResize(PointerDownEvent evt)
        {
            if (evt.button != 0 || _resizePointerId >= 0)
            {
                return;
            }

            _resizePointerId = evt.pointerId;
            _resizeStartY = evt.position.y;
            _resizeStartHeight = _tree.resolvedStyle.height;
            _resizeHandle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void Resize(PointerMoveEvent evt)
        {
            if (evt.pointerId != _resizePointerId)
            {
                return;
            }

            if ((evt.pressedButtons & 1) == 0)
            {
                EndResize(evt.pointerId);
                return;
            }

            _preferredTreeHeight = Mathf.Clamp(
                _resizeStartHeight + evt.position.y - _resizeStartY,
                MinVisibleSourceTreeRows * _tree.fixedItemHeight,
                _tree.viewController.GetItemsCount() * _tree.fixedItemHeight
            );
            UpdateSourceTreeHeight();
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void EndResize(int pointerId)
        {
            if (_resizePointerId < 0 || pointerId != _resizePointerId)
            {
                return;
            }

            _resizePointerId = -1;

            if (!Mathf.Approximately(_window.SourceTreeHeight, _preferredTreeHeight))
            {
                _window.SourceTreeHeight = _preferredTreeHeight;
            }

            if (_resizeHandle.HasPointerCapture(pointerId))
            {
                _resizeHandle.ReleasePointer(pointerId);
            }
        }

        private void UpdateSourceTreeHeight()
        {
            var count = _tree.viewController.GetItemsCount();
            var height = Mathf.Min(
                count * _tree.fixedItemHeight,
                Mathf.Max(MinVisibleSourceTreeRows * _tree.fixedItemHeight, _preferredTreeHeight)
            );

            if (_tree.style.height.value.value != height)
            {
                _tree.style.height = height;
            }

            _tree.EnableInClassList("empty-list", count == 0);
            _resizeHandle.style.display =
                count > MinVisibleSourceTreeRows ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateStickyRoot();
        }

        private sealed class SourceTreeRow : VisualElement
        {
            private CaptureSelectionNode _node;
            private readonly ObjectField _sourceField;
            private readonly Toggle _includedToggle;
            private readonly Toggle _ignoreBlendShapeToggle;
            private readonly Label _nameLabel;
            private readonly Image _itemCameraIndicator;
            private readonly Button _removeButton;
            private readonly IconGeneratorLocalization _localization;
            private readonly CaptureSelectionStore _selectionStore;
            private readonly IconGeneratorWindow _window;

            internal SourceTreeRow(
                IconGeneratorWindow window,
                IconGeneratorLocalization localization,
                CaptureSelectionStore selectionStore,
                Action<CaptureSelectionNode, ClickEvent> selectItem,
                Func<GameObject, GameObject[]> getContextTargets
            )
            {
                _localization = localization;
                _window = window;
                _selectionStore = selectionStore;
                AddToClassList("selection-row");
                _includedToggle = new Toggle { name = "include-object" };
                _includedToggle.RegisterValueChangedCallback(change =>
                {
                    if (_node?.Target != null)
                    {
                        _window.SetObjectsIncluded(_node.Controls, change.newValue);
                    }
                });
                Add(_includedToggle);
                _nameLabel = new Label();
                _nameLabel.AddToClassList("selection-name");
                IconGeneratorView.RegisterElementTooltip(
                    this,
                    _nameLabel,
                    () => _window.GetCaptureTargetTooltip(_node?.Target, true)
                );
                RegisterSelectOnClick(_nameLabel, selectItem);
                Add(_nameLabel);
                _sourceField = new ObjectField
                {
                    objectType = typeof(GameObject),
                    allowSceneObjects = true,
                };
                _sourceField.AddToClassList("selection-content");
                var sourceName = _sourceField.Q(className: ObjectField.objectUssClassName);
                RegisterSelectOnClick(sourceName, selectItem);
                _sourceField.RegisterValueChangedCallback(change =>
                {
                    if (_node?.Source != null)
                    {
                        _window.ReplaceSource(_node.Source, change.newValue as GameObject);
                    }
                });
                Add(_sourceField);
                _itemCameraIndicator = new Image
                {
                    image = EditorGUIUtility.ObjectContent(null, typeof(Camera)).image,
                    tooltip = _localization.Text("tooltips.ItemCameraIndicator"),
                };
                _itemCameraIndicator.AddToClassList("item-camera-indicator");
                _removeButton = new Button(() =>
                {
                    if (_node?.Source != null)
                    {
                        _window.RemoveSource(_node.Source);
                    }
                })
                {
                    text = "×",
                    tooltip = _localization.Text("tooltips.RemoveSource"),
                };
                Add(_itemCameraIndicator);
                _ignoreBlendShapeToggle = new Toggle
                {
                    name = "ignore-mesh-blend-shape-weights",
                    tooltip = _localization.Text("tooltips.IgnoreMeshBlendShapeWeights"),
                };
                _ignoreBlendShapeToggle.RegisterValueChangedCallback(change =>
                {
                    if (_node?.Target != null)
                    {
                        _window.SetBlendShapeWeightsIgnored(_node.Target, change.newValue);
                    }
                });
                var actions = new VisualElement();
                actions.AddToClassList("selection-row-actions");
                actions.Add(_ignoreBlendShapeToggle);
                actions.Add(_removeButton);
                Add(actions);
                this.AddManipulator(
                    new ContextualMenuManipulator(evt =>
                    {
                        var target = _node?.Target;

                        if (target == null)
                        {
                            return;
                        }

                        var targets = getContextTargets(target);
                        var multiple = targets.Length > 1;
                        evt.menu.AppendAction(
                            _localization.Text(
                                multiple ? "menus.SaveSelectedItems" : "menus.SaveItem"
                            ),
                            _ => _window.GenerateTargets(targets),
                            targets.Any(_window.CanCaptureTarget)
                                ? DropdownMenuAction.Status.Normal
                                : DropdownMenuAction.Status.Disabled
                        );
                        evt.menu.AppendSeparator();
                        evt.menu.AppendAction(
                            _localization.Text("menus.CopyItemCamera"),
                            _ => _window.CopyItemCamera(target),
                            multiple
                                ? DropdownMenuAction.Status.Disabled
                                : DropdownMenuAction.Status.Normal
                        );
                        evt.menu.AppendAction(
                            _localization.Text("menus.PasteItemCamera"),
                            _ => _window.PasteItemCamera(targets),
                            _window.HasCopiedItemCamera
                                ? DropdownMenuAction.Status.Normal
                                : DropdownMenuAction.Status.Disabled
                        );
                        evt.menu.AppendAction(
                            _localization.Text("menus.UseSharedCamera"),
                            _ => _window.UseSharedCamera(targets),
                            targets.Any(_window.UsesItemCamera)
                                ? DropdownMenuAction.Status.Normal
                                : DropdownMenuAction.Status.Disabled
                        );
                        evt.menu.AppendSeparator();
                        evt.menu.AppendAction(
                            _localization.Text(
                                EditorUtility.IsPersistent(target)
                                    ? "menus.FindInProject"
                                    : "menus.FindInScene"
                            ),
                            _ => SelectOriginal(target)
                        );

                        foreach (var component in _window.GetRelatedComponents(targets))
                        {
                            evt.menu.AppendAction(
                                $"{_localization.Text("menus.RelatedMaComponents")}/{RelatedComponentLabel(component)}",
                                _ => SelectOriginal(component)
                            );
                        }

                        evt.menu.AppendSeparator();
                        void SetIncluded(bool? included)
                        {
                            if (multiple)
                            {
                                _window.SetSelectedObjectsIncluded(targets, included);
                            }
                            else
                            {
                                _window.SetBranchIncluded(target, included);
                            }
                        }

                        evt.menu.AppendAction(
                            _localization.Text(
                                multiple ? "menus.IncludeSelectedItems" : "menus.IncludeBranch"
                            ),
                            _ => SetIncluded(true)
                        );
                        evt.menu.AppendAction(
                            _localization.Text(
                                multiple ? "menus.ExcludeSelectedItems" : "menus.ExcludeBranch"
                            ),
                            _ => SetIncluded(false)
                        );
                        evt.menu.AppendAction(
                            _localization.Text(
                                multiple
                                    ? "menus.RestoreSelectedItemsSceneState"
                                    : "menus.RestoreBranchSceneState"
                            ),
                            _ => SetIncluded(null)
                        );

                        if (
                            multiple
                            && _window.UsesSelectedMeshWeights
                            && targets.Any(_window.CanIgnoreBlendShapeWeights)
                        )
                        {
                            evt.menu.AppendSeparator();
                            evt.menu.AppendAction(
                                _localization.Text("menus.IgnoreSelectedBlendShapeWeights"),
                                _ => _window.SetBlendShapeWeightsIgnored(targets, true)
                            );
                            evt.menu.AppendAction(
                                _localization.Text("menus.RestoreSelectedBlendShapeWeights"),
                                _ => _window.SetBlendShapeWeightsIgnored(targets, false)
                            );
                        }

                        evt.StopPropagation();
                    })
                );
            }

            private void RegisterSelectOnClick(
                VisualElement element,
                Action<CaptureSelectionNode, ClickEvent> selectItem
            )
            {
                element.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                element.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
                element.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.button == 0 && _node?.Target != null)
                    {
                        selectItem(_node, evt);
                    }
                });
            }

            private static string RelatedComponentLabel(Component component)
            {
                var type = component.GetType();
                var name = ObjectNames
                    .NicifyVariableName(type.Name)
                    .Replace("Modular Avatar", "MA");
                var siblings = component.GetComponents(type);

                if (siblings.Length > 1)
                {
                    name += $" ({Array.IndexOf(siblings, component) + 1})";
                }

                var path = SelectionReference
                    .HierarchyPath(component.transform)
                    .Replace("/", " › ");

                return $"{path} ({name})";
            }

            private static void SelectOriginal(Object target)
            {
                if (target == null)
                {
                    return;
                }

                Selection.activeObject = target;

                if (EditorUtility.IsPersistent(target))
                {
                    EditorUtility.FocusProjectWindow();
                }
                else
                {
                    EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
                }

                EditorGUIUtility.PingObject(target);
            }

            internal void Bind(CaptureSelectionNode node, bool showBlendShapeWeights)
            {
                _node = node;
                var target = node?.Target;
                userData = target;
                var isSource = node?.Source != null;
                _sourceField.style.display = _removeButton.style.display = isSource
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                _nameLabel.style.display = isSource ? DisplayStyle.None : DisplayStyle.Flex;
                _sourceField.SetValueWithoutNotify(target);
                _sourceField.tooltip = isSource
                    ? $"{node.Source.Label}\n\n{_localization.Text(_window.UsesItemCamera(target) ? "tooltips.PreviewSource" : "tooltips.PreviewSourceRoot")}"
                    : "";
                _includedToggle.SetValueWithoutNotify(node?.Included == true);
                _includedToggle.SetEnabled(target != null && !node.InheritedExclusion);
                _includedToggle.tooltip = _localization.Text(
                    node?.InheritedExclusion == true ? "tooltips.InheritedExclusion"
                    : isSource ? "tooltips.IncludeSource"
                    : "tooltips.IncludeSourceItem"
                );
                _nameLabel.text = target != null ? target.name : "";
                _nameLabel.tooltip =
                    target != null ? SelectionReference.HierarchyPath(target.transform) : "";
                _nameLabel.EnableInClassList("excluded-object", node?.Included != true);
                _itemCameraIndicator.style.display = _window.UsesItemCamera(target)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                var meshTarget = node?.MeshTarget;
                var hasBlendShapes =
                    meshTarget != null && CaptureHierarchy.HasBlendShapes(meshTarget);
                _ignoreBlendShapeToggle.style.display =
                    showBlendShapeWeights && hasBlendShapes ? DisplayStyle.Flex : DisplayStyle.None;
                _ignoreBlendShapeToggle.SetValueWithoutNotify(
                    hasBlendShapes
                        && _selectionStore.IgnoredBlendShapeObjects.Any(item =>
                            item.Target == meshTarget
                        )
                );
            }
        }
    }
}
