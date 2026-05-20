# Docking pill — product spec

When Snapdesk is **docked to a screen edge** and **rolled up (auto-hidden)**, a small rounded pill on that edge shows where the app is docked. The pill is the only affordance in that state—not profile chips, window frames, or ghost outlines.

## When the pill is visible

| State | Pill |
|-------|------|
| Undocked | Off |
| Docked, expanded | Off |
| Docked, auto-hidden | **On** (persistent until reveal) |
| Dragging the widget | Off |

## Look and feel (macOS-style)

A minimal edge capsule—**50×4 px** (length × thickness), fully rounded ends (corner radius = half of thickness)—centered on the docked monitor’s bezel. Length is ~7× thickness so it reads as a thin “tick,” not a bar.

- **Fill:** soft grey (`#E6B8B8B8`, ~90% opacity)—a single capsule only; no inner bars or frame strips.
- **Material:** flat fill; no border, shadow, gradient, or icon.
- **Orientation:** horizontal on top/bottom docks; vertical on left/right docks.
- **Placement:** **6 px inset** from the monitor work-area edge (into the desktop); centered along the edge at the main widget’s vertical/horizontal center. On multi-monitor setups, only on the **pinned** docked display.
- **Motion:** appears instantly when roll-up completes; static while hidden; disappears immediately when the widget expands.
- **Theme:** follows Snapdesk light/dark theme via `AppTheme.IsDark`.

The handle is a **locator only**—non-interactive (click-through overlay). Hovering the **screen-edge reveal band** (~6 px along the same edge), not the pill, restores the widget. The pill does not change on hover (macOS-like).

### Must not look like

- Profile chips, window chrome, or a ghost/outline of the full widget
- A Windows notification or toast
- A thick tab or drag handle

## Acceptance tests

1. Dock to each edge; move cursor away → rolls up within ~0.5 s.
2. Pill visible immediately and stays visible while cursor is elsewhere.
3. Correct orientation per edge; no chips in peek; no black outline ghost.
4. Hover reveal band → widget expands; pill off.
5. Cold start at saved dock position → hide + pill without re-dock.
6. Undock → pill off.
7. Readable on solid black, white, and busy wallpaper at 100% and 125% scaling.

## Multi-monitor (shared bezel)

8. Dock to the edge between two monitors (e.g. right edge of the left display). Move away → widget stays on the docked monitor (does not slide onto the neighbor). Pill appears on that same monitor’s edge only.
9. Repeat for left, top, and bottom shared bezels.
10. Hover the docked monitor’s reveal band → expand; pill hides.

Hide moves the main window to its normal dock position inside the pinned monitor, then calls `AppWindow.Hide()` so only the pill overlay remains. The pill is anchored to that monitor’s `WorkArea`, not `GetFromPoint` after an off-screen slide.

## Implementation

| File | Role |
|------|------|
| [`DockRevealIndicatorHelper.cs`](../LayoutProfiles.WinUI/Helpers/DockRevealIndicatorHelper.cs) | Pill size, colors, layout |
| [`DockRevealPillWindow.cs`](../LayoutProfiles.WinUI/Helpers/DockRevealPillWindow.cs) | Borderless topmost overlay |
| [`DockRevealPillController.cs`](../LayoutProfiles.WinUI/Helpers/DockRevealPillController.cs) | Show/hide pill window |
| [`DockDisplayHelper.cs`](../LayoutProfiles.WinUI/Helpers/DockDisplayHelper.cs) | Multi-monitor dock resolution |
| [`WindowPositionController.cs`](../LayoutProfiles.WinUI/Helpers/WindowPositionController.cs) | Auto-hide, reveal band |
