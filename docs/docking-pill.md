# Docking pill — product spec

When Snapdesk is **docked to a screen edge** and **rolled up (auto-hidden)**, a small rounded pill on that edge shows where the app is docked. The pill is the only affordance in that state—not profile chips, window frames, or ghost outlines.

## When the pill is visible

| State | Pill |
|-------|------|
| Undocked | Off |
| Docked, expanded | Off |
| Docked, auto-hidden | **On** (persistent until reveal) |
| Dragging the widget | Off |

## Visual

- Capsule ~24×3.5 px (orientation swaps on left/right docks)
- Soft white on dark theme; dark gray on light theme
- Centered on the docked screen edge
- Non-interactive; existing screen-edge hover band reveals the widget

## Acceptance tests

1. Dock to each edge; move cursor away → rolls up within ~0.5 s.
2. Pill visible immediately and stays visible while cursor is elsewhere.
3. Correct orientation per edge; no chips in peek; no black outline ghost.
4. Hover reveal band → widget expands; pill off.
5. Cold start at saved dock position → hide + pill without re-dock.
6. Undock → pill off.

## Multi-monitor (shared bezel)

7. Dock to the edge between two monitors (e.g. right edge of the left display). Move away → widget stays on the docked monitor (does not slide onto the neighbor). Pill appears on that same monitor’s edge only.
8. Repeat for left, top, and bottom shared bezels.
9. Hover the docked monitor’s reveal band → expand; pill hides.

Hide moves the main window to its normal dock position inside the pinned monitor, then calls `AppWindow.Hide()` so only the pill overlay remains. The pill is anchored to that monitor’s `WorkArea`, not `GetFromPoint` after an off-screen slide.

Implementation: [`DockDisplayHelper.cs`](../LayoutProfiles.WinUI/Helpers/DockDisplayHelper.cs), [`DockRevealPillController.cs`](../LayoutProfiles.WinUI/Helpers/DockRevealPillController.cs), [`DockRevealPillWindow.cs`](../LayoutProfiles.WinUI/Helpers/DockRevealPillWindow.cs), [`WindowPositionController.cs`](../LayoutProfiles.WinUI/Helpers/WindowPositionController.cs).
