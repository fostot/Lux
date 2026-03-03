using System;
using Monofont;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.UI;
using TerrariaModder.Core.UI.Widgets;
using Color4 = TerrariaModder.Core.UI.Color4;

namespace Lux.UI.Widgets
{
    /// <summary>
    /// Top-level draggable panel container. Handles drag, header, close button,
    /// panel bounds registration, z-order blocking, escape-to-close, optional
    /// edge/corner resize, and catch-all click consumption.
    ///
    /// Usage: BeginDraw() → draw your content → EndDraw()
    /// </summary>
    public class DraggablePanel
    {
        private int _panelX = -1;
        private int _panelY = -1;
        private bool _isOpen;
        private bool _isDragging;
        private int _dragOffsetX;
        private int _dragOffsetY;
        private bool _blockInput;
        private bool _drawRegistered;

        // ── Resize state ───────────────────────────────────────────────
        private bool _isResizing;
        private ResizeEdge _resizeEdge;
        private int _resizeStartMouseX;
        private int _resizeStartMouseY;
        private int _resizeStartW;
        private int _resizeStartH;
        private int _resizeStartPanelX;
        private int _resizeStartPanelY;

        private const int Grip = 6; // edge hit-zone width in pixels

        private enum ResizeEdge
        {
            None,
            Left, Right, Top, Bottom,
            TopLeft, TopRight, BottomLeft, BottomRight
        }

        /// <summary>
        /// Create a new draggable panel.
        /// </summary>
        /// <param name="panelId">Unique ID for z-order and bounds registration.</param>
        /// <param name="title">Title displayed in the header.</param>
        /// <param name="width">Panel width in pixels.</param>
        /// <param name="height">Panel height in pixels.</param>
        public DraggablePanel(string panelId, string title, int width, int height)
        {
            PanelId = panelId;
            Title = title;
            Width = width;
            Height = height;
        }

        // -- Properties --

        public string PanelId { get; }
        public string Title { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int X => _panelX;
        public int Y => _panelY;
        public bool IsOpen => _isOpen;
        public bool BlockInput => _blockInput;

        /// <summary>Y coordinate where content starts (below header).</summary>
        public int ContentX => _panelX + Padding;

        /// <summary>Y coordinate where content starts (below header).</summary>
        public int ContentY => _panelY + HeaderHeight;

        /// <summary>Available width for content (panel width minus padding on both sides).</summary>
        public int ContentWidth => Width - Padding * 2;

        /// <summary>Available height for content (below header, minus bottom padding).</summary>
        public int ContentHeight => Height - HeaderHeight - Padding;

        // -- Configuration --

        public int HeaderHeight { get; set; } = 35;
        public int Padding { get; set; } = 8;
        public bool ShowCloseButton { get; set; } = true;
        public bool Draggable { get; set; } = true;
        public bool CloseOnEscape { get; set; } = true;
        public Action OnClose { get; set; }

        /// <summary>
        /// Optional icon texture (Texture2D via reflection) displayed in the header.
        /// Load via UIRenderer.LoadTexture() or set from ModInfo.IconTexture.
        /// </summary>
        public object IconTexture { get; set; }

        /// <summary>
        /// Whether to show an icon in the header. Default true.
        /// Set to false for lightweight panels that don't need icons.
        /// </summary>
        public bool ShowIcon { get; set; } = true;

        /// <summary>
        /// Optional delegate that resolves an icon texture (Texture2D) by panel ID.
        /// When ShowIcon is true and IconTexture is null, this delegate is called
        /// to resolve the icon. Return null to skip icon rendering.
        /// </summary>
        public Func<string, object> IconResolver { get; set; }

        /// <summary>
        /// Whether BeginDraw/EndDraw should clip the content area.
        /// Default true. Set to false when the panel manages its own clip regions
        /// (e.g., panels with tab bars, toolbars, and footers outside the scroll area).
        /// </summary>
        public bool ClipContent { get; set; } = true;

        /// <summary>
        /// Enable edge/corner resize. Default false (backward compatible).
        /// When true, users can drag panel edges and corners to resize.
        /// </summary>
        public bool Resizable { get; set; } = false;

        /// <summary>
        /// Use MonoFont for the panel title and close button text.
        /// Default false. Set to true when the panel uses MonoFont for all text.
        /// </summary>
        public bool UseMonoFont { get; set; } = false;

        /// <summary>Minimum panel width when resizing.</summary>
        public int MinWidth { get; set; } = 200;

        /// <summary>Minimum panel height when resizing.</summary>
        public int MinHeight { get; set; } = 150;

        // -- Lifecycle --

        /// <summary>
        /// Open the panel centered on screen.
        /// </summary>
        public void Open()
        {
            _panelX = -1;
            _panelY = -1;
            _isOpen = true;
            RegisterDraw();
        }

        /// <summary>
        /// Open the panel at a specific position.
        /// </summary>
        public void Open(int x, int y)
        {
            _panelX = x;
            _panelY = y;
            _isOpen = true;
            RegisterDraw();
        }

        /// <summary>
        /// Close the panel and unregister bounds.
        /// </summary>
        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            _isDragging = false;
            _isResizing = false;
            UIRenderer.UnregisterPanelBounds(PanelId);
            OnClose?.Invoke();
        }

        /// <summary>
        /// Toggle open/close.
        /// </summary>
        public void Toggle()
        {
            if (_isOpen)
                Close();
            else
                Open();
        }

        /// <summary>
        /// Register the panel's draw callback with UIRenderer.
        /// Call this once during mod initialization, passing your draw method.
        /// </summary>
        public void RegisterDrawCallback(Action drawCallback)
        {
            UIRenderer.RegisterPanelDraw(PanelId, drawCallback);
            _drawRegistered = true;
        }

        /// <summary>
        /// Unregister the panel's draw callback.
        /// </summary>
        public void UnregisterDrawCallback()
        {
            UIRenderer.UnregisterPanelDraw(PanelId);
            _drawRegistered = false;
        }

        // -- Draw Frame --

        /// <summary>
        /// Call at the start of your Draw callback. Returns false if panel is closed
        /// (skip all drawing). Handles escape, dragging, resize, header, close button, bounds.
        /// Sets WidgetInput.BlockInput for child widgets.
        /// </summary>
        public bool BeginDraw()
        {
            if (!_isOpen) return false;

            // Escape to close (only when not blocked by higher panel and not in text input)
            if (CloseOnEscape && !UIRenderer.IsWaitingForKeyInput
                && InputState.IsKeyJustPressed(KeyCode.Escape))
            {
                Close();
                return false;
            }

            // Set block flag for this frame
            _blockInput = UIRenderer.ShouldBlockForHigherPriorityPanel(PanelId);
            WidgetInput.BlockInput = _blockInput;

            // Default position: centered
            if (_panelX < 0) _panelX = (UIRenderer.ScreenWidth - Width) / 2;
            if (_panelY < 0) _panelY = (UIRenderer.ScreenHeight - Height) / 2;

            // Resize (must be checked BEFORE dragging — corner/edge takes priority)
            if (Resizable)
                HandleResize();

            // Dragging (only when not resizing)
            if (Draggable && !_isResizing)
                HandleDragging();

            // Clamp to screen
            _panelX = Math.Max(0, Math.Min(_panelX, UIRenderer.ScreenWidth - Width));
            _panelY = Math.Max(0, Math.Min(_panelY, UIRenderer.ScreenHeight - Height));

            // Register bounds for click-through prevention
            UIRenderer.RegisterPanelBounds(PanelId, _panelX, _panelY, Width, Height);

            // Draw panel background
            UIRenderer.DrawPanel(_panelX, _panelY, Width, Height, UIColors.PanelBg);

            // Draw resize handles (visual indicators at corners)
            if (Resizable)
                DrawResizeHandles();

            // Draw header
            UIRenderer.DrawRect(_panelX, _panelY, Width, HeaderHeight, UIColors.HeaderBg);

            int titleX = _panelX + 10;

            // Resolve and draw icon (skip entirely when ShowIcon is false)
            if (ShowIcon)
            {
                var icon = IconTexture;
                if (icon == null)
                    icon = IconResolver?.Invoke(PanelId);

                if (icon != null)
                {
                    UIRenderer.DrawTexture(icon, _panelX + 8, _panelY + 6, 22, 22);
                    titleX = _panelX + 34;
                }
            }

            if (UseMonoFont && MonoFont.IsReady)
                { var c = UIColors.TextTitle; MonoFont.DrawText(Title, titleX, _panelY + 9, c.R, c.G, c.B, c.A); }
            else
                UIRenderer.DrawTextShadow(Title, titleX, _panelY + 9, UIColors.TextTitle);

            // Close button
            if (ShowCloseButton)
            {
                int closeX = _panelX + Width - 35;
                int closeY = _panelY + 3;
                bool closeHover = WidgetInput.IsMouseOver(closeX, closeY, 30, 30);
                UIRenderer.DrawRect(closeX, closeY, 30, 30,
                    closeHover ? UIColors.CloseBtnHover : UIColors.CloseBtn);
                if (UseMonoFont && MonoFont.IsReady)
                    { var c = UIColors.Text; MonoFont.DrawText("X", closeX + 11, closeY + 7, c.R, c.G, c.B, c.A); }
                else
                    UIRenderer.DrawText("X", closeX + 11, closeY + 7, UIColors.Text);

                if (closeHover && WidgetInput.MouseLeftClick)
                {
                    WidgetInput.ConsumeClick();
                    Close();
                    return false;
                }
            }

            // Clear tooltip for this frame
            Tooltip.Clear();

            // Clip content area so nothing draws outside the panel
            if (ClipContent)
                UIRenderer.BeginClip(_panelX, _panelY + HeaderHeight, Width, Height - HeaderHeight);

            return true;
        }

        /// <summary>
        /// Call at the end of your Draw callback.
        /// Handles catch-all click consumption and clears BlockInput.
        /// </summary>
        public void EndDraw()
        {
            // End content clipping before drawing tooltip (tooltips can appear anywhere)
            if (ClipContent)
                UIRenderer.EndClip();

            // Draw deferred tooltip
            Tooltip.DrawDeferred();

            // Catch-all: consume any remaining clicks over the panel (+ resize grip zone)
            int expandedGrip = Resizable ? Grip : 0;
            if (!_blockInput && UIRenderer.IsMouseOver(
                    _panelX - expandedGrip, _panelY - expandedGrip,
                    Width + expandedGrip * 2, Height + expandedGrip * 2))
            {
                if (UIRenderer.MouseLeftClick) UIRenderer.ConsumeClick();
                if (UIRenderer.MouseRightClick) UIRenderer.ConsumeRightClick();
                if (UIRenderer.ScrollWheel != 0) UIRenderer.ConsumeScroll();
            }

            WidgetInput.BlockInput = false;
        }

        // -- Internal --

        private void RegisterDraw()
        {
            // Bring to front when opening
            if (_drawRegistered)
                UIRenderer.BringToFront(PanelId);
        }

        private void HandleDragging()
        {
            // Drag handle = header area, excluding close button
            int headerWidth = ShowCloseButton ? Width - 40 : Width;
            bool inHeader = WidgetInput.IsMouseOver(_panelX, _panelY, headerWidth, HeaderHeight);

            if (WidgetInput.MouseLeftClick && inHeader && !_isDragging)
            {
                _isDragging = true;
                _dragOffsetX = WidgetInput.MouseX - _panelX;
                _dragOffsetY = WidgetInput.MouseY - _panelY;
                WidgetInput.ConsumeClick();
            }

            if (_isDragging)
            {
                if (WidgetInput.MouseLeft)
                {
                    _panelX = WidgetInput.MouseX - _dragOffsetX;
                    _panelY = WidgetInput.MouseY - _dragOffsetY;
                }
                else
                {
                    _isDragging = false;
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  RESIZE
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Detect edge/corner hit zones and handle resize dragging.
        /// </summary>
        private void HandleResize()
        {
            int mx = WidgetInput.MouseX;
            int my = WidgetInput.MouseY;

            // While actively resizing, track mouse and apply
            if (_isResizing)
            {
                if (WidgetInput.MouseLeft)
                {
                    int dx = mx - _resizeStartMouseX;
                    int dy = my - _resizeStartMouseY;
                    ApplyResize(dx, dy);
                }
                else
                {
                    _isResizing = false;
                    _resizeEdge = ResizeEdge.None;
                }
                return;
            }

            // Not currently resizing — detect edge/corner hover
            if (_isDragging) return; // don't start resize while dragging

            ResizeEdge edge = DetectEdge(mx, my);

            // Start resize on click
            if (edge != ResizeEdge.None && WidgetInput.MouseLeftClick)
            {
                _isResizing = true;
                _resizeEdge = edge;
                _resizeStartMouseX = mx;
                _resizeStartMouseY = my;
                _resizeStartW = Width;
                _resizeStartH = Height;
                _resizeStartPanelX = _panelX;
                _resizeStartPanelY = _panelY;
                WidgetInput.ConsumeClick();
            }
        }

        /// <summary>
        /// Detect which edge or corner the mouse is hovering over.
        /// Corners (8×8 zones) take priority over edges (6px strips).
        /// </summary>
        private ResizeEdge DetectEdge(int mx, int my)
        {
            int px = _panelX;
            int py = _panelY;
            int pw = Width;
            int ph = Height;

            // Must be within the expanded grip zone around the panel
            bool inHorizontal = mx >= px - Grip && mx <= px + pw + Grip;
            bool inVertical   = my >= py - Grip && my <= py + ph + Grip;
            if (!inHorizontal || !inVertical) return ResizeEdge.None;

            bool onLeft   = mx >= px - Grip && mx <= px + Grip;
            bool onRight  = mx >= px + pw - Grip && mx <= px + pw + Grip;
            bool onTop    = my >= py - Grip && my <= py + Grip;
            bool onBottom = my >= py + ph - Grip && my <= py + ph + Grip;

            // Corners first (8px zones)
            if (onTop && onLeft)     return ResizeEdge.TopLeft;
            if (onTop && onRight)    return ResizeEdge.TopRight;
            if (onBottom && onLeft)  return ResizeEdge.BottomLeft;
            if (onBottom && onRight) return ResizeEdge.BottomRight;

            // Edges (only if mouse is actually within the panel's span)
            if (onLeft   && my > py + Grip && my < py + ph - Grip) return ResizeEdge.Left;
            if (onRight  && my > py + Grip && my < py + ph - Grip) return ResizeEdge.Right;
            if (onTop    && mx > px + Grip && mx < px + pw - Grip) return ResizeEdge.Top;
            if (onBottom && mx > px + Grip && mx < px + pw - Grip) return ResizeEdge.Bottom;

            return ResizeEdge.None;
        }

        /// <summary>
        /// Apply resize delta based on which edge/corner is being dragged.
        /// Left/top edges move the panel position; right/bottom edges change size.
        /// </summary>
        private void ApplyResize(int dx, int dy)
        {
            int newW = _resizeStartW;
            int newH = _resizeStartH;
            int newX = _resizeStartPanelX;
            int newY = _resizeStartPanelY;

            // Horizontal
            switch (_resizeEdge)
            {
                case ResizeEdge.Left:
                case ResizeEdge.TopLeft:
                case ResizeEdge.BottomLeft:
                    newW = _resizeStartW - dx;
                    newX = _resizeStartPanelX + dx;
                    break;
                case ResizeEdge.Right:
                case ResizeEdge.TopRight:
                case ResizeEdge.BottomRight:
                    newW = _resizeStartW + dx;
                    break;
            }

            // Vertical
            switch (_resizeEdge)
            {
                case ResizeEdge.Top:
                case ResizeEdge.TopLeft:
                case ResizeEdge.TopRight:
                    newH = _resizeStartH - dy;
                    newY = _resizeStartPanelY + dy;
                    break;
                case ResizeEdge.Bottom:
                case ResizeEdge.BottomLeft:
                case ResizeEdge.BottomRight:
                    newH = _resizeStartH + dy;
                    break;
            }

            // Enforce minimums (and fix position if clamped on left/top edge)
            if (newW < MinWidth)
            {
                if (newX != _resizeStartPanelX) // dragging left edge
                    newX = _resizeStartPanelX + _resizeStartW - MinWidth;
                newW = MinWidth;
            }
            if (newH < MinHeight)
            {
                if (newY != _resizeStartPanelY) // dragging top edge
                    newY = _resizeStartPanelY + _resizeStartH - MinHeight;
                newH = MinHeight;
            }

            Width = newW;
            Height = newH;
            _panelX = newX;
            _panelY = newY;
        }

        /// <summary>
        /// Draw visual resize indicators at corners and highlighted edges.
        /// </summary>
        private void DrawResizeHandles()
        {
            int px = _panelX;
            int py = _panelY;
            int pw = Width;
            int ph = Height;
            int mx = WidgetInput.MouseX;
            int my = WidgetInput.MouseY;

            // Detect current hover (for highlight)
            ResizeEdge hover = _isResizing ? _resizeEdge : DetectEdge(mx, my);

            // Corner grip triangles (small L-shaped marks at each corner)
            int cs = 12; // corner indicator size
            Color4 dimGrip = UIColors.Border;

            // Bottom-right corner (most common resize)
            Color4 brColor = (hover == ResizeEdge.BottomRight || hover == ResizeEdge.Bottom || hover == ResizeEdge.Right)
                ? UIColors.Accent : dimGrip;
            UIRenderer.DrawRect(px + pw - cs, py + ph - 3, cs, 3, brColor);  // horizontal bar
            UIRenderer.DrawRect(px + pw - 3, py + ph - cs, 3, cs, brColor);  // vertical bar

            // Bottom-left corner
            Color4 blColor = (hover == ResizeEdge.BottomLeft || hover == ResizeEdge.Bottom || hover == ResizeEdge.Left)
                ? UIColors.Accent : dimGrip;
            UIRenderer.DrawRect(px, py + ph - 3, cs, 3, blColor);
            UIRenderer.DrawRect(px, py + ph - cs, 3, cs, blColor);

            // Top-right corner
            Color4 trColor = (hover == ResizeEdge.TopRight || hover == ResizeEdge.Top || hover == ResizeEdge.Right)
                ? UIColors.Accent : dimGrip;
            UIRenderer.DrawRect(px + pw - cs, py, cs, 3, trColor);
            UIRenderer.DrawRect(px + pw - 3, py, 3, cs, trColor);

            // Top-left corner
            Color4 tlColor = (hover == ResizeEdge.TopLeft || hover == ResizeEdge.Top || hover == ResizeEdge.Left)
                ? UIColors.Accent : dimGrip;
            UIRenderer.DrawRect(px, py, cs, 3, tlColor);
            UIRenderer.DrawRect(px, py, 3, cs, tlColor);

            // Edge highlight bars (only when hovering a specific edge, not corner)
            if (hover == ResizeEdge.Top)
                UIRenderer.DrawRect(px + cs, py, pw - cs * 2, 2, UIColors.Accent);
            else if (hover == ResizeEdge.Bottom)
                UIRenderer.DrawRect(px + cs, py + ph - 2, pw - cs * 2, 2, UIColors.Accent);
            else if (hover == ResizeEdge.Left)
                UIRenderer.DrawRect(px, py + cs, 2, ph - cs * 2, UIColors.Accent);
            else if (hover == ResizeEdge.Right)
                UIRenderer.DrawRect(px + pw - 2, py + cs, 2, ph - cs * 2, UIColors.Accent);
        }
    }
}
