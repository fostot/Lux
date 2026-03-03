# Lux

A reusable UI widget toolkit for the [TerrariaModder](https://github.com/terraria-modder) platform. Lux provides rich, interactive UI components — draggable panels with edge/corner resize, text inputs with IME support, checkboxes, tooltips with word-wrap, and text measurement utilities — designed to be dropped into any TerrariaModder mod.

> **Platform:** TerrariaModder (Harmony-based mod injection)
> **Framework:** .NET Framework 4.8
> **Author:** Fostot

---

## Components

| Widget | Type | Description |
|--------|------|-------------|
| **DraggablePanel** | Instance | Full-featured panel container with drag, resize, header bar, close button, clipping, and z-order management. |
| **TextInput** | Instance | Single-line text input with focus management, IME support, placeholder text, clear button, and cursor visibility. |
| **Checkbox** | Static | Stateless checkbox rendering with checked/partial states and optional label support. |
| **Tooltip** | Static | Deferred tooltip renderer with word-wrap, rounded corners, title/description layout, and auto-positioning. |
| **TextUtil** | Static | Text measurement, truncation, and visible-tail utilities for both proportional and monospace fonts. |

---

## Dependencies

| Dependency | Description |
|------------|-------------|
| **TerrariaModder.Core** | Core mod framework — provides `UIRenderer`, `WidgetInput`, `InputState`, `UIColors`, and base widget APIs. |
| **Monofont** | Monospace bitmap font renderer — provides fixed-width glyph drawing for optional mono-styled text. |

---

## Installation

1. Build `Lux.dll` from the project (or grab it from `bin/`).
2. Place it alongside your mod DLL where TerrariaModder can resolve it.
3. Add a reference to `Lux.dll` in your mod's `.csproj`:

```xml
<Reference Include="Lux">
  <HintPath>..\Lux\bin\Lux.dll</HintPath>
  <Private>false</Private>
</Reference>
```

4. Import the namespace:

```csharp
using Lux.UI.Widgets;
```

---

## Usage

### DraggablePanel

The core component. Wraps your UI content in a draggable, optionally resizable panel with a header bar, close button, and content clipping.

**Basic panel:**

```csharp
// Create once (field in your mod class)
private DraggablePanel _panel = new DraggablePanel("my-panel", "My Panel", 400, 300);

// During mod initialization — register the draw callback
_panel.RegisterDrawCallback(DrawMyPanel);

// Toggle with a keybind
if (InputState.IsKeyJustPressed(KeyCode.P))
    _panel.Toggle();

// Draw callback
private void DrawMyPanel()
{
    if (!_panel.BeginDraw()) return;

    // Draw your content using _panel.ContentX, ContentY, ContentWidth, ContentHeight
    int x = _panel.ContentX;
    int y = _panel.ContentY;
    int w = _panel.ContentWidth;

    UIRenderer.DrawText("Hello from Lux!", x, y, UIColors.Text);

    _panel.EndDraw();
}
```

**Resizable panel with all options:**

```csharp
var panel = new DraggablePanel("settings-panel", "Settings", 500, 400)
{
    Resizable       = true,       // enable edge and corner resize
    MinWidth        = 300,        // minimum resize width
    MinHeight       = 250,        // minimum resize height
    ShowCloseButton = true,       // X button in header
    CloseOnEscape   = true,       // pressing Escape closes the panel
    Draggable       = true,       // drag by header to reposition
    ClipContent     = true,       // clip child content to panel bounds
    UseMonoFont     = false,      // use proportional font for title
    HeaderHeight    = 35,         // header bar height in pixels
    Padding         = 8,          // content area padding
    ShowIcon        = true,       // display icon in the header
    OnClose         = () => { /* cleanup logic */ }
};

// Open centered on screen
panel.Open();

// Or open at a specific position
panel.Open(100, 50);
```

When `Resizable = true`, users can drag any edge or corner to resize. Visual grip indicators appear at all four corners and highlight on hover. The panel enforces `MinWidth`/`MinHeight` and correctly handles left/top edge dragging by repositioning the panel origin.

**Panel content area:**

After `BeginDraw()`, use these properties for layout:

| Property | Description |
|----------|-------------|
| `ContentX` | Left edge of the content area (panel X + padding) |
| `ContentY` | Top of content area (below the header) |
| `ContentWidth` | Available width (panel width minus padding on both sides) |
| `ContentHeight` | Available height (below header, minus bottom padding) |
| `X`, `Y` | Top-left corner of the entire panel |
| `Width`, `Height` | Full panel dimensions |

**Icon support:**

```csharp
// Set a texture directly
panel.IconTexture = UIRenderer.LoadTexture("icon.png");

// Or use a resolver for dynamic icons
panel.IconResolver = (panelId) => MyMod.GetIconForPanel(panelId);
```

---

### TextInput

A stateful single-line text field with focus handling, IME composition, placeholder text, an X clear button, and cursor visibility via text scrolling.

```csharp
// Create once (field)
private TextInput _searchInput = new TextInput("Search...", maxLength: 200);

// IMPORTANT: call Update() in your mod's Update phase
public override void OnUpdate()
{
    _searchInput.Update();
}

// Draw inside a panel's BeginDraw/EndDraw block
private void DrawMyPanel()
{
    if (!_panel.BeginDraw()) return;

    int x = _panel.ContentX;
    int y = _panel.ContentY;
    int w = _panel.ContentWidth;

    // Draw the input field — returns current text
    string text = _searchInput.Draw(x, y, w, 28);

    // React to changes
    if (_searchInput.HasChanged)
    {
        FilterResults(text);
    }

    _panel.EndDraw();
}
```

**Key input blocking:**

When using TextInput inside a DraggablePanel, set `KeyBlockId` so keyboard shortcuts don't fire while the user is typing:

```csharp
_searchInput.KeyBlockId = _panel.PanelId;
```

**Programmatic control:**

```csharp
_searchInput.Focus();          // focus the input
_searchInput.Unfocus();        // remove focus
_searchInput.Clear();          // clear text
_searchInput.Text = "hello";   // set text directly
bool focused = _searchInput.IsFocused;
```

---

### Checkbox

Stateless checkbox rendering. You manage the boolean state; `Draw` returns `true` on click.

**Simple checkbox:**

```csharp
private bool _godMode = false;

// Inside your draw callback
if (Checkbox.Draw(x, y, 16, _godMode))
    _godMode = !_godMode;
```

**Checkbox with label:**

```csharp
private bool _fullBright = false;

// Full row — clicking the label also toggles
if (Checkbox.DrawWithLabel(x, y, width: 200, height: 24, "Full Bright", _fullBright))
    _fullBright = !_fullBright;
```

**Partial (indeterminate) state:**

```csharp
// Renders a warning-colored bar instead of a checkmark
Checkbox.Draw(x, y, 16, isChecked: false, partial: true);
```

**Monospace font label:**

```csharp
Checkbox.DrawWithLabel(x, y, 200, 24, "Fixed Width Label", _checked, useMonoFont: true);
```

---

### Tooltip

A deferred renderer — call `Set()` during your hover detection, and the tooltip draws at the end of the frame via `DrawDeferred()` (called automatically by `DraggablePanel.EndDraw()`).

**Basic tooltip:**

```csharp
// During hover detection in your draw code
if (WidgetInput.IsMouseOver(buttonX, buttonY, buttonW, buttonH))
{
    Tooltip.Set("Click to toggle god mode");
}
```

**Title + description:**

```csharp
if (WidgetInput.IsMouseOver(itemX, itemY, itemW, itemH))
{
    Tooltip.Set("Zenith Sword", "The ultimate melee weapon.\nCombines all sword projectiles.");
}
```

**Custom max width:**

```csharp
Tooltip.Set("Wide Tooltip", "This tooltip can be wider than the default.", maxWidth: 450);
```

**Configuration (set once at startup):**

```csharp
Tooltip.CornerRadius    = 4;     // 0-8px rounded corners (0 = sharp)
Tooltip.AutoWrap        = true;  // word-wrap long text at tooltip edges
Tooltip.AutoGrowWidth   = false; // grow beyond DefaultMaxWidth to fit content
Tooltip.DefaultMaxWidth = 300;   // base max width in pixels
```

**Integration callbacks:**

Wire up external tooltip systems to coexist with Lux tooltips:

```csharp
// Clear game tooltips when Lux clears
Tooltip.OnClear = () => ItemTooltip.Clear();

// Let game tooltips render alongside Lux tooltips
Tooltip.OnDrawDeferred = () => ItemTooltip.DrawDeferred();
```

---

### TextUtil

Utilities for measuring, truncating, and tail-slicing text strings with actual font metrics.

```csharp
// Measure pixel width of a string
int width = TextUtil.MeasureWidth("Hello World");

// Truncate with "..." to fit a pixel width
string label = TextUtil.Truncate("Very Long Item Name Here", maxPixelWidth: 150);
// Result: "Very Long It..."

// Get the visible tail (for scrollable text inputs)
string tail = TextUtil.VisibleTail("Long text with cursor at end|", maxPixelWidth: 200);
// Result: "sor at end|" (whatever fits from the right)

// Monospace truncation (pure math, no GPU measurement needed)
string mono = TextUtil.TruncateMono("Fixed Width Text", maxPixelWidth: 100);
```

---

## Full Example: Building a Mod Panel

Here's a complete example of a mod using Lux to create a settings panel with all widget types:

```csharp
using Lux.UI.Widgets;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.UI;

public class MyMod
{
    private DraggablePanel _panel;
    private TextInput _nameInput;
    private bool _featureEnabled = false;
    private bool _advancedMode = false;

    public void OnInitialize()
    {
        // Create a resizable panel
        _panel = new DraggablePanel("mymod-settings", "My Mod Settings", 450, 350)
        {
            Resizable     = true,
            MinWidth      = 350,
            MinHeight     = 250,
            CloseOnEscape = true,
            OnClose       = () => SaveSettings()
        };
        _panel.RegisterDrawCallback(DrawSettingsPanel);

        // Create text input
        _nameInput = new TextInput("Enter player name...", maxLength: 50)
        {
            KeyBlockId = _panel.PanelId
        };

        // Configure tooltips
        Tooltip.CornerRadius = 3;
        Tooltip.AutoWrap = true;
    }

    public void OnUpdate()
    {
        // Toggle panel with keybind
        if (InputState.IsKeyJustPressed(KeyCode.F7))
            _panel.Toggle();

        // TextInput needs Update() in the update phase
        _nameInput.Update();
    }

    private void DrawSettingsPanel()
    {
        if (!_panel.BeginDraw()) return;

        int x = _panel.ContentX;
        int y = _panel.ContentY;
        int w = _panel.ContentWidth;
        int rowHeight = 28;

        // Text input
        UIRenderer.DrawText("Player Name:", x, y + 6, UIColors.Text);
        _nameInput.Draw(x + 110, y, w - 110, rowHeight);
        y += rowHeight + 8;

        // Checkboxes
        if (Checkbox.DrawWithLabel(x, y, w, rowHeight, "Enable Feature", _featureEnabled))
            _featureEnabled = !_featureEnabled;

        if (WidgetInput.IsMouseOver(x, y, w, rowHeight))
            Tooltip.Set("Enable Feature", "Toggles the main feature on or off.\nChanges take effect immediately.");
        y += rowHeight + 4;

        if (Checkbox.DrawWithLabel(x, y, w, rowHeight, "Advanced Mode", _advancedMode))
            _advancedMode = !_advancedMode;
        y += rowHeight + 4;

        // Conditional content
        if (_advancedMode)
        {
            UIRenderer.DrawText("Advanced options go here...", x + 10, y, UIColors.TextDim);
        }

        _panel.EndDraw();
    }

    private void SaveSettings() { /* persist state */ }
}
```

---

## Architecture Notes

- **Draw-phase input handling** — All widget input (clicks, hover, keyboard) is processed during the draw phase, not in a separate input phase.
- **Click consumption** — Widgets consume mouse clicks to prevent click-through to the game world or lower panels.
- **Deferred tooltips** — `Tooltip.Set()` stores data during the frame; `DrawDeferred()` renders it last so tooltips appear on top of all content.
- **Z-order management** — `DraggablePanel` registers bounds with `UIRenderer` and uses `BringToFront` so overlapping panels interact correctly.
- **Content clipping** — `BeginDraw()`/`EndDraw()` wraps content in a clip region to prevent overflow. Disable with `ClipContent = false` for panels that manage their own clipping.
- **Conservative text measurement** — Tooltips use `max(real_measure, char_count * 10px)` to prevent text overflow from underestimated font widths.

---

## License

MIT
