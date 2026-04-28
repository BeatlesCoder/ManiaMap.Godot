# ManiaMap.Godot Non-Rectangular Room Shapes and Template Sizes

## 1. CELL GRID SYSTEM (ActiveCells)

The ActiveCells system is a 2D boolean array that represents room layout geometry.

**File**: RoomNode2D.cs (line 58)
**File**: RoomNode3D.cs (line 58)

Structure:
- Outer Array: Represents rows
- Inner Arrays: Represent columns
- Values: true = active cell (walkable), false = inactive cell

### Key Properties

**Rows and Columns** - RoomNode2D.cs lines 43-49:
- Rows: default 1, range 1-10+
- Columns: default 1, range 1-10+
- CellSize: default (96,96) for 2D, (1,1,1) for 3D

### Resizing and Default Behavior

**IRoomNodeExtensions.cs lines 130-158 (SizeActiveCells)**:
- Removes excess rows if decreased
- Adjusts column counts in existing rows
- NEW CELLS DEFAULT TO ACTIVE (true)
- Extends rows with all cells active

### Cell Activity Operations

**IRoomNodeExtensions.cs lines 103-124**:
Three operations:
1. Activate: Set to true
2. Deactivate: Set to false
3. Toggle: Flip state

### Runtime Behavior

**RoomNode2D.cs lines 323-335 (CreateCellAreas)**:
- Only active cells get CellArea2D instances created
- Checks "if (row[j])" before creating area

---

## 2. ROOM TEMPLATE SIZES

### Properties Definition

**RoomNode2D.cs lines 43-55**:
- Rows: 1-10+ cells
- Columns: 1-10+ cells
- CellSize: default Vector2(96, 96)

**RoomNode3D.cs lines 43-55**:
- Rows: 1-10+ cells
- Columns: 1-10+ cells
- CellSize: default Vector3(1, 1, 1)

### Sample Room Sizes

**L-Shaped (angle_3x4_room_2d.tscn lines 15-17)**:
Rows = 4, Columns = 3
ActiveCells = [[T,T,T], [T,F,F], [T,F,F], [T,F,F]]

**Rectangular (square_3x3_room_2d.tscn lines 16-18)**:
Rows = 3, Columns = 3
ActiveCells = [[T,T,T], [T,T,T], [T,T,T]]

**3D (square_3x3_room_3d.tscn lines 27-30)**:
Rows = 3, Columns = 3, CellSize = Vector3(6, 6, 6)

### Cell Position Calculation

**RoomNode2D lines 361-364**:
```
CellCenterLocalPosition = (column * CellSize.X, row * CellSize.Y) + 0.5 * CellSize
```

**RoomNode3D lines 294-297**:
```
CellCenterLocalPosition = (column * CellSize.X, 0, row * CellSize.Z) + 0.5 * CellSize
```

---

## 3. TEMPLATE SERIALIZATION

### RoomTemplateResource Structure

**File**: RoomTemplateResource.cs lines 14-46
Properties:
- EditId: Allow manual ID editing
- Id: Unique template ID
- TemplateName: Human-readable name
- ScenePath: File path
- SerializedText: JSON of RoomTemplate

### Build Process

**IRoomNodeExtensions.cs lines 165-186 (GetMMRoomTemplate)**:
1. Get cells from ActiveCells grid
2. Add doors from DoorNodes
3. Add features from FeatureNodes
4. Get collectable spots
5. Validate entire template

### JSON Structure

From angle_3x4_room_2d.room_template.tres:
```json
{
  "Id": 353812577,
  "Name": "Angle3x4Room2D",
  "Cells": {
    "Rows": 4,
    "Columns": 3,
    "Array": [
      {"Doors": {"Map": [...]}, "Features": []},
      {"Doors": {"Map": []}, "Features": []},
      null,
      ...
    ]
  },
  "CollectableSpots": {
    "Map": [
      {"Key": 1058014276, "Value": {"Position": {"X": 0, "Y": 2}}}
    ]
  }
}
```

Key Details:
- Sparse array with null for inactive cells
- Door encoding: Key = DoorDirection (0=N, 1=S, 2=E, 3=W)
- CollectableSpots: Position X=Row, Y=Column

---

## 4. DOOR PLACEMENT ON IRREGULAR SHAPES

### Door Node Classes

**DoorNode2D.cs lines 15-69** and **DoorNode3D.cs lines 15-69**

Properties:
- AutoAssignDirection: Automatically determine facing
- DoorDirection: Which side (North/South/East/West/Top/Bottom)
- DoorType: Type of door
- DoorCode: Door flags

### AutoAssign Process

**CellChild2D.cs lines 33-39**:
```csharp
public virtual void AutoAssign(RoomNode2D room)
{
    Room = room;
    if (AutoAssignCell)
        (Row, Column) = room.FindClosestActiveCellIndex(GlobalPosition);
}
```

**DoorNode2D.cs lines 62-69**:
```csharp
public override void AutoAssign(RoomNode2D room)
{
    base.AutoAssign(room);
    if (AutoAssignDirection)
        DoorDirection = room.FindClosestDoorDirection(Row, Column, GlobalPosition);
}
```

### Finding Closest Active Cell

**RoomNode2D.cs lines 243-274**:
1. Quick check: Is cell directly under door position active?
2. If not, search ALL active cells
3. Return closest by distance
4. ONLY considers cells where ActiveCells[row][column] == true

### Finding Closest Door Direction

**RoomNode2D.cs lines 283-317**:
Uses dot product alignment:
```csharp
var delta = (doorPosition - cellCenter) / CellSize;
var dotProduct = delta.Dot(directionVector);
// Return direction with HIGHEST dot product
```

2D Directions (vectors):
- North: (0, -1)
- East: (1, 0)
- South: (0, 1)
- West: (-1, 0)

**RoomNode3D.cs lines 369-407**:
Adds two more:
- Top: (0, 1, 0)
- Bottom: (0, -1, 0)

### Door Validation During Serialization

**IRoomNodeExtensions.cs lines 288-299**:
1. Find all DoorNode children
2. For each door, get Row/Column/Direction
3. Add door to cell in cells array
4. Validate no conflicts

---

## 5. PRACTICAL ROOM EXAMPLES

### Example 1: L-Shaped (angle_3x4_room_2d.tscn)

Layout (4 rows x 3 columns):
```
[A][A][A]  <- Row 0: 3 active cells
[A][ ][ ]  <- Rows 1-3: 1 active cell each
[A][ ][ ]
[A][ ][ ]
```

ActiveCells = [[true,true,true], [true,false,false], [true,false,false], [true,false,false]]

Doors placed at:
- Lines 29-71: Mixed North/South/East/West
- 2 north doors, 2 south, 2 west, 2 east

### Example 2: Square (square_3x3_room_2d.tscn)

Layout (3 rows x 3 columns):
```
[A][A][A]
[A][A][A]
[A][A][A]
```

ActiveCells = [[true,true,true], [true,true,true], [true,true,true]]

Doors:
- North: 3 doors at row 0, columns 0,1,2 (lines 30-42)
- South: 3 doors at row 2, columns 0,1,2 (lines 44-62)
- West: 3 doors at rows 0,1,2, column 0 (lines 64-79)
- East: 3 doors at rows 0,1,2, column 2 (lines 81-99)

### Example 3: 3D Square (square_3x3_room_3d.tscn)

CellSize = Vector3(6, 6, 6)
Same 3x3 layout but with 6-unit cells

Doors (lines 32-223):
- Horizontal wall doors (lines 32-84): N/E/S/W directions
- Top doors (lines 106-163): DoorDirection = 4, 9 doors
- Bottom doors (lines 165-223): DoorDirection = 5, 9 doors
- Total: 27 potential connection points

---

## 6. NON-RECTANGULAR PATTERN IDEAS

### T-Shaped Room
```
      [A]
[A] [A] [A]
      [A]
```
ActiveCells = [[F,T,F], [T,T,T], [F,T,F]]

### Cross-Shaped Room
```
    [A]   [A]
[A][A][A][A][A]
    [A]   [A]
```

### Ring-Shaped Room
```
[A][A][A]
[A][ ][A]
[A][A][A]
```
ActiveCells = [[T,T,T], [T,F,T], [T,T,T]]

---

## 7. KEY CODE PATTERNS

### Cell Iteration (only active cells)
```csharp
for (int i = 0; i < ActiveCells.Count; i++)
{
    var row = ActiveCells[i];
    for (int j = 0; j < row.Count; j++)
    {
        if (row[j])  // Check if active
        {
            // Process cell
        }
    }
}
```

### Validation
IRoomNodeExtensions.cs lines 174-183:
Catches duplicate IDs, invalid configs, misplacements

### Bounds Checking
IRoomNodeExtensions.cs lines 21-24:
```csharp
return (uint)row < (uint)room.Rows && (uint)column < (uint)room.Columns;
```

---

## 8. FILE REFERENCE TABLE

File | Lines | Purpose
-----|-------|--------
RoomNode2D.cs | 43-55, 323-335 | 2D room, cell management
RoomNode3D.cs | 43-55, 265-277 | 3D room, cell management
RoomTemplateResource.cs | 14-96 | Template I/O
IRoomNodeExtensions.cs | 130-158, 165-186, 288-299, 412-436 | Cell ops, template build
DoorNode2D.cs | 15-69 | 2D door placement
DoorNode3D.cs | 15-69 | 3D door placement
CellChild2D.cs | 1-50 | Base for cell children
angle_3x4_room_2d.tscn | 15-17 | L-shaped example
square_3x3_room_2d.tscn | 16-18 | Rectangular example
square_3x3_room_3d.tscn | 27-30 | 3D example

