using System;
using System.Collections.Generic;
using TerrariaModder.Core.UI;
using TerrariaModder.Core.UI.Widgets;

namespace Lux.UI.Widgets
{
    /// <summary>
    /// Deferred tooltip renderer with word-wrapping, conservative text measurement,
    /// and rounded corners. Call Set() during hover, DrawDeferred() at end of panel draw.
    /// Only the last-set tooltip is drawn (last-writer-wins per frame).
    ///
    /// Configurable properties (set from Framework preferences):
    ///   CornerRadius   — 0-8px rounding on corners (0 = sharp, default 2)
    ///   AutoWrap       — word-wrap text at tooltip edge (default ON)
    ///   AutoGrowWidth  — grow tooltip beyond DefaultMaxWidth to fit content (default OFF)
    ///   DefaultMaxWidth — base max width before auto-grow (default 300)
    /// </summary>
    public static class Tooltip
    {
        private static string _text;
        private static string _title;
        private static bool _hasTooltip;
        private static int _maxWidth;

        // Configurable properties (set from Framework preferences)
        public static int CornerRadius = 2;
        public static bool AutoWrap = true;
        public static bool AutoGrowWidth = false;
        public static int DefaultMaxWidth = 300;

        private const int LineHeight = 16;
        private const int Padding = 8;
        private const int CursorOffset = 16;
        private const int ScreenMargin = 4;

        /// <summary>
        /// Conservative per-character width estimate (pixels).
        /// MeasureText can fall back to 7px which is too optimistic — the actual Terraria
        /// font is closer to 9-10px per character. SafeMeasure uses whichever is larger.
        /// </summary>
        private const int FallbackCharWidth = 10;

        /// <summary>
        /// Optional callback invoked when Clear() is called.
        /// Wire up externally to integrate with game-specific tooltip systems
        /// (e.g., Lux.UI.Widgets.Tooltip.OnClear = () => ItemTooltip.Clear()).
        /// </summary>
        public static Action OnClear;

        /// <summary>
        /// Optional callback invoked at the start of DrawDeferred().
        /// Wire up externally to let game-specific tooltips draw
        /// (e.g., Lux.UI.Widgets.Tooltip.OnDrawDeferred = () => ItemTooltip.DrawDeferred()).
        /// </summary>
        public static Action OnDrawDeferred;

        // ────────────────────────────────────────────────────────────────
        //  SET / CLEAR
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Set a single-line tooltip. Call during hover detection.
        /// </summary>
        public static void Set(string text)
        {
            _text = text;
            _title = null;
            _maxWidth = DefaultMaxWidth;
            _hasTooltip = true;
        }

        /// <summary>
        /// Set a tooltip with title and description.
        /// </summary>
        public static void Set(string title, string description)
        {
            _title = title;
            _text = description;
            _maxWidth = DefaultMaxWidth;
            _hasTooltip = true;
        }

        /// <summary>
        /// Set a tooltip with title, description, and custom max width.
        /// </summary>
        public static void Set(string title, string description, int maxWidth)
        {
            _title = title;
            _text = description;
            _maxWidth = Math.Max(80, maxWidth);
            _hasTooltip = true;
        }

        /// <summary>
        /// Clear pending tooltip. Called automatically by DraggablePanel.BeginDraw().
        /// </summary>
        public static void Clear()
        {
            _hasTooltip = false;
            _text = null;
            _title = null;
            OnClear?.Invoke();
        }

        // ────────────────────────────────────────────────────────────────
        //  SAFE MEASUREMENT
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Conservative text width measurement.
        /// Returns the LARGER of the real font measurement and the 10px-per-char estimate.
        /// This prevents the 7px fallback from letting text overflow the tooltip.
        /// </summary>
        public static int SafeMeasure(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int real = TextUtil.MeasureWidth(text);
            int estimated = text.Length * FallbackCharWidth;
            return Math.Max(real, estimated);
        }

        // ────────────────────────────────────────────────────────────────
        //  WORD WRAP
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Word-wrap text to fit within a pixel width using conservative measurement.
        /// Splits on explicit newlines first, then wraps each paragraph at word boundaries.
        /// Falls back to character-level breaking for words that exceed the width.
        /// If AutoWrap is OFF, returns each paragraph as a single unwrapped line.
        /// </summary>
        public static List<string> WordWrap(string text, int maxPixelWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            foreach (string paragraph in text.Split('\n'))
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    lines.Add("");
                    continue;
                }

                // If wrapping is disabled, truncate with "..." if too wide
                if (!AutoWrap)
                {
                    if (!AutoGrowWidth && SafeMeasure(paragraph) > maxPixelWidth)
                        lines.Add(Truncate(paragraph, maxPixelWidth));
                    else
                        lines.Add(paragraph);
                    continue;
                }

                if (SafeMeasure(paragraph) <= maxPixelWidth)
                {
                    lines.Add(paragraph);
                    continue;
                }

                // Greedy forward scan — build lines word by word
                string[] words = paragraph.Split(' ');
                string currentLine = "";

                for (int w = 0; w < words.Length; w++)
                {
                    string word = words[w];
                    if (word.Length == 0) continue;

                    string candidate = currentLine.Length == 0
                        ? word
                        : currentLine + " " + word;

                    if (SafeMeasure(candidate) <= maxPixelWidth)
                    {
                        currentLine = candidate;
                    }
                    else if (currentLine.Length == 0)
                    {
                        // Single word wider than max — break by character
                        BreakLongWord(word, maxPixelWidth, lines);
                    }
                    else
                    {
                        // Commit current line
                        lines.Add(currentLine);

                        // Start new line with this word (or break it if too long)
                        if (SafeMeasure(word) <= maxPixelWidth)
                            currentLine = word;
                        else
                        {
                            BreakLongWord(word, maxPixelWidth, lines);
                            currentLine = "";
                        }
                    }
                }

                if (currentLine.Length > 0)
                    lines.Add(currentLine);
            }

            return lines;
        }

        /// <summary>
        /// Break a single long word at character boundaries.
        /// Always takes at least 1 character per line to guarantee progress.
        /// </summary>
        private static void BreakLongWord(string word, int maxPixelWidth, List<string> lines)
        {
            int start = 0;
            while (start < word.Length)
            {
                int count = 1;
                while (start + count < word.Length &&
                       SafeMeasure(word.Substring(start, count + 1)) <= maxPixelWidth)
                {
                    count++;
                }

                lines.Add(word.Substring(start, count));
                start += count;
            }
        }

        /// <summary>
        /// Truncate a line so it fits within maxPixelWidth, replacing the tail with "...".
        /// Used when both AutoWrap and AutoGrowWidth are OFF.
        /// </summary>
        private static string Truncate(string text, int maxPixelWidth)
        {
            const string ellipsis = "...";

            // Measure the full combined string — not parts separately
            for (int i = text.Length - 1; i > 0; i--)
            {
                string candidate = text.Substring(0, i) + ellipsis;
                if (SafeMeasure(candidate) <= maxPixelWidth)
                    return candidate;
            }

            // Nothing fits — just return ellipsis
            return ellipsis;
        }

        // ────────────────────────────────────────────────────────────────
        //  ROUNDED RECT DRAWING
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw a filled rectangle with rounded corners.
        /// When CornerRadius is 0, falls back to a simple rectangle.
        /// </summary>
        private static void DrawRoundedRect(int x, int y, int w, int h, Color4 bg)
        {
            int r = CornerRadius;

            if (r <= 0 || w < r * 2 + 2 || h < r * 2 + 2)
            {
                // Sharp corners — simple rect
                UIRenderer.DrawRect(x, y, w, h, bg);
                return;
            }

            // Vertical center strip (full height, inset by radius on left/right)
            UIRenderer.DrawRect(x + r, y, w - r * 2, h, bg);
            // Horizontal center strip (full width, inset by radius on top/bottom)
            UIRenderer.DrawRect(x, y + r, w, h - r * 2, bg);

            // Fill corner curves with progressive pixel rows
            for (int i = 1; i < r; i++)
            {
                int offset = r - i;
                // Top-left
                UIRenderer.DrawRect(x + offset, y + i, i - offset + 1, 1, bg);
                // Top-right
                UIRenderer.DrawRect(x + w - i - 1, y + offset, 1, i - offset + 1, bg);
                // Bottom-left
                UIRenderer.DrawRect(x + offset, y + h - i - 1, i - offset + 1, 1, bg);
                // Bottom-right
                UIRenderer.DrawRect(x + w - i - 1, y + h - offset - (i - offset + 1), 1, i - offset + 1, bg);
            }
        }

        /// <summary>
        /// Draw a 1px outline with rounded corners.
        /// When CornerRadius is 0, falls back to a simple rectangle outline.
        /// </summary>
        private static void DrawRoundedRectOutline(int x, int y, int w, int h, Color4 border)
        {
            int r = CornerRadius;

            if (r <= 0 || w < r * 2 + 2 || h < r * 2 + 2)
            {
                // Sharp corners — simple outline
                UIRenderer.DrawRectOutline(x, y, w, h, border);
                return;
            }

            // Top edge (inset from corners)
            UIRenderer.DrawRect(x + r, y, w - r * 2, 1, border);
            // Bottom edge
            UIRenderer.DrawRect(x + r, y + h - 1, w - r * 2, 1, border);
            // Left edge
            UIRenderer.DrawRect(x, y + r, 1, h - r * 2, border);
            // Right edge
            UIRenderer.DrawRect(x + w - 1, y + r, 1, h - r * 2, border);

            // Corner diagonals — draw pixels along a quarter-circle approximation
            for (int i = 1; i <= r; i++)
            {
                int offset = r - i;
                // These create a stepped diagonal for each corner
                UIRenderer.DrawRect(x + offset, y + i, 1, 1, border);           // top-left
                UIRenderer.DrawRect(x + w - 1 - offset, y + i, 1, 1, border);   // top-right
                UIRenderer.DrawRect(x + offset, y + h - 1 - i, 1, 1, border);   // bottom-left
                UIRenderer.DrawRect(x + w - 1 - offset, y + h - 1 - i, 1, 1, border); // bottom-right
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  DRAW
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the deferred tooltip near the mouse cursor.
        /// Call at the END of your panel draw, after all content.
        /// Word-wraps long text and clamps to screen bounds.
        /// </summary>
        public static void DrawDeferred()
        {
            // Let external tooltip system (e.g., ItemTooltip) draw
            OnDrawDeferred?.Invoke();

            if (!_hasTooltip) return;
            if (string.IsNullOrEmpty(_text) && string.IsNullOrEmpty(_title))
            {
                _hasTooltip = false;
                return;
            }

            int effectiveMaxWidth = _maxWidth;

            // AutoGrowWidth: measure all content at unlimited width, then cap
            if (AutoGrowWidth)
            {
                int screenMaxWidth = WidgetInput.ScreenWidth - ScreenMargin * 2;
                int naturalWidth = 0;

                if (!string.IsNullOrEmpty(_title))
                {
                    foreach (string paragraph in _title.Split('\n'))
                        naturalWidth = Math.Max(naturalWidth, TextUtil.MeasureWidth(paragraph));
                }
                if (!string.IsNullOrEmpty(_text))
                {
                    foreach (string paragraph in _text.Split('\n'))
                        naturalWidth = Math.Max(naturalWidth, TextUtil.MeasureWidth(paragraph));
                }
                // Add 10% buffer — MeasureWidth can underestimate slightly
                naturalWidth = naturalWidth + (naturalWidth / 10);

                int desired = naturalWidth + Padding * 2;
                // Grow beyond DefaultMaxWidth but cap at screen width
                effectiveMaxWidth = Math.Max(_maxWidth, Math.Min(desired, screenMaxWidth));
            }

            int maxContentWidth = effectiveMaxWidth - Padding * 2;

            // Build all lines with title tracking
            var lines = new List<string>();
            int titleLineCount = 0;

            if (!string.IsNullOrEmpty(_title))
            {
                var titleLines = WordWrap(_title, maxContentWidth);
                titleLineCount = titleLines.Count;
                lines.AddRange(titleLines);
            }

            if (!string.IsNullOrEmpty(_text))
                lines.AddRange(WordWrap(_text, maxContentWidth));

            if (lines.Count == 0)
            {
                _hasTooltip = false;
                return;
            }

            // Measure actual widths for tight-fitting tooltip
            // Real font + 10% buffer prevents clipping without massive overestimate
            int contentWidth = 0;
            foreach (var line in lines)
            {
                int w = TextUtil.MeasureWidth(line);
                contentWidth = Math.Max(contentWidth, w + (w / 10));
            }

            int tooltipWidth = Math.Min(contentWidth + Padding * 2, effectiveMaxWidth);
            int tooltipHeight = lines.Count * LineHeight + Padding * 2;

            // Position near mouse, clamped to screen edges
            int tx = WidgetInput.MouseX + CursorOffset;
            int ty = WidgetInput.MouseY + CursorOffset;

            if (tx + tooltipWidth > WidgetInput.ScreenWidth - ScreenMargin)
                tx = WidgetInput.MouseX - tooltipWidth - ScreenMargin;
            if (ty + tooltipHeight > WidgetInput.ScreenHeight - ScreenMargin)
                ty = WidgetInput.MouseY - tooltipHeight - ScreenMargin;

            tx = Math.Max(ScreenMargin, tx);
            ty = Math.Max(ScreenMargin, ty);

            // Rounded background + border
            DrawRoundedRect(tx, ty, tooltipWidth, tooltipHeight, UIColors.TooltipBg);
            DrawRoundedRectOutline(tx, ty, tooltipWidth, tooltipHeight, UIColors.Border);

            // Clip text to tooltip bounds — safety net
            UIRenderer.BeginClip(tx, ty, tooltipWidth, tooltipHeight);

            int ly = ty + Padding;
            for (int i = 0; i < lines.Count; i++)
            {
                Color4 color = (i < titleLineCount) ? UIColors.TextTitle : UIColors.Text;
                UIRenderer.DrawText(lines[i], tx + Padding, ly, color);
                ly += LineHeight;
            }

            UIRenderer.EndClip();

            _hasTooltip = false;
        }
    }
}
