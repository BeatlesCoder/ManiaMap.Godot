# LayoutGenerator 扩展方案（完整版）

## 一、目标

在 ManiaMap 核心库基础上，以尽量少的修改，支持以下扩展需求：

1. **位置约束**：出生房在地图底部（X 轴随机），撤离房在地图顶部
2. **最小距离约束**：可配置重点房间之间的最小格子距离（如出生房离宝箱房≥10格）
3. **地图尺寸限制**：可配置地图最大 size（行数×列数上限）
4. **重点房间数量可配**：出生房数量、撤离房数量等
5. **可视化标识**：生成结果中重要房间有醒目标记，一眼可辨

---

## 二、现有算法流程（不变）

### 2.1 总体流程

```
LayoutGenerator.Generate()
  │
  ├── Graph.FindChains(MaxBranchLength)    // 图分解为有序 chain 列表
  │     ├── AddCycleChains()               // 先处理环路
  │     ├── AddBranchChains()              // 再处理分支
  │     └── FormSequentialChains()         // 排成可依次放置的顺序
  │
  ├── baseLayout = new Layout(layoutId)    // 空布局，起点
  │
  └── while (layouts.Count > 0)            // 逐 chain 放置
        ├── ChainIndex >= chains.Count?    // 全部 chain 放完？
        │     └── Layout.IsComplete() → 返回结果
        ├── Layout.Rebases > AllowableRebases? // 当前布局重试过多？
        │     └── 回溯到上一层
        └── AddChain(chains[ChainIndex])   // 尝试放置当前 chain
              ├── 成功 → ChainIndex++, push 到栈
              └── 失败 → 当前布局 Rebases++, 重试
```

### 2.2 AddRoom 核心逻辑（第397-433行）— 所有扩展的主要切入点

```csharp
private bool AddRoom(IRoomSource source, Uid fromRoomId, DoorCode code, EdgeDirection direction)
{
    var fromRoom = Layout.Rooms[fromRoomId];
    var z = source.Z - fromRoom.Position.Z;

    foreach (var entry in GetTemplateGroupEntries(source.TemplateGroup))
    {
        foreach (var config in GetConfigurations(fromRoom.Template, entry.Template))
        {
            var position = config.Position + fromRoom.Position.To2D();

            if (!config.Matches(z, code, direction))       continue;  // 检查1：门兼容
            if (Layout.Intersects(entry.Template, position, source.Z)) continue;  // 检查2：不重叠

            // ★ 第一个通过检查的就直接采用（无位置偏好、无距离检查、无尺寸检查）
            var room = new Room(source, position, entry.Template, RandomSeed.Next());
            Layout.Rooms.Add(room.Id, room);

            if (!AddDoorConnection(fromRoomId, source.RoomId, config))
            {
                Layout.Rooms.Remove(room.Id);
                continue;
            }

            Layout.IncreaseTemplateCount(entry);
            return true;
        }
    }
    return false;
}
```

### 2.3 坐标系统

```
Position.X = 行（Row）   → Godot Y 轴，X 越大越靠下（地图底部）
Position.Y = 列（Column）→ Godot X 轴

Godot 映射（RoomNode2D.MoveToLayoutPosition）：
  Godot.X = CellSize.X × Position.Y
  Godot.Y = CellSize.Y × Position.X
```

---

## 三、完整文件修改清单

### ManiaMap 核心库 (`/d/UGit/ManiaMap/src/ManiaMap/`)

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `PositionConstraint.cs` | **新增** | 位置约束枚举 |
| `DistanceConstraint.cs` | **新增** | 最小距离约束数据类 |
| `IRoomSource.cs` | 修改 | 接口加 `PositionConstraint` 属性 |
| `Graphs/LayoutNode.cs` | 修改 | 实现 `PositionConstraint` 属性 |
| `Graphs/LayoutEdge.cs` | 修改 | 实现 `PositionConstraint` 属性（默认 None） |
| `Generators/LayoutGenerator.cs` | 修改 | 核心：排序 + 距离检查 + 尺寸检查 + 最终验证 |

### ManiaMap.Godot 封装 (`/d/UGit/ManiaMapGodot/`)

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `ManiaMap.Godot.csproj` | 修改 | NuGet → 本地项目引用 |
| `addons/.../graphs/LayoutGraphNode.cs` | 修改 | 编辑器暴露 PositionConstraint |
| `addons/.../generators/LayoutGeneratorStep.cs` | 修改 | 暴露 MaxMapSize、DistanceConstraints |
| `samples/scripts/RoomLayout2DSample.cs` | 修改 | 生成后添加重要房间标记 |

---

## 四、扩展1：位置约束

### 4.1 新增：PositionConstraint 枚举

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/PositionConstraint.cs`（新建）

```csharp
namespace MPewsey.ManiaMap
{
    /// <summary>
    /// Specifies a position constraint for room placement in the layout.
    /// </summary>
    public enum PositionConstraint
    {
        /// <summary>
        /// No position constraint.
        /// </summary>
        None = 0,

        /// <summary>
        /// Room should be placed toward the bottom of the map (high row values).
        /// X-axis (column) position is random.
        /// </summary>
        Bottom = 1,

        /// <summary>
        /// Room should be placed toward the top of the map (low row values).
        /// </summary>
        Top = 2,

        /// <summary>
        /// Room should be placed at the top-half edge of the map.
        /// The final validation verifies the room is on the layout boundary.
        /// </summary>
        TopEdge = 3,
    }
}
```

### 4.2 修改：IRoomSource 接口

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/IRoomSource.cs`

```diff
 public interface IRoomSource
 {
     Uid RoomId { get; }
     string Name { get; }
     Color4 Color { get; }
     int Z { get; }
     string TemplateGroup { get; }
     List<string> Tags { get; }
+
+    /// <summary>
+    /// The position constraint for this room in the layout.
+    /// </summary>
+    PositionConstraint PositionConstraint { get; }
 }
```

### 4.3 修改：LayoutNode

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/Graphs/LayoutNode.cs`

```diff
+    /// <inheritdoc/>
+    [DataMember(Order = 7)]
+    public PositionConstraint PositionConstraint { get; set; } = PositionConstraint.None;

     // Copy 构造函数中：
+    PositionConstraint = other.PositionConstraint;

     // 新增链式设置方法：
+    public LayoutNode SetPositionConstraint(PositionConstraint value)
+    {
+        PositionConstraint = value;
+        return this;
+    }
```

### 4.4 修改：LayoutEdge

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/Graphs/LayoutEdge.cs`

```diff
+    /// <inheritdoc/>
+    [DataMember(Order = 12)]
+    public PositionConstraint PositionConstraint { get; set; } = PositionConstraint.None;

     // Copy 构造函数和 SetProperties 中：
+    PositionConstraint = other.PositionConstraint;
```

### 4.5 修改 LayoutGenerator：软约束（候选排序）

在 `LayoutGenerator` 类中新增方法：

```csharp
/// <summary>
/// Sorts configurations to prefer positions that satisfy the constraint.
/// This is a soft constraint - reorders but does not reject candidates.
/// </summary>
private static void SortByPositionConstraint(List<Configuration> configurations,
    Vector2DInt fromPosition, PositionConstraint constraint)
{
    switch (constraint)
    {
        case PositionConstraint.Bottom:
            // Position.X 大 = 行号大 = 画面下方 → 降序
            configurations.Sort((a, b) =>
                (b.Position.X + fromPosition.X).CompareTo(a.Position.X + fromPosition.X));
            break;

        case PositionConstraint.Top:
        case PositionConstraint.TopEdge:
            // Position.X 小 = 行号小 = 画面上方 → 升序
            configurations.Sort((a, b) =>
                (a.Position.X + fromPosition.X).CompareTo(b.Position.X + fromPosition.X));
            break;
    }
}
```

修改 `AddRoom`（第397-433行）：

```diff
     foreach (var entry in GetTemplateGroupEntries(source.TemplateGroup))
     {
-        foreach (var config in GetConfigurations(fromRoom.Template, entry.Template))
+        var configs = GetConfigurations(fromRoom.Template, entry.Template);
+
+        if (source.PositionConstraint != PositionConstraint.None)
+            SortByPositionConstraint(configs, fromRoom.Position.To2D(), source.PositionConstraint);
+
+        foreach (var config in configs)
         {
```

修改 `InsertRoom`（第445-501行）同理：

```diff
     foreach (var entry in GetTemplateGroupEntries(source.TemplateGroup))
     {
-        foreach (var config1 in GetConfigurations(backRoom.Template, entry.Template))
+        var configs1 = GetConfigurations(backRoom.Template, entry.Template);
+
+        if (source.PositionConstraint != PositionConstraint.None)
+            SortByPositionConstraint(configs1, backRoom.Position.To2D(), source.PositionConstraint);
+
+        foreach (var config1 in configs1)
         {
```

### 4.6 修改 LayoutGenerator：硬约束（最终验证）

```csharp
/// <summary>
/// Validates all position constraints after layout generation completes.
/// </summary>
private bool ValidatePositionConstraints()
{
    if (Layout.Rooms.Count == 0)
        return true;

    // 计算布局行范围
    var minRow = int.MaxValue;
    var maxRow = int.MinValue;

    foreach (var room in Layout.Rooms.Values)
    {
        minRow = Math.Min(minRow, room.Position.X);
        maxRow = Math.Max(maxRow, room.Position.X + room.Template.Cells.Rows - 1);
    }

    var midRow = minRow + (maxRow - minRow + 1) / 2;

    foreach (var room in Layout.Rooms.Values)
    {
        var constraint = GetNodePositionConstraint(room);
        if (constraint == PositionConstraint.None)
            continue;

        var roomMinRow = room.Position.X;
        var roomMaxRow = room.Position.X + room.Template.Cells.Rows - 1;

        switch (constraint)
        {
            case PositionConstraint.Bottom:
                if (roomMaxRow < midRow) return false;
                break;
            case PositionConstraint.Top:
                if (roomMinRow > midRow) return false;
                break;
            case PositionConstraint.TopEdge:
                if (roomMinRow > midRow) return false;
                if (!IsOnLayoutEdge(room)) return false;
                break;
        }
    }

    return true;
}

/// <summary>
/// Gets the PositionConstraint for a room by looking up its source node.
/// Returns None for edge-generated rooms or if not found.
/// </summary>
private PositionConstraint GetNodePositionConstraint(Room room)
{
    // room.Id.A = node ID for node rooms, check if it's a graph node
    if (Graph.NodeDictionary.TryGetValue(room.Id.A, out var node))
        return node.PositionConstraint;
    return PositionConstraint.None;
}

/// <summary>
/// Returns true if the room is on the edge of the layout
/// (at least one side has no adjacent room).
/// </summary>
private bool IsOnLayoutEdge(Room target)
{
    var tMinR = target.Position.X;
    var tMaxR = target.Position.X + target.Template.Cells.Rows;
    var tMinC = target.Position.Y;
    var tMaxC = target.Position.Y + target.Template.Cells.Columns;

    bool hasN = false, hasS = false, hasW = false, hasE = false;

    foreach (var other in Layout.Rooms.Values)
    {
        if (other.Id == target.Id || other.Position.Z != target.Position.Z)
            continue;

        var oMinR = other.Position.X;
        var oMaxR = other.Position.X + other.Template.Cells.Rows;
        var oMinC = other.Position.Y;
        var oMaxC = other.Position.Y + other.Template.Cells.Columns;

        bool colOverlap = oMinC < tMaxC && oMaxC > tMinC;
        bool rowOverlap = oMinR < tMaxR && oMaxR > tMinR;

        if (colOverlap && oMaxR <= tMinR) hasN = true;
        if (colOverlap && oMinR >= tMaxR) hasS = true;
        if (rowOverlap && oMaxC <= tMinC) hasW = true;
        if (rowOverlap && oMinC >= tMaxC) hasE = true;
    }

    return !hasN || !hasS || !hasW || !hasE;
}
```

在 `Generate` 方法的完成检查中调用：

```diff
     if (Layout.IsComplete(TemplateGroups))
     {
+        if (!ValidatePositionConstraints())
+        {
+            ChainIndex = 0;
+            layouts.Clear();
+            layouts.Push(baseLayout);
+            logger?.Invoke("[Layout Generator] Position constraints not satisfied. Restarting...");
+            continue;
+        }
+
         Layout = new Layout(Layout);
         logger?.Invoke("[Layout Generator] Layout generator complete.");
         return Layout;
     }
```

---

## 五、扩展2：最小距离约束

### 5.1 新增：DistanceConstraint 数据类

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/DistanceConstraint.cs`（新建）

```csharp
using System.Runtime.Serialization;

namespace MPewsey.ManiaMap
{
    /// <summary>
    /// Defines a minimum Manhattan distance constraint between two tagged rooms.
    /// Distance is measured in cell grid units between room positions.
    /// </summary>
    [DataContract(Namespace = Constants.DataContractNamespace)]
    public class DistanceConstraint
    {
        /// <summary>
        /// Tag of the first room group (e.g. "spawn").
        /// </summary>
        [DataMember(Order = 1)]
        public string TagA { get; set; }

        /// <summary>
        /// Tag of the second room group (e.g. "treasure").
        /// </summary>
        [DataMember(Order = 2)]
        public string TagB { get; set; }

        /// <summary>
        /// Minimum Manhattan distance in cell grid units.
        /// distance = |roomA.Position.X - roomB.Position.X|
        ///          + |roomA.Position.Y - roomB.Position.Y|
        /// </summary>
        [DataMember(Order = 3)]
        public int MinDistance { get; set; }

        public DistanceConstraint() { }

        public DistanceConstraint(string tagA, string tagB, int minDistance)
        {
            TagA = tagA;
            TagB = tagB;
            MinDistance = minDistance;
        }
    }
}
```

### 5.2 修改 LayoutGenerator：距离约束支持

给 `LayoutGenerator` 添加距离约束列表属性和构造函数参数：

```diff
 public class LayoutGenerator : IPipelineStep
 {
     public int MaxRebases { ... }
     public float RebaseDecayRate { ... }
     public int MaxBranchLength { get; set; }
+
+    /// <summary>
+    /// Preferred maximum map width in cell columns. 0 or negative = no limit.
+    /// This is a soft limit - the generator prefers to stay within bounds
+    /// but allows overflow up to MapSizeTolerance cells if needed.
+    /// </summary>
+    public int MaxMapWidth { get; set; }
+
+    /// <summary>
+    /// Preferred maximum map height in cell rows. 0 or negative = no limit.
+    /// This is a soft limit - the generator prefers to stay within bounds
+    /// but allows overflow up to MapSizeTolerance cells if needed.
+    /// </summary>
+    public int MaxMapHeight { get; set; }
+
+    /// <summary>
+    /// Number of cells a room is allowed to exceed MaxMapWidth/MaxMapHeight.
+    /// For example, if MaxMapHeight=60 and MapSizeTolerance=5, a layout up
+    /// to 65 rows is still accepted. Default = 3.
+    /// </summary>
+    public int MapSizeTolerance { get; set; }
+
+    /// <summary>
+    /// List of minimum distance constraints between tagged rooms.
+    /// </summary>
+    public List<DistanceConstraint> DistanceConstraints { get; set; }

     // ... 现有私有字段不变 ...

-    public LayoutGenerator(int maxRebases = 100, float rebaseDecayRate = 0.25f, int maxBranchLength = -1)
+    public LayoutGenerator(int maxRebases = 100, float rebaseDecayRate = 0.25f,
+        int maxBranchLength = -1, int maxMapWidth = 0, int maxMapHeight = 0,
+        int mapSizeTolerance = 3, List<DistanceConstraint> distanceConstraints = null)
     {
         MaxRebases = maxRebases;
         RebaseDecayRate = rebaseDecayRate;
         MaxBranchLength = maxBranchLength;
+        MaxMapWidth = maxMapWidth;
+        MaxMapHeight = maxMapHeight;
+        MapSizeTolerance = mapSizeTolerance;
+        DistanceConstraints = distanceConstraints ?? new List<DistanceConstraint>();
     }
```

### 5.3 地图尺寸控制：软排序 + 宽容验证

地图尺寸采用**软限制**策略，不在 `AddRoom` 中硬拒绝超出边界的候选：

> **为什么不硬拒绝？** 假设 MaxMapHeight=60，当前布局已到第 58 行，需要接一个 3×3 房间连接撤离房。如果硬拒绝，这个 3×3 放不下，生成失败。但实际上超出 1-2 格完全可接受，不应因此导致生成失败率飙升。

#### 5.3.1 在 AddRoom 中：对候选按"越界程度"排序（优先不越界）

在已有的位置约束排序之后，追加一个尺寸偏好排序。越界少的候选排在前面，但越界多的也不会被丢弃：

```csharp
/// <summary>
/// Calculates how many cells the layout would overflow the preferred bounds
/// if a template were placed at the given position. Returns 0 if within bounds.
/// </summary>
private int CalculateBoundsOverflow(RoomTemplate template, Vector2DInt position)
{
    if (MaxMapWidth <= 0 && MaxMapHeight <= 0)
        return 0;

    var minRow = position.X;
    var maxRow = position.X + template.Cells.Rows - 1;
    var minCol = position.Y;
    var maxCol = position.Y + template.Cells.Columns - 1;

    foreach (var room in Layout.Rooms.Values)
    {
        minRow = Math.Min(minRow, room.Position.X);
        maxRow = Math.Max(maxRow, room.Position.X + room.Template.Cells.Rows - 1);
        minCol = Math.Min(minCol, room.Position.Y);
        maxCol = Math.Max(maxCol, room.Position.Y + room.Template.Cells.Columns - 1);
    }

    int overflow = 0;
    if (MaxMapHeight > 0)
        overflow += Math.Max(0, (maxRow - minRow + 1) - MaxMapHeight);
    if (MaxMapWidth > 0)
        overflow += Math.Max(0, (maxCol - minCol + 1) - MaxMapWidth);
    return overflow;
}
```

在 `AddRoom` 中应用：

```diff
     foreach (var entry in GetTemplateGroupEntries(source.TemplateGroup))
     {
         var configs = GetConfigurations(fromRoom.Template, entry.Template);

         if (source.PositionConstraint != PositionConstraint.None)
             SortByPositionConstraint(configs, fromRoom.Position.To2D(), source.PositionConstraint);

+        // 优先选择不越界的候选，但不丢弃越界的
+        if (MaxMapWidth > 0 || MaxMapHeight > 0)
+        {
+            configs.Sort((a, b) =>
+            {
+                var posA = a.Position + fromRoom.Position.To2D();
+                var posB = b.Position + fromRoom.Position.To2D();
+                return CalculateBoundsOverflow(entry.Template, posA)
+                    .CompareTo(CalculateBoundsOverflow(entry.Template, posB));
+            });
+        }

         foreach (var config in configs)
         {
             var position = config.Position + fromRoom.Position.To2D();

             if (!config.Matches(z, code, direction))
                 continue;
             if (Layout.Intersects(entry.Template, position, source.Z))
                 continue;
-            if (!WithinMapBounds(entry.Template, position))
-                continue;

             // 不拒绝越界候选，只是排序让不越界的优先
             var room = new Room(source, position, entry.Template, RandomSeed.Next());
```

> **注意排序顺序：** 如果同时有位置约束和尺寸偏好，先按位置约束排序（主排序），再按越界程度排序（次排序）。由于 C# `List.Sort` 是稳定排序（TimSort），第二次排序不会打乱第一次的相对顺序——**前提是越界程度相同时保持原序**。实际上这里两个排序的 key 不同，可以合并为一个组合排序以确保正确性：

```csharp
// 合并排序：位置约束优先级 > 越界程度
if (source.PositionConstraint != PositionConstraint.None || MaxMapWidth > 0 || MaxMapHeight > 0)
{
    var constraint = source.PositionConstraint;
    configs.Sort((a, b) =>
    {
        // 主排序：位置约束
        if (constraint != PositionConstraint.None)
        {
            var rowA = a.Position.X + fromRoom.Position.To2D().X;
            var rowB = b.Position.X + fromRoom.Position.To2D().X;
            int cmp = constraint == PositionConstraint.Bottom
                ? rowB.CompareTo(rowA)   // Bottom: 大行号优先
                : rowA.CompareTo(rowB);  // Top/TopEdge: 小行号优先
            if (cmp != 0) return cmp;
        }
        // 次排序：越界程度
        var posA = a.Position + fromRoom.Position.To2D();
        var posB = b.Position + fromRoom.Position.To2D();
        return CalculateBoundsOverflow(entry.Template, posA)
            .CompareTo(CalculateBoundsOverflow(entry.Template, posB));
    });
}
```

#### 5.3.2 最终验证：宽容的尺寸检查

生成完成后，检查总尺寸是否在 `Max + Tolerance` 范围内：

```csharp
/// <summary>
/// Validates that the layout size is within the preferred bounds plus tolerance.
/// Returns true if no bounds are configured, or if the overflow is within tolerance.
/// </summary>
private bool ValidateMapSize()
{
    if (MaxMapWidth <= 0 && MaxMapHeight <= 0)
        return true;

    var bounds = Layout.GetBounds();
    // Layout.GetBounds() returns RectangleInt(Y=minCol, X=minRow, Width=cols, Height=rows)

    if (MaxMapHeight > 0 && bounds.Height > MaxMapHeight + MapSizeTolerance)
        return false;
    if (MaxMapWidth > 0 && bounds.Width > MaxMapWidth + MapSizeTolerance)
        return false;

    return true;
}
```

在 `Generate` 完成检查中：

```diff
     if (Layout.IsComplete(TemplateGroups))
     {
         if (!ValidatePositionConstraints()) { ... restart ... }
         if (!ValidateDistanceConstraints()) { ... restart ... }
+        if (!ValidateMapSize())
+        {
+            ChainIndex = 0;
+            layouts.Clear();
+            layouts.Push(baseLayout);
+            logger?.Invoke("[Layout Generator] Map size exceeded tolerance. Restarting...");
+            continue;
+        }

         Layout = new Layout(Layout);
```

#### 5.3.3 效果示意

```
MaxMapHeight = 60, MapSizeTolerance = 3

情况1: 布局 58 行 → ✓ 在限制内
情况2: 布局 61 行 → ✓ 超出 1 行，在容差 3 内，接受
情况3: 布局 63 行 → ✓ 超出 3 行，刚好在容差内，接受
情况4: 布局 64 行 → ✗ 超出 4 行，超过容差，重启生成

日常场景：
  软排序让大部分房间优先选择不越界的位置
  → 最终布局通常在 60 行以内
  → 偶尔有个大房间超出 1-2 格，容差允许，不重试
  → 只有严重超标才重试
```

### 5.4 最终验证：距离约束

在 `ValidatePositionConstraints` 之后调用：

```csharp
/// <summary>
/// Validates that all distance constraints between tagged rooms are satisfied.
/// Distance is Manhattan distance between room positions in cell grid units.
/// </summary>
private bool ValidateDistanceConstraints()
{
    if (DistanceConstraints == null || DistanceConstraints.Count == 0)
        return true;

    foreach (var constraint in DistanceConstraints)
    {
        var roomsA = Layout.FindRoomsWithTag(constraint.TagA);
        var roomsB = Layout.FindRoomsWithTag(constraint.TagB);

        foreach (var roomA in roomsA)
        {
            foreach (var roomB in roomsB)
            {
                if (roomA.Id == roomB.Id)
                    continue;

                var distance = Math.Abs(roomA.Position.X - roomB.Position.X)
                             + Math.Abs(roomA.Position.Y - roomB.Position.Y);

                if (distance < constraint.MinDistance)
                    return false;
            }
        }
    }

    return true;
}
```

在 `Generate` 方法完成检查中：

```diff
     if (Layout.IsComplete(TemplateGroups))
     {
         if (!ValidatePositionConstraints())
         {
             // ... restart ...
             continue;
         }
+
+        if (!ValidateDistanceConstraints())
+        {
+            ChainIndex = 0;
+            layouts.Clear();
+            layouts.Push(baseLayout);
+            logger?.Invoke("[Layout Generator] Distance constraints not satisfied. Restarting...");
+            continue;
+        }

         Layout = new Layout(Layout);
```

---

## 六、扩展3：Godot 侧配置暴露

### 6.1 修改 LayoutGeneratorStep

**文件：** `/d/UGit/ManiaMapGodot/addons/mpewsey.maniamap/scripts/runtime/generators/LayoutGeneratorStep.cs`

```diff
 [Tool]
 [GlobalClass]
 public partial class LayoutGeneratorStep : GenerationStep
 {
     [Export(PropertyHint.Range, "1,100,1,or_greater")] public int MaxRebases { get; set; } = 100;
     [Export(PropertyHint.Range, "0,1,0.05,or_greater")] public float RebaseDecayRate { get; set; } = 0.25f;
     [Export(PropertyHint.Range, "-1,10,1,or_greater")] public int MaxBranchLength { get; set; } = -1;
+
+    /// <summary>
+    /// Preferred maximum map width in cell columns. 0 = no limit.
+    /// Soft limit: generator prefers to stay within but allows minor overflow.
+    /// </summary>
+    [ExportGroup("Map Constraints")]
+    [Export(PropertyHint.Range, "0,200,1")] public int MaxMapWidth { get; set; } = 0;
+
+    /// <summary>
+    /// Preferred maximum map height in cell rows. 0 = no limit.
+    /// </summary>
+    [Export(PropertyHint.Range, "0,200,1")] public int MaxMapHeight { get; set; } = 0;
+
+    /// <summary>
+    /// How many cells a room is allowed to exceed Max size before rejecting.
+    /// E.g. MaxMapHeight=60, Tolerance=3 → layouts up to 63 rows accepted.
+    /// </summary>
+    [Export(PropertyHint.Range, "0,10,1")] public int MapSizeTolerance { get; set; } = 3;
+
+    /// <summary>
+    /// Distance constraints between tagged rooms.
+    /// Each entry: TagA, TagB, MinDistance (Manhattan distance in cells).
+    /// </summary>
+    [Export] public Godot.Collections.Array<Godot.Collections.Dictionary> DistanceConstraints { get; set; } = new();

     public override IPipelineStep CreateStep()
     {
-        return new LayoutGenerator(MaxRebases, RebaseDecayRate, MaxBranchLength);
+        var constraints = new List<DistanceConstraint>();
+        foreach (var dict in DistanceConstraints)
+        {
+            constraints.Add(new DistanceConstraint(
+                dict["TagA"].AsString(),
+                dict["TagB"].AsString(),
+                dict["MinDistance"].AsInt32()));
+        }
+        return new LayoutGenerator(MaxRebases, RebaseDecayRate, MaxBranchLength,
+            MaxMapWidth, MaxMapHeight, MapSizeTolerance, constraints);
     }
 }
```

### 6.2 修改 LayoutGraphNode

**文件：** `/d/UGit/ManiaMapGodot/addons/mpewsey.maniamap/scripts/runtime/graphs/LayoutGraphNode.cs`

```diff
+using MPewsey.ManiaMap;
 // ...
     [Tool]
     public partial class LayoutGraphNode : Resource
     {
         // ... 现有属性 ...

+        private PositionConstraint _positionConstraint;
+        /// <summary>
+        /// The position constraint for this room in the layout.
+        /// </summary>
+        [Export] public PositionConstraint PositionConstraint
+        {
+            get => _positionConstraint;
+            set => SetField(ref _positionConstraint, value);
+        }

         public void AddMMLayoutNode(LayoutGraph graph)
         {
             var node = graph.AddNode(Id);
             node.Name = Name;
             node.Color = ColorUtility.ConvertColorToColor4(Color);
             node.TemplateGroup = TemplateGroup.Name;
             node.Z = Z;
             node.Tags = new List<string>(Tags);
+            node.PositionConstraint = PositionConstraint;

             if (!string.IsNullOrWhiteSpace(VariationGroup))
                 graph.AddNodeVariation(VariationGroup, node.Id);
         }
     }
```

### 6.3 修改 ManiaMap.Godot.csproj

**文件：** `/d/UGit/ManiaMapGodot/ManiaMap.Godot.csproj`

```diff
   <ItemGroup>
     <PackageReference Include="gdUnit4.api" Version="4.2.3" />
-    <PackageReference Include="MPewsey.ManiaMap" Version="2.5.3" />
+    <ProjectReference Include="..\ManiaMap\src\ManiaMap\ManiaMap.csproj" />
   </ItemGroup>
```

---

## 七、扩展4：重要房间可视化标识

### 7.1 思路

ManiaMap 已有的数据通路：`LayoutNode.Tags` → `Room.Tags` + `LayoutNode.Color` → `Room.Color`。

生成完成后，遍历所有已实例化的 `RoomNode2D`，根据 `Room.Tags` 添加标记 Label。

### 7.2 修改 RoomLayout2DSample

**文件：** `/d/UGit/ManiaMapGodot/samples/scripts/RoomLayout2DSample.cs`

```diff
 using Godot;
 using MPewsey.ManiaMap;
 using MPewsey.ManiaMapGodot.Generators;
+using System.Collections.Generic;

 namespace MPewsey.ManiaMapGodot.Samples
 {
     [GlobalClass]
     public partial class RoomLayout2DSample : Node
     {
         [Export] public Camera2DController Camera { get; set; }
         [Export] public Node2D Container { get; set; }
         [Export] public GenerationPipeline Pipeline { get; set; }
         [Export] public Button GenerateButton { get; set; }
         [Export] public RichTextLabel MessageLabel { get; set; }
         [Export] public RoomTemplateDatabase RoomTemplateDatabase { get; set; }
         [Export] public Vector2 CellSize { get; set; } = new Vector2(96, 96);

+        // 标签到显示名称和颜色的映射
+        private static readonly Dictionary<string, (string Label, Color Color)> ImportantTags = new()
+        {
+            { "spawn",      ("出生",  new Color(0.2f, 0.8f, 0.2f)) },   // 绿色
+            { "extraction", ("撤离",  new Color(0.8f, 0.2f, 0.2f)) },   // 红色
+            { "boss",       ("BOSS",  new Color(0.8f, 0.1f, 0.8f)) },   // 紫色
+            { "treasure",   ("宝箱",  new Color(1.0f, 0.8f, 0.0f)) },   // 金色
+        };

         // ... _Ready, ClearContainer, OnGenerateButtonPressed 不变 ...

         private async void GenerateLayoutAsync()
         {
             MessageLabel.Text = "Generating...";
             GenerateButton.Disabled = true;
             var seed = Rand.Random.Next(1, int.MaxValue);
             var results = await Pipeline.RunAttemptsAsync(seed);
             GenerateButton.Disabled = false;

             if (!results.Success)
             {
                 MessageLabel.Text = $"[color=#ff0000]Generation FAILED (Seed = {seed})[/color]";
                 return;
             }

             MessageLabel.Text = string.Empty;
             var layout = results.GetOutput<Layout>("Layout");
             var layoutPack = new LayoutPack(layout, new LayoutState(layout), new ManiaMapSettings());
             ClearContainer();
-            RoomTemplateDatabase.CreateRoom2DInstances(Container, layoutPack);
+            var rooms = RoomTemplateDatabase.CreateRoom2DInstances(Container, layoutPack);
+            AddRoomLabels(rooms, layout);
             Camera.CenterCameraView(layout, CellSize);
         }

+        /// <summary>
+        /// Adds colored labels to important rooms based on their tags.
+        /// </summary>
+        private void AddRoomLabels(List<RoomNode2D> roomNodes, Layout layout)
+        {
+            foreach (var roomNode in roomNodes)
+            {
+                if (!roomNode.IsInitialized)
+                    continue;
+
+                var room = roomNode.RoomLayout;
+
+                foreach (var tag in room.Tags)
+                {
+                    if (ImportantTags.TryGetValue(tag, out var info))
+                    {
+                        var label = new Label();
+                        label.Text = info.Label;
+                        label.AddThemeColorOverride("font_color", info.Color);
+                        label.AddThemeFontSizeOverride("font_size", 24);
+                        label.HorizontalAlignment = HorizontalAlignment.Center;
+                        label.VerticalAlignment = VerticalAlignment.Center;
+                        // 放在房间中心
+                        var rows = room.Template.Cells.Rows;
+                        var cols = room.Template.Cells.Columns;
+                        label.Position = new Vector2(
+                            cols * CellSize.X * 0.5f - 30,
+                            rows * CellSize.Y * 0.5f - 15);
+                        roomNode.AddChild(label);
+
+                        // 背景高亮色块
+                        var bg = new ColorRect();
+                        bg.Color = new Color(info.Color.R, info.Color.G, info.Color.B, 0.15f);
+                        bg.Size = new Vector2(cols * CellSize.X, rows * CellSize.Y);
+                        bg.ZIndex = -1;
+                        roomNode.AddChild(bg);
+
+                        break; // 每个房间只显示第一个匹配的标签
+                    }
+                }
+            }
+        }
     }
 }
```

### 7.3 效果示意

生成后的地图中：

```
┌─────────────────┐
│  ┌───┐  ┌───┐   │
│  │   │  │撤离│   │  ← 红色高亮 + "撤离" 标签
│  └─┬─┘  └─┬─┘   │
│    │      │      │
│  ┌─┴──────┴─┐    │
│  │   BOSS   │    │  ← 紫色高亮 + "BOSS" 标签
│  └────┬─────┘    │
│       │          │
│  ┌──┐ ┌──┐       │
│  │  ├─┤  │       │
│  └──┘ └──┘       │
│       │          │
│  ┌────┴─────┐    │
│  │  出生    │    │  ← 绿色高亮 + "出生" 标签
│  └──────────┘    │
└─────────────────┘
```

---

## 八、配置方式汇总

### 8.1 通过布局图编辑器（设计时）

在 Godot 的 LayoutGraph 编辑器中，选中一个节点：

| Inspector 字段 | 作用 | 示例 |
|---------------|------|------|
| `Name` | 房间名称 | "出生房" |
| `PositionConstraint` | 位置约束下拉框 | Bottom / Top / TopEdge |
| `Tags` | 标签数组 | ["spawn"] |
| `TemplateGroup` | 可用模板组 | "SpawnRooms" |
| `Color` | 节点颜色 | 绿色 |

### 8.2 通过 LayoutGeneratorStep（场景 Inspector）

选中 GenerationPipeline → Steps → LayoutGeneratorStep 节点：

| Inspector 字段 | 作用 | 示例 |
|---------------|------|------|
| `Max Map Width` | 地图期望最大列数（软限制） | 50（0=不限） |
| `Max Map Height` | 地图期望最大行数（软限制） | 80（0=不限） |
| `Map Size Tolerance` | 允许超出的格子数 | 3（默认） |
| `Distance Constraints` | 距离约束数组 | 见下表 |

距离约束数组的每个元素是一个 Dictionary：

| 字段 | 类型 | 示例 |
|------|------|------|
| `TagA` | string | "spawn" |
| `TagB` | string | "treasure" |
| `MinDistance` | int | 10 |

### 8.3 重点房间数量

**由布局图中对应节点的数量决定**：
- 需要 2 个出生房 → 图中设 2 个带 `spawn` 标签 + `Bottom` 约束的节点
- 需要 3 个撤离房 → 图中设 3 个带 `extraction` 标签 + `TopEdge` 约束的节点

也可以在代码中动态构建 `LayoutGraph`，根据配置参数决定节点数量。

### 8.4 示例布局图配置

```
节点1: Name="出生房",  Tags=["spawn"],      PositionConstraint=Bottom,  TemplateGroup="SpawnRooms"
节点2: Name="普通房A", Tags=[],             PositionConstraint=None,    TemplateGroup="NormalRooms"
节点3: Name="普通房B", Tags=[],             PositionConstraint=None,    TemplateGroup="NormalRooms"
节点4: Name="宝箱房",  Tags=["treasure"],   PositionConstraint=None,    TemplateGroup="TreasureRooms"
节点5: Name="Boss房",  Tags=["boss"],       PositionConstraint=None,    TemplateGroup="BossRooms"
节点6: Name="撤离房",  Tags=["extraction"], PositionConstraint=TopEdge, TemplateGroup="ExitRooms"

边: 1→2, 2→3, 3→4, 4→5, 5→6, 2→5 (形成环路)

LayoutGeneratorStep:
  MaxMapWidth = 40
  MaxMapHeight = 60
  MapSizeTolerance = 3
  DistanceConstraints = [
    { TagA="spawn", TagB="treasure", MinDistance=10 },
    { TagA="spawn", TagB="extraction", MinDistance=15 }
  ]
```

---

## 九、算法工作原理图解

### 9.1 无约束时（当前行为）

```
配置候选列表（shuffle 后）: [右, 下, 左, 上, 右下, 左上, ...]
                             ↑
                         取第一个合法的 → 方向随机
```

### 9.2 有约束时（修改后）

```
出生房（Bottom）候选排序后: [下, 右下, 左下, 右, 左, 上, ...]
撤离房（TopEdge）候选排序后: [上, 右上, 左上, 右, 左, 下, ...]
普通房间: [shuffle 随机] (不变)

每个候选还需通过：
  ✓ 门兼容
  ✓ 不重叠
  取第一个全部通过的（越界少的排在前面，但越界的也不丢弃）

生成完毕后最终验证：
  ✓ 位置约束（出生在底部？撤离在顶部边缘？）
  ✓ 距离约束（出生离宝箱 ≥ 10 格？）
  ✓ 地图尺寸（在 Max + Tolerance 范围内？）
  任一不通过 → 重启生成
```

---

## 十、风险与成功率分析

### 10.1 软约束成功率

对典型模板（4-8 个候选配置），排序后前 2-4 个满足方向偏好。单房间放置满足方向：**70-90%**。

### 10.2 硬约束重试

- 位置不满足 → 重启，不同随机种子
- 距离不满足 → 重启
- 尺寸超标 → AddRoom 时就 skip，不会到重启
- `RunAttemptsAsync` 默认 10 次，可调大
- **预期 1-5 次即可成功**

### 10.3 向后兼容

- `PositionConstraint` 默认 `None` → 不排序、不验证
- 尺寸限制是**软排序 + 宽容验证**，不硬拒绝 → 不会因大房间卡在边界导致失败
- `MapSizeTolerance` 默认 `3` → 允许少量越界
- `DistanceConstraints` 默认空列表 → 不检查
- **原有测试全部通过，无需修改**

---

## 十一、验证计划

### 11.1 单元测试（ManiaMap）

```csharp
[Test]
public void TestPositionConstraintBottom()
{
    var graph = new LayoutGraph(1, "Test");
    graph.AddNode(1).SetName("Spawn").SetTemplateGroup("Default")
        .SetPositionConstraint(PositionConstraint.Bottom).AddTag("spawn");
    graph.AddNode(2).SetName("Room").SetTemplateGroup("Default");
    graph.AddNode(3).SetName("Exit").SetTemplateGroup("Default")
        .SetPositionConstraint(PositionConstraint.TopEdge).AddTag("extraction");
    graph.AddEdge(1, 2);
    graph.AddEdge(2, 3);

    var generator = new LayoutGenerator();
    int successCount = 0;
    for (int i = 0; i < 100; i++)
    {
        var layout = generator.Generate(1, graph, templateGroups, new RandomSeed(i + 1));
        if (layout != null)
        {
            var spawn = layout.FindRoomWithTag("spawn");
            var exit = layout.FindRoomWithTag("extraction");
            if (spawn.Position.X >= exit.Position.X)
                successCount++;
        }
    }
    Assert.Greater(successCount, 80);
}

[Test]
public void TestDistanceConstraint()
{
    // ... 创建图，设置 spawn 和 treasure 标签 ...
    var constraints = new List<DistanceConstraint>
    {
        new DistanceConstraint("spawn", "treasure", 10)
    };
    var generator = new LayoutGenerator(distanceConstraints: constraints);
    var layout = generator.Generate(1, graph, templateGroups, new RandomSeed(42));
    Assert.NotNull(layout);

    var spawn = layout.FindRoomWithTag("spawn");
    var treasure = layout.FindRoomWithTag("treasure");
    var dist = Math.Abs(spawn.Position.X - treasure.Position.X)
             + Math.Abs(spawn.Position.Y - treasure.Position.Y);
    Assert.GreaterOrEqual(dist, 10);
}

[Test]
public void TestMapSizeSoftLimit()
{
    // Tolerance=3 means up to maxHeight+3 is accepted
    var generator = new LayoutGenerator(maxMapWidth: 20, maxMapHeight: 20, mapSizeTolerance: 3);
    var layout = generator.Generate(1, graph, templateGroups, new RandomSeed(42));
    Assert.NotNull(layout);

    var bounds = layout.GetBounds();
    // Soft limit: actual size may exceed max by up to tolerance
    Assert.LessOrEqual(bounds.Width, 20 + 3);
    Assert.LessOrEqual(bounds.Height, 20 + 3);
}
```

### 11.2 Godot 集成测试

1. 修改 `ManiaMap.Godot.csproj` 指向本地项目
2. `dotnet build` 确认编译通过
3. Godot 中创建布局图，设置：
   - 出生节点：Tags=["spawn"], PositionConstraint=Bottom
   - 撤离节点：Tags=["extraction"], PositionConstraint=TopEdge
4. LayoutGeneratorStep 设置 MaxMapWidth=50, MaxMapHeight=80, MapSizeTolerance=3
5. 添加 DistanceConstraint: spawn ↔ extraction MinDistance=15
6. 运行 room_layout_2d_sample，验证：
   - 出生房有绿色 "出生" 标签且在底部 ✓
   - 撤离房有红色 "撤离" 标签且在顶部边缘 ✓
   - 地图尺寸不超标 ✓
   - 多次生成结果不同但均满足约束 ✓

### 11.3 回归测试

```bash
cd /d/UGit/ManiaMap && dotnet test
```
