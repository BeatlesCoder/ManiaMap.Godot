# ManiaMap 需求支持分析

> 逐项对照你的需求与 ManiaMap 源码，明确已支持 / 需扩展。

---

## 一、逐项分析

### ✅ 1. 多种房间大小（1×1、1×2、2×2、3×2、3×3 等）

**已支持。**

`RoomTemplate` 的形状由 `Array2D<Cell> Cells` 定义，Rows × Columns 任意：

```csharp
// RoomTemplate.cs 第35行
public Array2D<Cell> Cells { get; private set; }
```

Godot 侧通过 `RoomNode2D` 的 `Rows`、`Columns` 属性设置尺寸，策划在编辑器中直接配置即可。

---

### ✅ 2. L形、T形等不规则形状

**已支持。**

`Array2D<Cell>` 中每个格子可以是 `Cell` 对象（激活）或 `null`（空/非激活）：

```csharp
// Cell.cs 第18行
public static Cell Empty => null;   // null = 该格子不存在
public static Cell New => new Cell(); // 有效格子
```

例如 3×3 的 T 形：
```
[Cell] [Cell] [Cell]     ← 第0行：全激活
[null] [Cell] [null]     ← 第1行：只激活中间
[null] [Cell] [null]     ← 第2行：只激活中间
```

Godot 编辑器中通过 RoomNode2D 的 ActiveCells 面板点击切换。重叠检测 `Layout.Intersects()` 也只检测激活格子，L/T 形状能正确拼接。

---

### ✅ 3. 门可在任意格子的外边（每个房间 1~n 个门）

**已支持。**

每个 `Cell` 内部有一个 `Dictionary<DoorDirection, Door>` 字典，6 个方向均可放门：

```csharp
// Cell.cs 第29行
public HashMap<DoorDirection, Door> Doors { get; set; }

// 6个方向
public Door WestDoor  { get; set; }
public Door NorthDoor { get; set; }
public Door EastDoor  { get; set; }
public Door SouthDoor { get; set; }
public Door TopDoor   { get; set; }     // 3D 用
public Door BottomDoor { get; set; }    // 3D 用
```

任何激活格子的任何边都可以放门，数量无上限。一个 3×3 房间可以有 12 个门或 1 个门，完全自由。

Godot 侧通过添加 `DoorNode2D` 子节点，设置 Row、Column、DoorDirection 来定义。

---

### ✅ 4. 房间不可旋转

**需求满足，但需注意 ManiaMap 实际上提供了旋转 API。**

`RoomTemplate` 类有完整的旋转/镜像方法：

```csharp
// RoomTemplate.cs 第265-296行
public List<RoomTemplate> AllVariations()     // 生成所有旋转+镜像变体
public List<RoomTemplate> UniqueVariations()  // 去重后的唯一变体
public RoomTemplate Rotated90(int id)
public RoomTemplate Rotated180(int id)
public RoomTemplate Rotated270(int id)
public RoomTemplate MirroredVertically(int id)
public RoomTemplate MirroredHorizontally(int id)
```

**但 `LayoutGenerator` 本身不会自动旋转房间**——搜索确认 LayoutGenerator.cs 中没有调用任何旋转/镜像方法。这些 API 是给用户**手动**生成变体用的（比如把一个 L 形模板生成 4 个旋转版本，作为 4 个独立模板放入 TemplateGroup）。

**结论：只要策划不主动调用旋转 API 来生成变体，房间就不会被旋转。天然满足你的需求。**

---

### ✅ 5. 门对门直接拼接，不走廊

**已支持。这是 ManiaMap 的默认行为。**

`LayoutGenerator.AddRoom()` 通过 `ConfigurationSpace` 寻找两个模板之间所有可能的门对门对接位置，直接将新房间放置在已有房间旁边：

```csharp
// LayoutGenerator.cs 第405-413行
foreach (var config in GetConfigurations(fromRoom.Template, entry.Template))
{
    var position = config.Position + fromRoom.Position.To2D();
    // position 就是新房间紧挨着 fromRoom 的位置
    ...
}
```

`ConfigurationSpace` 预计算所有对齐的门对（第47-64行）——两个模板之间所有可以门对门的摆放方式。

**"走廊"在 ManiaMap 中是可选的**——只有当 `LayoutEdge.RoomChance > 0` 时才会在边上生成额外房间。设置 `RoomChance = 0`（默认值）则完全没有走廊房间。

---

### ✅ 6. 房间形成图（非树），相互连通

**已支持。**

`GraphChainDecomposer.FindChains()` 明确先处理环路、再处理分支：

```csharp
// GraphChainDecomposer.cs 第53-55行
AddCycleChains();      // ← 先找所有环
AddBranchChains();     // ← 再找分支
```

`AddCycleChains()` 调用 `Graph.FindCycles()` 找到所有环路：

```csharp
// 第66-75行
private void AddCycleChains()
{
    var cycles = Graph.FindCycles();  // 找所有环
    cycles.Sort((x, y) => x.Count.CompareTo(y.Count));
    foreach (var cycle in cycles)
    {
        cycle.Add(cycle[0]);  // 闭合环
        Chains.Add(GetChainEdges(cycle));
    }
}
```

布局图中画多条路径连接同一对节点，就形成环路。`LayoutGraph.Validate()` 只要求图是全连通的，不要求是树。

---

### ✅ 7. 模板使用次数可控

**已支持。**

`TemplateGroupsEntry` 有 `MinQuantity` 和 `MaxQuantity`：

```csharp
// TemplateGroupsEntry.cs 第19-39行
public int MinQuantity { get; set; }              // 最少使用次数，默认 0
public int MaxQuantity { get; set; } = int.MaxValue; // 最多使用次数，默认无限
```

`LayoutGenerator.GetTemplateGroupEntries()` 在选择模板时会检查数量上限：

```csharp
// LayoutGenerator.cs 第256-258行
var count = Layout.GetTemplateCount(entry);
if (count < entry.MaxQuantity)
    result.Add(entry);
```

生成完成后 `Layout.IsComplete()` 检查所有 MinQuantity 是否满足：

```csharp
// Layout.cs 第110-119行
public bool IsComplete(TemplateGroups groups)
{
    foreach (var entry in groups.GetAllEntries())
    {
        if (!entry.QuantitySatisfied(GetTemplateCount(entry)))
            return false;
    }
    return true;
}
```

---

### ❌ 8. 重点房间位置约束（出生房在底部、撤离房在顶部）

**不支持，需要扩展。**

`LayoutNode` 的所有属性：

```csharp
// LayoutNode.cs
public int Id { get; set; }
public string Name { get; set; }
public int Z { get; set; }                    // 层级坐标（楼层），不是上下位置
public string TemplateGroup { get; set; }
public Color4 Color { get; set; }
public List<string> Tags { get; set; }
```

**没有任何位置约束属性。**

`AddFirstRoom()` 固定把第一个房间放在 `(0,0)`：

```csharp
// LayoutGenerator.cs 第381行
var room = new Room(source, Vector2DInt.Zero, entry.Template, RandomSeed.Next());
```

`AddRoom()` 从 shuffle 后的候选配置中取第一个合法的，方向完全随机：

```csharp
// LayoutGenerator.cs 第242行
return RandomSeed.Shuffled(space.Configurations);  // 随机打乱
```

**需要扩展：给 LayoutNode 加位置约束属性，在 AddRoom 中按约束排序候选配置。**

---

### ❌ 9. 重点房间之间的最小距离约束

**不支持，需要扩展。**

搜索 LayoutGenerator.cs 和 Layout.cs，**没有任何距离相关的代码**。

`distance` 关键字只出现在 `CollectableGenerator.cs` 中，用于物品分配权重计算，与房间放置完全无关。

`AddRoom()` 的两个检查条件只有：
1. `config.Matches()` — 门方向/代码兼容
2. `Layout.Intersects()` — 不重叠

**没有距离检查。**

**需要扩展：在 AddRoom 中增加最小距离验证，或在生成完成后验证距离。**

---

## 二、总结表

| # | 需求 | ManiaMap 状态 | 代码证据 |
|---|------|--------------|----------|
| 1 | 多种房间大小 | ✅ 已支持 | `RoomTemplate.Cells` = `Array2D<Cell>`，Rows×Columns 任意 |
| 2 | L/T 不规则形状 | ✅ 已支持 | `Cell` 为 `null` 表示空格子，支持任意形状 |
| 3 | 任意位置任意数量的门 | ✅ 已支持 | `Cell.Doors` = `Dictionary<DoorDirection, Door>`，每格子6方向 |
| 4 | 不可旋转 | ✅ 天然满足 | `LayoutGenerator` 不调用旋转API，但API存在（不用即可） |
| 5 | 门对门拼接，无走廊 | ✅ 已支持 | `ConfigurationSpace` 预计算门对门配置，`RoomChance=0` 禁走廊 |
| 6 | 图拓扑（有环） | ✅ 已支持 | `GraphChainDecomposer.AddCycleChains()` 先处理环路 |
| 7 | 模板使用次数 | ✅ 已支持 | `TemplateGroupsEntry.MinQuantity/MaxQuantity` |
| **8** | **位置约束** | **❌ 需扩展** | **LayoutNode 无位置属性，AddRoom 无方向偏好** |
| **9** | **最小距离约束** | **❌ 需扩展** | **无任何距离检查代码** |

---

## 三、需要扩展的两项

### 扩展1：位置约束

**改动点：**
- `LayoutNode` 增加 `PositionConstraint` 属性（Bottom / Top / TopEdge）
- `LayoutGenerator.AddRoom()` 中按约束对候选配置**排序**（软约束）
- `LayoutGenerator.Generate()` 完成后**验证**位置（硬约束兜底）

**影响范围：** 3个文件（LayoutNode、IRoomSource、LayoutGenerator）

### 扩展2：最小距离约束

**改动点：**
- 新增距离约束数据结构（哪两种房间之间需要多少最小距离）
- `LayoutGenerator.AddRoom()` 中增加距离检查条件
- 或在 `Generate()` 完成后验证所有距离约束

**距离计算方式：** 两个房间之间的格子曼哈顿距离（用 `Room.Position` 计算）：
```
distance = |roomA.Position.X - roomB.Position.X| + |roomA.Position.Y - roomB.Position.Y|
```

**影响范围：** 2-3个文件（LayoutGenerator、可能新增约束配置类）

---

## 四、改动量评估

两项扩展加起来，核心改动集中在 `LayoutGenerator.cs` 一个文件（~50行新增代码），加上数据结构定义（~30行）。

**不影响任何已有功能**——所有新属性默认值都使约束不生效，原有测试应全部通过。
