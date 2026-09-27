# GK2 Move Buildings

BepInEx 5 mod for Graveyard Keeper 2 that adds a **Move** tile next to **Remove** in the building menu.

1. Open the building menu, pick **Move** (four-arrow icon).
2. Hover a building (blue highlight), click it.
3. Its ghost follows the cursor: rotate with the usual key, click to place. Free of charge.
4. Right click / Esc during placement puts it back and returns to selection.

What is preserved: the object itself (same id), chest contents, fuel, craft queue, zombie workers
at dock points, workbench slot extensions (moved and re-seated in their slots, also when rotated), garden orders, building scripts,
conveyor links (rebuilt at the new spot) and conveyor-chest slot filters.

Not movable: fight buildings, anything the game refuses to remove (quest-locked, auto-built belt pieces).

## Build

```
dotnet build -c Release -p:Deploy=true            # builds and copies into <game>/BepInEx/plugins
dotnet build -c Release -p:GameDir="/path/to/Graveyard Keeper 2"
```

Log: `<game>/BepInEx/LogOutput.log`, lines tagged `GK2 Move Buildings`.

## Art

`move_icon.png` and `move_cursor.png` are drawn by the scripts in `tools/` in the style of the game's
build icons and cursors (32x32 pixel art; the icon sits in a 48x48 canvas like the game's sprites).
`tools/mkcursor.py` recolours the game's own remove cursor, which you extract yourself as `remove_cursor_src.png`.
