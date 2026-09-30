using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KOZ39.IconGenerator
{
    internal sealed class IconPreview : VisualElement
    {
        private const float PrecisionSpeed = 0.2f;
        private readonly Image _image;
        private readonly VisualElement _grid;
        private bool _showGrid;
        private bool _showAtOutputSize;
        private int _activePointerId = -1;
        private int _dragButton;
        private Vector2 _lastPosition;
        private bool _firstClickHadCameraControls;
        private bool _cameraControlsEnabled = true;

        internal bool CameraControlsEnabled
        {
            get => _cameraControlsEnabled;
            set
            {
                _cameraControlsEnabled = value;

                if (!value)
                {
                    EndDrag(_activePointerId);
                }
            }
        }

        internal event Action InteractionStarted;
        internal event Action InteractionEnded;
        internal event Action<Vector2> OrbitDragged;
        internal event Action<Vector2> PanDragged;
        internal event Action<float> ZoomScrolled;
        internal event Action FrameRequested;

        internal bool ShowGrid
        {
            get => _showGrid;
            set
            {
                if (_showGrid == value)
                {
                    return;
                }

                _showGrid = value;
                _grid.MarkDirtyRepaint();
            }
        }

        internal Texture Texture
        {
            set
            {
                _image.image = value;
                UpdateImageLayout();
            }
        }

        internal bool ShowAtOutputSize
        {
            get => _showAtOutputSize;
            set
            {
                if (_showAtOutputSize == value)
                {
                    return;
                }

                _showAtOutputSize = value;
                UpdateImageLayout();
            }
        }

        internal IconPreview()
        {
            focusable = true;
            AddToClassList("icon-preview");
            generateVisualContent += DrawBackground;
            _image = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            _image.style.position = Position.Absolute;
            Add(_image);
            _grid = new VisualElement { pickingMode = PickingMode.Ignore };
            _grid.StretchToParentSize();
            _grid.generateVisualContent += DrawGrid;
            Add(_grid);
            RegisterCallback<GeometryChangedEvent>(_ => UpdateImageLayout());
            RegisterCallback<PointerDownEvent>(PointerDown);
            RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                var frame =
                    evt.clickCount == 2
                    && _firstClickHadCameraControls
                    && CameraControlsEnabled
                    && _image.image != null
                    && _activePointerId < 0;
                _firstClickHadCameraControls = evt.clickCount == 1 && CameraControlsEnabled;

                if (!frame)
                {
                    return;
                }

                FrameRequested?.Invoke();
                evt.StopPropagation();
                evt.PreventDefault();
            });
            RegisterCallback<PointerMoveEvent>(PointerMove);
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == _dragButton)
                {
                    EndDrag(evt.pointerId);
                }
            });
            RegisterCallback<PointerCancelEvent>(evt => EndDrag(evt.pointerId));
            RegisterCallback<PointerCaptureOutEvent>(evt => EndDrag(evt.pointerId));
            RegisterCallback<DetachFromPanelEvent>(_ => EndDrag(_activePointerId));
            RegisterCallback<WheelEvent>(evt =>
            {
                if (!CameraControlsEnabled || _image.image == null)
                {
                    return;
                }

                ZoomScrolled?.Invoke(evt.delta.y * (evt.shiftKey ? PrecisionSpeed : 1));
                evt.StopPropagation();
                evt.PreventDefault();
            });
        }

        private void PointerDown(PointerDownEvent evt)
        {
            if (evt.button >= 0 && evt.button <= 2)
            {
                Focus();
            }

            if (
                !CameraControlsEnabled
                || _image.image == null
                || _activePointerId >= 0
                || evt.button < 1
                || evt.button > 2
            )
            {
                return;
            }

            _activePointerId = evt.pointerId;
            _dragButton = evt.button;
            _lastPosition = evt.position;
            this.CapturePointer(_activePointerId);
            InteractionStarted?.Invoke();
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void PointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _activePointerId)
            {
                return;
            }

            if ((evt.pressedButtons & (1 << _dragButton)) == 0)
            {
                EndDrag(evt.pointerId);
                return;
            }

            var position = (Vector2)evt.position;
            var delta = (position - _lastPosition) * (evt.shiftKey ? PrecisionSpeed : 1);
            _lastPosition = position;

            if (_dragButton == 2)
            {
                PanDragged?.Invoke(delta / Mathf.Max(1, GetImageRect().width));
            }
            else
            {
                OrbitDragged?.Invoke(delta);
            }

            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void EndDrag(int pointerId)
        {
            if (_activePointerId < 0 || pointerId != _activePointerId)
            {
                return;
            }

            _activePointerId = -1;

            if (this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }

            InteractionEnded?.Invoke();
        }

        internal Rect GetImageRect()
        {
            var pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            var side =
                ShowAtOutputSize && _image.image != null
                    ? _image.image.width / pixelsPerPoint
                    : Mathf.Min(contentRect.width, contentRect.height);

            return new Rect(
                Mathf.Round((contentRect.width - side) * pixelsPerPoint / 2) / pixelsPerPoint,
                Mathf.Round((contentRect.height - side) * pixelsPerPoint / 2) / pixelsPerPoint,
                side,
                side
            );
        }

        private void UpdateImageLayout()
        {
            var rectangle = GetImageRect();
            _image.style.left = rectangle.x;
            _image.style.top = rectangle.y;
            _image.style.width = rectangle.width;
            _image.style.height = rectangle.height;
            MarkDirtyRepaint();
            _grid.MarkDirtyRepaint();
        }

        private void DrawBackground(MeshGenerationContext context)
        {
            var rectangle = GetImageRect();

            if (rectangle.width <= 0)
            {
                return;
            }

            var painter = context.painter2D;
            const int count = 16;
            var size = rectangle.width / count;

            for (var y = 0; y < count; y++)
            {
                for (var x = 0; x < count; x++)
                {
                    painter.fillColor =
                        ((x + y) & 1) == 0
                            ? new Color(0.25f, 0.25f, 0.25f)
                            : new Color(0.31f, 0.31f, 0.31f);
                    var position = rectangle.position + new Vector2(x * size, y * size);
                    painter.BeginPath();
                    painter.MoveTo(position);
                    painter.LineTo(position + new Vector2(size, 0));
                    painter.LineTo(position + new Vector2(size, size));
                    painter.LineTo(position + new Vector2(0, size));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        private void DrawGrid(MeshGenerationContext context)
        {
            if (!_showGrid)
            {
                return;
            }

            var rectangle = GetImageRect();
            var painter = context.painter2D;
            painter.lineWidth = 1;
            painter.strokeColor = new Color(1, 1, 1, 0.38f);

            for (var i = 1; i <= 2; i++)
            {
                var distance = rectangle.width * i / 3;
                painter.BeginPath();
                painter.MoveTo(new Vector2(rectangle.x + distance, rectangle.y));
                painter.LineTo(new Vector2(rectangle.x + distance, rectangle.yMax));
                painter.MoveTo(new Vector2(rectangle.x, rectangle.y + distance));
                painter.LineTo(new Vector2(rectangle.xMax, rectangle.y + distance));
                painter.Stroke();
            }
        }
    }
}
