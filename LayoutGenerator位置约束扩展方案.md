# LayoutGenerator 位置约束扩展方案

## 一、目标

在 ManiaMap 核心库的 `LayoutGenerator` 算法中增加**房间位置约束**能力，使得：

1. **出生房**始终生成在地图**底部**（行号最大的区域）
2. **撤离房**始终生成在地图**上半部的边缘位置**
3. 整体地图呈现**从下往上**的探索趋势

## 二、现有算法流程分析

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

### 2.2 单个房间放置流程

```
AddChain(chain)
  └── 逐边遍历：
        ├── CanInsertRoom? → InsertRooms()    // 插入到两个已有房间之间
        └── else → AddRooms(edge)             // 从已有房间延伸
              ├── AddFirstRoom(fromNode)       // 若起始房不存在
              │     └── position = Vector2DInt.Zero (0,0) ← ★ 固定原点
              └── AddRoom(toNode, fromRoomId, ...) // 放置新房间
```

### 2.3 AddRoom 核心逻辑（第397-433行）

这是最关键的扩展点：

```csharp
private bool AddRoom(IRoomSource source, Uid fromRoomId, DoorCode code, EdgeDirection direction)
{
    var fromRoom = Layout.Rooms[fromRoomId];
    var z = source.Z - fromRoom.Position.Z;

    // 外层循环：遍历候选模板（已 shuffle）
    foreach (var entry in GetTemplateGroupEntries(source.TemplateGroup))
    {
        // 内层循环：遍历候选配置/位置（已 shuffle）
        foreach (var config in GetConfigurations(fromRoom.Template, entry.Template))
        {
            var position = config.Position + fromRoom.Position.To2D();

            // 检查1：门方向/门代码兼容
            if (!config.Matches(z, code, direction))
                continue;
            // 检查2：不与已有房间重叠
            if (Layout.Intersects(entry.Template, position, source.Z))
                continue;

            // ★ 第一个通过两项检查的配置就直接采用！
            // ★ 没有任何位置偏好/约束
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

### 2.4 ConfigurationSpace 是什么

`ConfigurationSpace` 预计算了两个模板之间**所有可能的门对门连接方式**：

```csharp
// ConfigurationSpace.FindConfigurations()
for (int i = -ToTemplate.Rows; i <= FromTemplate.Rows; i++)
    for (int j = -ToTemplate.Columns; j <= FromTemplate.Columns; j++)
        // 在偏移 (i,j) 处找到所有对齐的门对
        foreach (var pair in FromTemplate.AlignedDoors(ToTemplate, position))
            Configurations.Add(new Configuration(position, pair.FromDoor, pair.ToDoor));
```

每个 `Configuration` 包含：
- `Position` (Vector2DInt)：新房间相对于已有房间的偏移量
- `FromDoor` / `ToDoor`：匹配的门对
- `EdgeDirection`：门对应的方向

在 `AddRoom` 中，`GetConfigurations()` 会对这个列表做 **shuffle**（随机打乱），所以当前放置方向完全随机。

### 2.5 坐标系统

```
Position.X = 行（Row）   → 对应 Godot Y 轴，X 越大越靠下
Position.Y = 列（Column）→ 对应 Godot X 轴

Godot 映射（RoomNode2D.MoveToLayoutPosition）：
  Godot.X = CellSize.X × Position.Y（列）
  Godot.Y = CellSize.Y × Position.X（行）

所以：Position.X 越大 = Godot Y 越大 = 画面越靠下 = 地图底部
```

---

## 三、修改方案

### 3.1 整体思路

采用**软约束（排序优先）+ 硬约束（最终验证）**的组合策略：

1. **软约束**：在 `AddRoom` 中，对候选配置列表按位置约束排序，使算法**优先尝试**满足约束的方向
2. **硬约束**：在生成完成后（`IsComplete` 之后），验证位置约束是否满足，不满足则重启

软约束大幅提高首次命中率，硬约束兜底确保最终结果正确。

### 3.2 涉及的文件清单

#### ManiaMap 核心库 (`/d/UGit/ManiaMap/src/ManiaMap/`)

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `PositionConstraint.cs` | **新增** | 位置约束枚举定义 |
| `IRoomSource.cs` | 修改 | 接口添加 `PositionConstraint` 属性 |
| `Graphs/LayoutNode.cs` | 修改 | 实现 `PositionConstraint` 属性 |
| `Graphs/LayoutEdge.cs` | 修改 | 实现 `PositionConstraint` 属性（默认 None） |
| `Generators/LayoutGenerator.cs` | 修改 | 核心：排序逻辑 + 验证逻辑 |

#### ManiaMap.Godot 封装 (`/d/UGit/ManiaMapGodot/addons/mpewsey.maniamap/`)

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `ManiaMap.Godot.csproj` | 修改 | NuGet 引用 → 本地项目引用 |
| `scripts/runtime/graphs/LayoutGraphNode.cs` | 修改 | 编辑器暴露 PositionConstraint |

---

## 四、详细修改内容

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
        /// When used as the first room in a chain, it will be placed at a positive
        /// row offset instead of (0,0).
        /// </summary>
        Bottom = 1,

        /// <summary>
        /// Room should be placed toward the top of the map (low row values).
        /// </summary>
        Top = 2,

        /// <summary>
        /// Room should be placed at the top-half edge of the map.
        /// The generator will prefer positions with low row values,
        /// and the final validation will verify the room is on the layout boundary.
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
+    /// Defaults to None.
+    /// </summary>
+    PositionConstraint PositionConstraint { get; }
 }
```

### 4.3 修改：LayoutNode

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/Graphs/LayoutNode.cs`

```diff
 [DataContract(Namespace = Constants.DataContractNamespace)]
 public class LayoutNode : IRoomSource, IValueHashMapEntry<int>
 {
     [DataMember(Order = 1)]
     public int Id { get; set; }

     [DataMember(Order = 2)]
     public string Name { get; set; } = string.Empty;

     [DataMember(Order = 3)]
     public int Z { get; set; }

     [DataMember(Order = 4)]
     public string TemplateGroup { get; set; } = "Default";

     [DataMember(Order = 5)]
     public Color4 Color { get; set; } = new Color4(25, 25, 112, 255);

     [DataMember(Order = 6)]
     public List<string> Tags { get; set; } = new List<string>();

+    /// <inheritdoc/>
+    [DataMember(Order = 7)]
+    public PositionConstraint PositionConstraint { get; set; } = PositionConstraint.None;

     public Uid RoomId { get => new Uid(Id); }

     // ... 现有代码 ...

     public LayoutNode(int id)
     {
         Id = id;
     }

     private LayoutNode(LayoutNode other)
     {
         Id = other.Id;
         Name = other.Name;
         Z = other.Z;
         TemplateGroup = other.TemplateGroup;
         Color = other.Color;
         Tags = new List<string>(other.Tags);
+        PositionConstraint = other.PositionConstraint;
     }

+    /// <summary>
+    /// Sets the position constraint of the node and returns the node.
+    /// </summary>
+    public LayoutNode SetPositionConstraint(PositionConstraint value)
+    {
+        PositionConstraint = value;
+        return this;
+    }
 }
```

### 4.4 修改：LayoutEdge

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/Graphs/LayoutEdge.cs`

LayoutEdge 也实现了 `IRoomSource`（边上可以生成过渡房间），需要加上属性：

```diff
 [DataContract(Namespace = Constants.DataContractNamespace)]
 public class LayoutEdge : IRoomSource, IValueHashMapEntry<EdgeIndexes>
 {
     // ... 现有属性 ...

+    /// <inheritdoc/>
+    [DataMember(Order = 12)]
+    public PositionConstraint PositionConstraint { get; set; } = PositionConstraint.None;

     private LayoutEdge(LayoutEdge other)
     {
         Name = other.Name;
         FromNode = other.FromNode;
         ToNode = other.ToNode;
         Direction = other.Direction;
         DoorCode = other.DoorCode;
         Z = other.Z;
         RoomChance = other.RoomChance;
         Color = other.Color;
         TemplateGroup = other.TemplateGroup;
         Tags = new List<string>(other.Tags);
+        PositionConstraint = other.PositionConstraint;
     }

     public void SetProperties(LayoutEdge other)
     {
         Name = other.Name;
         Direction = other.Direction;
         DoorCode = other.DoorCode;
         Z = other.Z;
         RoomChance = other.RoomChance;
         Color = other.Color;
         TemplateGroup = other.TemplateGroup;
         Tags = new List<string>(other.Tags);
+        PositionConstraint = other.PositionConstraint;
     }
 }
```

### 4.5 修改：LayoutGenerator（核心）

**文件：** `/d/UGit/ManiaMap/src/ManiaMap/Generators/LayoutGenerator.cs`

#### 4.5.1 添加辅助方法：对候选配置按约束排序

在 `LayoutGenerator` 类中添加以下方法：

```csharp
/// <summary>
/// Sorts the configuration list based on the position constraint of the target room.
/// This is a soft constraint - it reorders candidates to prefer positions that
/// satisfy the constraint, but does not reject any candidates.
/// </summary>
/// <param name="configurations">The list of configurations to sort.</param>
/// <param name="fromPosition">The position of the existing room.</param>
/// <param name="constraint">The position constraint to apply.</param>
private static void SortByPositionConstraint(List<Configuration> configurations,
    Vector2DInt fromPosition, PositionConstraint constraint)
{
    switch (constraint)
    {
        case PositionConstraint.Bottom:
            // 优先选择 Position.X（行号）更大的配置 → 放在地图下方
            // Position.X 大 = 行号大 = Godot Y 大 = 画面下方
            configurations.Sort((a, b) =>
            {
                var posA = a.Position.X + fromPosition.X;
                var posB = b.Position.X + fromPosition.X;
                return posB.CompareTo(posA); // 降序：大行号优先
            });
            break;

        case PositionConstraint.Top:
            // 优先选择 Position.X（行号）更小的配置 → 放在地图上方
            configurations.Sort((a, b) =>
            {
                var posA = a.Position.X + fromPosition.X;
                var posB = b.Position.X + fromPosition.X;
                return posA.CompareTo(posB); // 升序：小行号优先
            });
            break;

        case PositionConstraint.TopEdge:
            // 优先选择 Position.X（行号）更小的配置 → 放在地图上方
            // 边缘检查在最终验证中完成
            configurations.Sort((a, b) =>
            {
                var posA = a.Position.X + fromPosition.X;
                var posB = b.Position.X + fromPosition.X;
                return posA.CompareTo(posB); // 升序：小行号优先
            });
            break;
    }
}
```

#### 4.5.2 修改 AddRoom：应用排序

修改 `AddRoom` 方法（第397-433行），在 `GetConfigurations` 之后插入排序调用：

```diff
 private bool AddRoom(IRoomSource source,
     Uid fromRoomId, DoorCode code, EdgeDirection direction)
 {
     var fromRoom = Layout.Rooms[fromRoomId];
     var z = source.Z - fromRoom.Position.Z;

     foreach (var entry in GetTemplateGroupEntries(source.TemplateGroup))
     {
-        foreach (var config in GetConfigurations(fromRoom.Template, entry.Template))
+        var configs = GetConfigurations(fromRoom.Template, entry.Template);
+
+        // 根据位置约束对候选配置排序（软约束）
+        if (source.PositionConstraint != PositionConstraint.None)
+        {
+            SortByPositionConstraint(configs, fromRoom.Position.To2D(), source.PositionConstraint);
+        }
+
+        foreach (var config in configs)
         {
             var position = config.Position + fromRoom.Position.To2D();

             if (!config.Matches(z, code, direction))
                 continue;
             if (Layout.Intersects(entry.Template, position, source.Z))
                 continue;

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

> **注意：** `GetConfigurations` 原本返回的是 `RandomSeed.Shuffled()`，即一个已 shuffle 的新 `List<Configuration>`。排序会覆盖 shuffle 的随机性——这正是我们想要的：对有约束的房间，位置偏好优先于随机性。

#### 4.5.3 修改 InsertRoom：应用排序

`InsertRoom` 方法（第445-501行）处理将房间插入两个已有房间之间的情况，也需要同样的排序逻辑：

```diff
 private bool InsertRoom(IRoomSource source,
     Uid backRoomId, DoorCode backCode, EdgeDirection backDirection,
     Uid aheadRoomId, DoorCode aheadCode, EdgeDirection aheadDirection)
 {
     var backRoom = Layout.Rooms[backRoomId];
     var aheadRoom = Layout.Rooms[aheadRoomId];
     var z1 = source.Z - backRoom.Position.Z;
     var z2 = aheadRoom.Position.Z - source.Z;

     foreach (var entry in GetTemplateGroupEntries(source.TemplateGroup))
     {
-        foreach (var config1 in GetConfigurations(backRoom.Template, entry.Template))
+        var configs1 = GetConfigurations(backRoom.Template, entry.Template);
+
+        if (source.PositionConstraint != PositionConstraint.None)
+        {
+            SortByPositionConstraint(configs1, backRoom.Position.To2D(), source.PositionConstraint);
+        }
+
+        foreach (var config1 in configs1)
         {
             // ... 原有逻辑不变 ...
         }
     }

     return false;
 }
```

#### 4.5.4 添加 ValidatePositionConstraints 方法

生成完成后的硬约束验证：

```csharp
/// <summary>
/// Validates that all rooms with position constraints satisfy their constraints
/// relative to the overall layout bounds.
/// Returns true if all constraints are satisfied.
/// </summary>
private bool ValidatePositionConstraints()
{
    if (Layout.Rooms.Count == 0)
        return true;

    // 计算布局的行范围 (Position.X)
    var minRow = int.MaxValue;
    var maxRow = int.MinValue;

    foreach (var room in Layout.Rooms.Values)
    {
        var roomMinRow = room.Position.X;
        var roomMaxRow = room.Position.X + room.Template.Cells.Rows - 1;
        minRow = Math.Min(minRow, roomMinRow);
        maxRow = Math.Max(maxRow, roomMaxRow);
    }

    var totalHeight = maxRow - minRow + 1;
    // 上半部的分界线：从 minRow 到 minRow + totalHeight/2
    var midRow = minRow + totalHeight / 2;

    foreach (var room in Layout.Rooms.Values)
    {
        var node = Graph.GetNode(room.Id.A);
        if (node == null)
            continue;

        var roomMinRow = room.Position.X;
        var roomMaxRow = room.Position.X + room.Template.Cells.Rows - 1;

        switch (node.PositionConstraint)
        {
            case PositionConstraint.Bottom:
                // 房间的底边（最大行）应在布局下半部
                if (roomMaxRow < midRow)
                    return false;
                break;

            case PositionConstraint.Top:
                // 房间的顶边（最小行）应在布局上半部
                if (roomMinRow > midRow)
                    return false;
                break;

            case PositionConstraint.TopEdge:
                // 1) 房间应在上半部
                if (roomMinRow > midRow)
                    return false;
                // 2) 房间应在边缘：至少一个方向上与布局边界相邻
                if (!IsOnLayoutEdge(room))
                    return false;
                break;
        }
    }

    return true;
}

/// <summary>
/// Returns true if the room is on the edge of the layout
/// (i.e., at least one side has no adjacent room).
/// </summary>
private bool IsOnLayoutEdge(Room targetRoom)
{
    var targetMinRow = targetRoom.Position.X;
    var targetMaxRow = targetRoom.Position.X + targetRoom.Template.Cells.Rows;
    var targetMinCol = targetRoom.Position.Y;
    var targetMaxCol = targetRoom.Position.Y + targetRoom.Template.Cells.Columns;

    // 检查四个方向是否存在完全遮挡的邻居
    bool hasNorthNeighbor = false;  // 上方
    bool hasSouthNeighbor = false;  // 下方
    bool hasWestNeighbor = false;   // 左方
    bool hasEastNeighbor = false;   // 右方

    foreach (var other in Layout.Rooms.Values)
    {
        if (other.Id == targetRoom.Id || other.Position.Z != targetRoom.Position.Z)
            continue;

        var otherMinRow = other.Position.X;
        var otherMaxRow = other.Position.X + other.Template.Cells.Rows;
        var otherMinCol = other.Position.Y;
        var otherMaxCol = other.Position.Y + other.Template.Cells.Columns;

        // 列范围有重叠才可能是上下邻居
        bool colOverlap = otherMinCol < targetMaxCol && otherMaxCol > targetMinCol;
        // 行范围有重叠才可能是左右邻居
        bool rowOverlap = otherMinRow < targetMaxRow && otherMaxRow > targetMinRow;

        if (colOverlap && otherMaxRow <= targetMinRow)
            hasNorthNeighbor = true; // 上方有房间
        if (colOverlap && otherMinRow >= targetMaxRow)
            hasSouthNeighbor = true; // 下方有房间
        if (rowOverlap && otherMaxCol <= targetMinCol)
            hasWestNeighbor = true;  // 左方有房间
        if (rowOverlap && otherMinCol >= targetMaxCol)
            hasEastNeighbor = true;  // 右方有房间
    }

    // 至少一个方向没有邻居 = 在边缘
    return !hasNorthNeighbor || !hasSouthNeighbor || !hasWestNeighbor || !hasEastNeighbor;
}
```

#### 4.5.5 修改 Generate 方法：在完成检查后添加验证

修改 `Generate` 方法（第168-232行）中的完成检查部分：

```diff
 while (layouts.Count > 0)
 {
     if (cancellationToken.IsCancellationRequested)
     {
         logger?.Invoke("[Layout Generator] Process cancelled.");
         return null;
     }

     Layout = layouts.Peek();

     if (ChainIndex >= chains.Count)
     {
         if (Layout.IsComplete(TemplateGroups))
         {
+            // 验证位置约束
+            if (!ValidatePositionConstraints())
+            {
+                ChainIndex = 0;
+                layouts.Clear();
+                layouts.Push(baseLayout);
+                logger?.Invoke("[Layout Generator] Position constraints not satisfied. Restarting...");
+                continue;
+            }
+
             Layout = new Layout(Layout);
             logger?.Invoke("[Layout Generator] Layout generator complete.");
             return Layout;
         }

         ChainIndex = 0;
         layouts.Clear();
         layouts.Push(baseLayout);
         logger?.Invoke("[Layout Generator] Layout constraints not satisfied. Restarting...");
         continue;
     }

     // ... 后续不变 ...
 }
```

### 4.6 修改：ManiaMap.Godot.csproj

**文件：** `/d/UGit/ManiaMapGodot/ManiaMap.Godot.csproj`

从 NuGet 引用改为本地项目引用：

```diff
 <Project Sdk="Godot.NET.Sdk/4.6.2">
   <PropertyGroup>
     <TargetFramework>net8.0</TargetFramework>
     <LangVersion>11.0</LangVersion>
     <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
     <EnableDynamicLoading>true</EnableDynamicLoading>
   </PropertyGroup>
   <ItemGroup>
     <PackageReference Include="gdUnit4.api" Version="4.2.3" />
-    <PackageReference Include="MPewsey.ManiaMap" Version="2.5.3" />
+    <ProjectReference Include="..\ManiaMap\src\ManiaMap\ManiaMap.csproj" />
   </ItemGroup>
 </Project>
```

### 4.7 修改：Godot 侧 LayoutGraphNode

**文件：** `/d/UGit/ManiaMapGodot/addons/mpewsey.maniamap/scripts/runtime/graphs/LayoutGraphNode.cs`

添加 PositionConstraint 属性并暴露到编辑器 Inspector：

```diff
 using Godot;
 using MPewsey.ManiaMap.Graphs;
+using MPewsey.ManiaMap;
 using System;
 using System.Collections.Generic;

 namespace MPewsey.ManiaMapGodot.Graphs
 {
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

         // ... 现有代码 ...

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
 }
```

---

## 五、算法工作原理图解

### 5.1 无约束时（当前行为）

```
配置候选列表（shuffle 后）: [右, 下, 左, 上, 右下, 左上, ...]
                             ↑
                         取第一个合法的 → 方向随机

结果示意（每次不同）：
  ┌─┐
  │B│
  └┬┘        ┌─┐         ┌─┬─┐
   │    或   │A├─B   或   │A│B│
  ┌┴┐        └─┘         └─┴─┘
  │A│
  └─┘
```

### 5.2 有约束时（修改后）

```
出生房（Bottom 约束）候选排序后: [下, 右下, 左下, 右, 左, 上, ...]
                                  ↑
                              优先往下放

撤离房（TopEdge 约束）候选排序后: [上, 右上, 左上, 右, 左, 下, ...]
                                    ↑
                                优先往上放

结果示意（高概率）：
  ┌──┐
  │撤│ ← 行号小（上方边缘）
  └┬─┘
   │
  ┌┴┐
  │ │ ← 中间房间
  └┬┘
   │
  ┌┴──┐
  │出生│ ← 行号大（下方）
  └───┘
```

### 5.3 硬约束验证示意

```
生成完成后检查：

布局行范围: minRow=0, maxRow=20, midRow=10

出生房 Position.X=18, Rows=3 → maxRow=20 ≥ midRow=10 ✓ (在下半部)
撤离房 Position.X=2,  Rows=2 → minRow=2  ≤ midRow=10 ✓ (在上半部)
撤离房边缘检查：上方(Row<2)无邻居 ✓ (在边缘)

全部通过 → 返回布局
```

---

## 六、布局图设计建议

为了配合位置约束，布局图应按以下原则设计：

### 6.1 示例布局图

```
出生(Bottom)──房A──房B──房C──撤离(TopEdge)
                │              │
                房D────────────房E
```

- **出生节点**设置 `PositionConstraint = Bottom`
- **撤离节点**设置 `PositionConstraint = TopEdge`
- 出生和撤离之间有**多条路径**（图而非树）
- 出生节点在图的"起始端"，撤离在"末端"

### 6.2 chain 分解与放置顺序

ManiaMap 的 `FindChains` 会将图分解为：
1. **环路链**（cycle chains）— 先处理
2. **分支链**（branch chains）— 后处理

由于出生节点在第一条链的起始位置，它会被 `AddFirstRoom` 放在 (0,0)。后续房间通过 `AddRoom` 向外扩展。软约束确保：
- 与出生节点相连的房间**优先往上**放（因为其他房间没有 Bottom 约束）
- 撤离节点**优先往上**放（TopEdge 约束排序）

整体效果：布局从下（出生）往上（撤离）自然展开。

### 6.3 重点房间数量可配

重点房间的数量完全由布局图节点数量决定：
- 需要 2 个撤离点 → 图中放 2 个 `TopEdge` 约束节点
- 需要 3 个 Boss 房间 → 图中放 3 个对应节点

可以在运行时通过代码动态生成不同的 `LayoutGraph`，实现配置化。

---

## 七、风险与成功率分析

### 7.1 软约束的成功率

对于一个典型的 1×1 房间模板（四个门），`ConfigurationSpace` 通常包含 4-8 个候选配置。排序后，排在前面的 2-4 个候选是满足方向约束的。只要其中一个不重叠，就能成功放置。

**预估成功率：**
- 单房间放置满足方向偏好：**70-90%**（取决于已有布局的拥挤程度）
- 整体布局满足所有约束：**50-80%**（取决于图的复杂度）

### 7.2 硬约束的重试代价

即使软约束未能完全满足，硬约束验证会在布局完成后触发重启。每次重启使用不同的随机种子（来自 rebase 机制或 `RunAttemptsAsync` 的种子偏移）。

- `RunAttemptsAsync` 默认 10 次重试
- 可以增大到 20-50 次
- 每次超时 5000ms
- **实际预期：1-3 次即可成功**

### 7.3 不影响原有行为

- 所有现有节点的 `PositionConstraint` 默认为 `None`
- `None` 约束不触发任何排序或验证
- 原有测试应全部通过

---

## 八、验证计划

### 8.1 单元测试

在 ManiaMap 测试项目中添加测试：

```csharp
[Test]
public void TestPositionConstraintBottom()
{
    // 创建布局图，出生节点设置 Bottom 约束
    var graph = new LayoutGraph(1, "Test");
    graph.AddNode(1).SetName("Spawn").SetTemplateGroup("Default")
        .SetPositionConstraint(PositionConstraint.Bottom);
    graph.AddNode(2).SetName("Room").SetTemplateGroup("Default");
    graph.AddNode(3).SetName("Exit").SetTemplateGroup("Default")
        .SetPositionConstraint(PositionConstraint.TopEdge);
    graph.AddEdge(1, 2);
    graph.AddEdge(2, 3);

    // 运行生成 100 次，统计出生房在下半部的比率
    var generator = new LayoutGenerator();
    int successCount = 0;
    for (int i = 0; i < 100; i++)
    {
        var layout = generator.Generate(1, graph, templateGroups,
            new RandomSeed(i + 1));
        if (layout != null)
        {
            var spawnRoom = layout.Rooms[new Uid(1)];
            var exitRoom = layout.Rooms[new Uid(3)];
            if (spawnRoom.Position.X >= exitRoom.Position.X)
                successCount++;
        }
    }
    Assert.Greater(successCount, 80); // 期望 > 80% 成功率
}
```

### 8.2 Godot 集成测试

1. 在 Godot 编辑器中创建布局图
2. 对出生节点设置 `PositionConstraint = Bottom`
3. 对撤离节点设置 `PositionConstraint = TopEdge`
4. 运行 `room_layout_2d_sample`
5. 多次点击生成按钮，观察：
   - 出生房是否始终在底部区域
   - 撤离房是否在上半部边缘
   - 整体布局是否紧凑、从下往上展开

### 8.3 回归测试

```bash
cd /d/UGit/ManiaMap
dotnet test
```

确保所有原有测试通过。
