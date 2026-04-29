# ManiaMap 程序化地图生成方案 — 技术文档

> 本文档详细描述 ManiaMap 地图生成系统的完整技术方案，覆盖从全局架构到算法细节。
> 目标读者：需要在 UE 引擎中重新实现该系统的开发者或大模型。

---

## 一、全局概述

### 1.1 一句话总结

ManiaMap 是一个**基于图分解 + 约束放置 + 栈回溯**的程序化地图生成器。它将策划设计的**布局图**（LayoutGraph）分解为有序的**链**（Chain），然后沿着链逐个放置**房间模板**（RoomTemplate），同时满足门对齐、空间不重叠、位置约束等规则。

### 1.2 核心数据流

```
策划输入                    生成过程                      运行时输出
┌──────────┐            ┌──────────────┐            ┌──────────────┐
│ 布局图    │            │              │            │              │
│ (节点+边) │──────────→│  图分解为链   │            │ Layout       │
│          │            │  ↓           │            │ (房间位置表)  │
├──────────┤            │  逐链放置房间 │──────────→│              │
│ 房间模板组 │──────────→│  ↓           │            ├──────────────┤
│ (tscn场景)│            │  约束验证    │            │ 实例化场景    │
├──────────┤            │  ↓           │            │ (世界中的     │
│ 约束配置  │──────────→│  回溯重试    │            │  房间节点)    │
└──────────┘            └──────────────┘            └──────────────┘
```

### 1.3 坐标系统

```
Layout 坐标 (X, Y, Z):
  X = 行号 (Row)，0 在顶部，向下递增 → 对应引擎 Y 轴
  Y = 列号 (Column)，0 在左侧，向右递增 → 对应引擎 X 轴
  Z = 层级，用于多层地图（2D 单层时固定为 0）

世界坐标转换:
  WorldX = Column * CellSizeX
  WorldY = Row * CellSizeY

门方向:
  North = 0 (向上，Row 减小方向)
  South = 1 (向下，Row 增大方向)
  East  = 2 (向右，Column 增大方向)
  West  = 3 (向左，Column 减小方向)
  Top   = 4 (向上层，Z 增大方向)
  Bottom= 5 (向下层，Z 减小方向)
```

---

## 二、核心数据结构

### 2.1 布局图（LayoutGraph）

策划设计的**拓扑关系图**，定义房间之间的连接关系，不包含空间位置信息。

```
LayoutGraph
├── Id: int                          // 图的唯一 ID
├── Name: string                     // 图名称
├── Nodes: Dict<int, LayoutNode>     // 节点列表 (每个节点 = 一个房间)
├── Edges: Dict<EdgeIndexes, LayoutEdge>  // 边列表 (每条边 = 一个连接)
└── Neighbors: Dict<int, List<int>>  // 邻接表 (自动维护)
```

**LayoutNode（布局节点）**
```
LayoutNode
├── Id: int                       // 节点 ID
├── Name: string                  // 房间名称 (如 "Spawn", "Boss")
├── TemplateGroup: string         // 引用的模板组名称 (如 "spawn_rooms")
├── Color: Color4                 // 颜色 (可视化用)
├── Z: int                        // 层级
├── Tags: List<string>            // 标签 (如 ["spawn"], ["extraction"])
└── PositionConstraint: enum      // 位置约束 (None/Bottom/Top/TopEdge)
    ├── None = 0     无约束
    ├── Bottom = 1   房间应在地图下半部（行号大）
    ├── Top = 2      房间应在地图上半部（行号小）
    └── TopEdge = 3  房间应在地图上半部且处于边缘位置
```

**LayoutEdge（布局边）**
```
LayoutEdge
├── FromNode: int                  // 起始节点 ID
├── ToNode: int                    // 目标节点 ID
├── Name: string                   // 边名称
├── TemplateGroup: string?         // 可选：边上的中间房间使用的模板组
├── Direction: EdgeDirection       // 边方向性 (Both/ForwardFixed/ReverseFixed 等)
├── DoorCode: DoorCode             // 门代码 (用于门匹配过滤)
├── RoomChance: float              // 在边上生成中间房间的概率 (0~1)
├── RequireRoom: bool              // 是否必须生成中间房间
└── Tags: List<string>             // 中间房间的标签
```

### 2.2 房间模板（RoomTemplate）

描述一个房间的**形状和门位置**，是放置时的"积木块"。

```
RoomTemplate
├── Id: int                                    // 模板唯一 ID
├── Name: string                               // 模板名称
├── Cells: Array2D<Cell>                       // 二维格子数组 (null = 空位，非 null = 活跃格子)
│   ├── Rows: int                              // 行数
│   └── Columns: int                           // 列数
└── CollectableSpots: Dict<int, CollectableSpot>  // 可收集物放置点
```

**Cell（格子）**
```
Cell
├── Doors: HashMap<DoorDirection, Door>   // 该格子上的门 (方向 → 门对象)
└── Features: List<string>                // 特性标签

// 例如一个 3x3 房间，左上角格子有北门和西门：
Cells[0,0].Doors = { North → Door, West → Door }
// 中间格子没有门（内部格子）：
Cells[1,1].Doors = {}
// null 表示该位置无格子（用于 L 形、T 形）：
Cells[0,2] = null
```

**Door（门）**
```
Door
├── Type: DoorType     // 门类型
└── Code: DoorCode     // 门代码 (位标志，用于匹配过滤)

DoorCode (位标志枚举):
  None = 0      只能和 None 匹配
  A = 1, B = 2, C = 4 ... Z = 1<<25
  
匹配规则：
  None & None → 匹配 ✓
  A & A → 匹配 ✓
  A & B → 不匹配 ✗
  (A|B) & A → 匹配 ✓ (有共同位)
```

### 2.3 模板组（TemplateGroups）

按名称分组的模板集合，布局图节点通过名称引用。

```
TemplateGroups
└── Groups: Dict<string, TemplateGroupsEntry[]>
    ├── "general_rooms" → [entry1, entry2, ...]
    ├── "spawn_rooms"   → [entry3, entry4]
    └── "exit_rooms"    → [entry5, entry6]

TemplateGroupsEntry
├── Template: RoomTemplate    // 房间模板
├── MinQuantity: int          // 最少使用次数 (默认 0)
└── MaxQuantity: int          // 最多使用次数 (默认 MaxInt)
```

### 2.4 布局结果（Layout）

生成器的输出，记录所有房间的位置和连接关系。

```
Layout
├── Id: int
├── Name: string
├── Seed: int                                           // 随机种子
├── Rooms: HashMap<Uid, Room>                           // 所有房间
├── DoorConnections: HashMap<RoomPair, DoorConnection>  // 所有门连接
├── Rebases: int                                        // 回溯计数器 (内部用)
└── TemplateCounts: Dict<Entry, int>                    // 模板使用次数统计

Room（房间实例）
├── Id: Uid                    // 复合 ID
├── Name: string               // 房间名称
├── Position: Vector3DInt      // 位置 (Row, Column, Z)
├── Template: RoomTemplate     // 使用的模板
├── Color: Color4              // 颜色
├── Tags: List<string>         // 标签
└── Seed: int                  // 房间级随机种子

DoorConnection（门连接）
├── FromRoom: Uid              // 起始房间 ID
├── ToRoom: Uid                // 目标房间 ID
├── FromDoor: DoorPosition     // 起始侧门位置
│   ├── Position: Vector2DInt  // 格子坐标 (Row, Col)
│   ├── Direction: DoorDirection
│   └── Door: Door
├── ToDoor: DoorPosition       // 目标侧门位置
└── Shaft: Box?                // 垂直通道 (跨层时使用)
```

---

## 三、配置空间预计算（ConfigurationSpace）

### 3.1 核心概念

在生成开始前，系统对**所有模板对**预计算出所有合法的对齐方式（Configuration）。这是一个离线步骤，避免运行时重复计算。

```
ConfigurationSpace
├── FromTemplate: RoomTemplate    // 已放置的房间模板
├── ToTemplate: RoomTemplate      // 待放置的房间模板
└── Configurations: List<Configuration>  // 所有合法对齐方式
```

### 3.2 预计算算法

```
FindConfigurations(fromTemplate, toTemplate):

  对 toTemplate 相对于 fromTemplate 的每个可能偏移 (i, j):
    i 范围: [-toTemplate.Rows, +fromTemplate.Rows]
    j 范围: [-toTemplate.Columns, +fromTemplate.Columns]
    
    offset = Vector2DInt(i, j)
    
    找出在该偏移下 fromTemplate 和 toTemplate 的所有对齐门对:
      遍历 fromTemplate 的每个格子 (r, c):
        检查四个方向的邻居格子是否属于 toTemplate:
          如果 fromTemplate[r,c] 有 South 门，且 toTemplate[r+1-offset.X, c-offset.Y] 有 North 门
          → 产生一个 DoorPair(fromDoor, toDoor)
    
    每个 DoorPair 生成一个 Configuration:
      Configuration {
        Position: offset,           // toTemplate 相对于 fromTemplate 的偏移
        FromDoor: DoorPosition,     // fromTemplate 侧的门
        ToDoor: DoorPosition,       // toTemplate 侧的门
        EdgeDirection: 推算的边方向
      }
```

### 3.3 Configuration 匹配检查

运行时使用配置时，还需检查额外条件：

```
Configuration.Matches(z, doorCode, edgeDirection):
  1. 边方向匹配: this.EdgeDirection == edgeDirection
  2. 门代码匹配: fromDoor.Code & doorCode != 0 (或都为 None)
  3. Z 偏移匹配:
     z > 0 → fromDoor 必须是 Top，toDoor 必须是 Bottom
     z < 0 → fromDoor 必须是 Bottom，toDoor 必须是 Top
     z == 0 → 水平方向，自动满足
```

---

## 四、图分解算法（Graph Chain Decomposition）

### 4.1 为什么需要分解

生成器不能一次性放置所有房间，需要一个有序的放置顺序。图分解将布局图拆分为若干**链**（Chain），每条链是一系列相连的边，可以按顺序处理。

### 4.2 三阶段分解

**第一阶段：环路检测（FindCycles）**

使用 DFS 着色算法（基于 GeeksforGeeks 方法）：
```
对每个节点作为起点执行 DFS:
  颜色 0 = 未访问
  颜色 1 = 正在访问 (DFS 栈上)
  颜色 2 = 已完成
  
  当访问到颜色 1 的节点时 → 发现环路
  沿 parent 链回溯收集环上所有节点
  
结果去重（按节点集合判断唯一性）
按环路长度排序（小环优先）
```

**第二阶段：分支检测（FindBranches）**

```
1. 标记所有环路上的节点为"主干"(trunk)
   如果没有环路，选择邻居最多的节点作为主干
   
2. 从每个主干节点出发 DFS:
   遇到未标记的邻居 → 继续深入
   遇到叶子节点（度为 1）→ 回溯收集路径形成分支
   遇到另一个主干节点 → 形成连接分支
```

**第三阶段：链排序（FormSequentialChains）**

```
1. 环路边 + 分支边 → 合并为边列表，去重
2. 拆分不连续的链段
3. 拆分超长链（如果设置了 MaxBranchLength）
4. 调整边的方向使链内节点顺序一致
5. 排序为可构建序列:
   - 取第一条链，标记其所有节点
   - 循环查找下一条链:
     - 环路链：旋转使其从已标记节点开始
     - 分支链：检查首尾是否连接到已标记节点，必要时反转
   - 标记新链的节点，继续查找
```

### 4.3 排序后的链结构

```
示例图: 1-2-3-4-5-1, 3-6-7
        (五节点环 + 两节点分支)

分解结果:
  Chain 0 (环路): [Edge(1,2), Edge(2,3), Edge(3,4), Edge(4,5), Edge(5,1)]
  Chain 1 (分支): [Edge(3,6), Edge(6,7)]

生成顺序:
  Chain 0: 放置 1→2→3→4→5，最后 5→1 闭合环
  Chain 1: 3 已存在，从 3 出发放置 6→7
```

---

## 五、生成算法主循环

### 5.1 算法参数

```
MaxRebases: int = 100        // 每个子布局最多被重用次数
RebaseDecayRate: float = 0.25 // 指数衰减率
MaxBranchLength: int = -1     // 最大链长度 (-1 = 不限)
MaxMapWidth: int = 0          // 软性最大地图宽度 (0 = 不限)
MaxMapHeight: int = 0         // 软性最大地图高度 (0 = 不限)
MapSizeTolerance: int = 3     // 尺寸容差
DistanceConstraints: List     // 距离约束列表
```

### 5.2 主循环伪代码

```
function Generate(layoutId, graph, templateGroups, randomSeed):
  
  // ===== 初始化 =====
  graph.Validate()
  chains = graph.FindChains(MaxBranchLength)
  configSpaces = templateGroups.GetConfigurationSpaces()  // 预计算所有模板对
  
  baseLayout = new Layout(layoutId)
  stack = Stack<Layout>()
  stack.Push(baseLayout)
  chainIndex = 0
  
  // ===== 主循环 =====
  while stack.Count > 0:
    
    layout = stack.Peek()
    
    // --- 所有链已处理完毕 → 验证约束 ---
    if chainIndex >= chains.Count:
      
      if not layout.IsComplete(templateGroups):
        RESET()  // 重置到初始状态
        continue
      
      if not ValidatePositionConstraints():
        RESET()
        continue
        
      if not ValidateDistanceConstraints():
        RESET()
        continue
        
      if not ValidateMapSize():
        RESET()
        continue
      
      return DeepCopy(layout)  // ★ 成功！
    
    // --- 检查回溯次数 ---
    allowableRebases = ceil(MaxRebases * exp(-chainIndex * RebaseDecayRate))
    
    if layout.Rebases > allowableRebases:
      stack.Pop()       // 放弃当前基础布局
      chainIndex--      // 退回上一条链
      continue
    
    // --- 尝试添加下一条链 ---
    childLayout = Copy(layout)   // 复制构造，layout.Rebases++
    
    if AddChain(childLayout, chains[chainIndex]):
      stack.Push(childLayout)
      chainIndex++
    // else: 失败，下次循环 layout.Rebases 已增加，会重试或回溯
  
  return null  // 所有尝试耗尽，生成失败


function RESET():
  chainIndex = 0
  stack.Clear()
  stack.Push(baseLayout)
```

### 5.3 回溯机制详解

**指数衰减公式：**
```
AllowableRebases = ceil(MaxRebases × e^(-chainIndex × RebaseDecayRate))

默认参数 (MaxRebases=100, Rate=0.25):
  Chain 0: ceil(100 × e^0)     = 100 次
  Chain 1: ceil(100 × e^-0.25) = 78 次
  Chain 2: ceil(100 × e^-0.50) = 61 次
  Chain 5: ceil(100 × e^-1.25) = 29 次
  Chain 10: ceil(100 × e^-2.5) = 9 次
  Chain 15: ceil(100 × e^-3.75) = 3 次
```

**设计思想：**
- 早期链（靠近图根部）允许更多重试，因为影响全局布局
- 后期链（末梢分支）重试少，因为选择空间有限，尽快放弃回溯
- 保证算法在有限步骤内终止

### 5.4 AddChain — 链处理

```
function AddChain(layout, chain):
  
  for i = 0 to chain.Count - 1:
    backEdge = chain[i]
    aheadEdge = chain[i+1] if exists, else null
    
    // 优先尝试"插入"（两端房间已存在，在中间放新房间）
    if CanInsertRoom(backEdge, aheadEdge):
      if not InsertRooms(backEdge, aheadEdge):
        return false
      i++  // 跳过 aheadEdge（已处理）
      continue
    
    // 常规"追加"（从已有房间向外扩展）
    if not AddRooms(backEdge):
      return false
  
  return true
```

---

## 六、房间放置算法

### 6.1 AddFirstRoom — 放置链的第一个房间

```
function AddFirstRoom(source):
  // source = LayoutNode 或 LayoutEdge
  
  for each entry in GetTemplateGroupEntries(source.TemplateGroup):
    if 该模板使用次数 < MaxQuantity:
      room = new Room(source, position=(0,0), entry.Template)
      layout.Rooms.Add(room)
      return true
  
  return false
```

**注意：** 第一个房间始终放在原点 (0, 0)。

### 6.2 AddRoom — 从已有房间扩展放置新房间

这是最核心的放置逻辑：

```
function AddRoom(source, fromRoomId, doorCode, edgeDirection):
  
  fromRoom = layout.Rooms[fromRoomId]
  z = source.Z - fromRoom.Position.Z
  
  for each entry in GetTemplateGroupEntries(source.TemplateGroup):
    
    // 1. 获取配置空间（所有合法对齐方式）
    configs = GetConfigurations(fromRoom.Template, entry.Template)
    // configs 已经是 shuffle 过的随机顺序
    
    // 2. 按约束排序
    SortConfigurations(configs, fromRoom, entry.Template, source.PositionConstraint)
    
    // 3. 逐个尝试
    for each config in configs:
      
      // 计算候选位置 = 配置偏移 + 已有房间位置
      position = config.Position + fromRoom.Position.To2D()
      
      // 检查 1: 配置是否匹配（门代码、方向、Z偏移）
      if not config.Matches(z, doorCode, edgeDirection):
        continue
      
      // 检查 2: 是否与已有房间重叠
      if layout.Intersects(entry.Template, position, source.Z):
        continue
      
      // 检查通过，放置房间
      room = new Room(source, position, entry.Template)
      layout.Rooms.Add(room)
      
      // 创建门连接
      if not AddDoorConnection(fromRoomId, room.Id, config):
        layout.Rooms.Remove(room.Id)  // 连接失败则撤回
        continue
      
      return true  // ★ 放置成功
  
  return false  // 所有配置都失败
```

### 6.3 InsertRoom — 在两个已有房间之间插入新房间

```
function InsertRoom(source, backRoomId, aheadRoomId, ...):
  
  backRoom = layout.Rooms[backRoomId]
  aheadRoom = layout.Rooms[aheadRoomId]
  
  for each entry in templates:
    
    // 1. 找与 backRoom 的合法配置
    configs1 = GetConfigurations(backRoom.Template, entry.Template)
    SortConfigurations(configs1, ...)
    
    for each config1 in configs1:
      position = config1.Position + backRoom.Position.To2D()
      
      if not config1.Matches(...): continue
      if layout.Intersects(...): continue
      
      // 2. 在该位置下，找与 aheadRoom 的合法配置
      configs2 = GetConfigurations(entry.Template, aheadRoom.Template)
      expectedOffset = aheadRoom.Position.To2D() - position
      
      for each config2 in configs2:
        if not config2.Matches(expectedOffset, ...): continue
        
        // 两端都能连上！放置房间
        room = new Room(source, position, entry.Template)
        layout.Rooms.Add(room)
        
        // 连接两端
        if not AddDoorConnection(backRoomId, room.Id, config1):
          撤回; continue
        if not AddDoorConnection(room.Id, aheadRoomId, config2):
          撤回两个; continue
        
        return true
  
  return false
```

### 6.4 SortConfigurations — 配置排序

两级排序，让生成器**优先尝试**满足约束的位置：

```
function SortConfigurations(configs, fromRoom, toTemplate, constraint):
  
  if constraint != None:
    // 第一级：按位置约束排序
    for each config:
      计算 newRow = config.Position.X + fromRoom.Position.X + toTemplate.Rows/2
    
    if constraint == Bottom:
      按 newRow 降序排序  // 优先选行号大的（地图下方）
    else if constraint == Top or TopEdge:
      按 newRow 升序排序  // 优先选行号小的（地图上方）
  
  // 第二级：按地图越界程度排序
  if MaxMapWidth > 0 or MaxMapHeight > 0:
    for each config:
      计算放置后的地图边界
      overflow = max(0, 总高度 - MaxMapHeight) + max(0, 总宽度 - MaxMapWidth)
    
    按 overflow 升序排序  // 优先选不越界的位置
```

### 6.5 重叠检测（Intersects）

```
function Layout.Intersects(template, position2D, z):
  
  // 1. 检查与已有房间的重叠
  for each existingRoom in Rooms:
    if existingRoom.Z != z: continue  // 不同层不冲突
    
    // 检查两个模板在二维空间是否有格子重叠
    for each cell (r, c) in template:
      if cell == null: continue
      worldR = r + position2D.X
      worldC = c + position2D.Y
      localR = worldR - existingRoom.Position.X
      localC = worldC - existingRoom.Position.Y
      
      if existingRoom.Template[localR, localC] != null:
        return true  // 重叠！
  
  // 2. 检查与垂直通道(Shaft)的重叠
  for each connection in DoorConnections:
    if connection.Shaft 与新房间范围相交:
      return true
  
  return false  // 无重叠
```

---

## 七、约束验证

所有链处理完毕后，对整个布局进行全局验证。任一验证失败则**完全重置**重新开始。

### 7.1 位置约束验证

```
function ValidatePositionConstraints():
  
  if Rooms.Count == 0: return true
  
  // 1. 计算地图中线
  minRow = 所有房间中最小的 Position.X
  maxRow = 所有房间中最大的 (Position.X + Template.Rows - 1)
  midRow = minRow + (maxRow - minRow + 1) / 2
  
  // 2. 逐个检查有约束的房间
  for each room in Rooms:
    constraint = 对应 LayoutNode 的 PositionConstraint
    if constraint == None: continue
    
    roomMinRow = room.Position.X
    roomMaxRow = room.Position.X + room.Template.Rows - 1
    
    switch constraint:
      case Bottom:
        if roomMaxRow < midRow: return false  // 房间不在下半部
      
      case Top:
        if roomMinRow > midRow: return false  // 房间不在上半部
      
      case TopEdge:
        if roomMinRow > midRow: return false  // 不在上半部
        if not IsOnLayoutEdge(room): return false  // 不在边缘
  
  return true


function IsOnLayoutEdge(room):
  // 检查房间是否在布局边界（至少一侧没有紧邻的房间）
  
  hasNorthNeighbor = false
  hasSouthNeighbor = false
  hasEastNeighbor = false
  hasWestNeighbor = false
  
  for each otherRoom in Rooms (同层):
    if otherRoom 紧贴 room 的北边: hasNorthNeighbor = true
    if otherRoom 紧贴 room 的南边: hasSouthNeighbor = true
    if otherRoom 紧贴 room 的东边: hasEastNeighbor = true
    if otherRoom 紧贴 room 的西边: hasWestNeighbor = true
  
  return not (hasNorthNeighbor and hasSouthNeighbor 
              and hasEastNeighbor and hasWestNeighbor)
```

### 7.2 距离约束验证

```
function ValidateDistanceConstraints():
  
  for each constraint in DistanceConstraints:
    // constraint = { TagA: "spawn", TagB: "extraction", MinDistance: 8 }
    
    roomsA = 所有含 TagA 标签的房间
    roomsB = 所有含 TagB 标签的房间
    
    for each roomA in roomsA:
      for each roomB in roomsB:
        if roomA == roomB: continue
        
        distance = |roomA.Position.X - roomB.Position.X|
                 + |roomA.Position.Y - roomB.Position.Y|
                 // 曼哈顿距离，基于格子坐标
        
        if distance < constraint.MinDistance:
          return false
  
  return true
```

### 7.3 地图尺寸验证

```
function ValidateMapSize():
  
  if MaxMapWidth <= 0 and MaxMapHeight <= 0:
    return true  // 无限制
  
  bounds = Layout.GetBounds()  // 计算所有房间的总边界
  
  if MaxMapHeight > 0:
    if bounds.Height > MaxMapHeight + MapSizeTolerance:
      return false
  
  if MaxMapWidth > 0:
    if bounds.Width > MaxMapWidth + MapSizeTolerance:
      return false
  
  return true
```

---

## 八、生成管线（Pipeline）

### 8.1 管线架构

多步骤顺序管线，每步读取输入、产出输出，步骤间通过字典传递数据。

```
Pipeline = [Step1, Step2, Step3, Step4]

Step1: LayoutGraphSelectorStep
  输入: "LayoutGraphs" (LayoutGraph[])
  输出: "LayoutGraph" (单个选中的图)
  逻辑: 从数组中随机选一个

Step2: LayoutGraphRandomizerStep
  输入: "LayoutGraph"
  输出: "LayoutGraph" (原地修改)
  逻辑: 对同一 VariationGroup 的节点随机交换属性

Step3: LayoutGeneratorStep ← 核心步骤
  输入: "LayoutId", "LayoutGraph", "TemplateGroups", "RandomSeed"
  输出: "Layout"
  逻辑: 执行第五~七章描述的生成算法

Step4: CollectableGeneratorStep (可选)
  输入: "Layout", "CollectableGroups"
  输出: 修改 Layout (在房间中分配可收集物)
```

### 8.2 重试机制

```
function RunAttemptsAsync(seed, attempts=10, timeout=5000ms):
  
  for i = 0 to attempts - 1:
    currentSeed = seed + i * 1447   // 每次偏移种子
    
    result = Pipeline.Run(currentSeed, timeout)
    
    if result.Success:
      return result
  
  return FailedResult
```

---

## 九、运行时实例化

### 9.1 从 Layout 到场景

```
// 1. 获取生成结果
Layout layout = results.GetOutput<Layout>("Layout")

// 2. 创建 LayoutPack
LayoutPack pack = new LayoutPack(layout, new LayoutState(layout), settings)

// 3. 对每个房间，加载并实例化场景
for each room in layout.Rooms:
  templateResource = database.GetRoomTemplate(room.Template.Id)
  scene = templateResource.LoadScene()       // 加载 .tscn
  
  roomNode = scene.Instantiate<RoomNode2D>() // 实例化场景
  roomNode.Initialize(pack, room, roomState) // 注入布局数据
  roomNode.MoveToLayoutPosition()            // 设置世界坐标
  // WorldPos = (Column * CellSize.X, Row * CellSize.Y)
  
  parent.AddChild(roomNode)                  // 加入场景树
```

### 9.2 房间场景结构

策划制作的 .tscn 文件结构：

```
room_spawn_2x2.tscn
├── RoomNode2D (根节点，脚本: RoomNode2D.cs)
│   ├── 属性: Rows=2, Columns=2, CellSize=(96,96)
│   ├── 属性: ActiveCells=[[true,true],[true,true]]
│   │
│   ├── Cell_r0c0 (ColorRect，视觉填充)
│   ├── Cell_r0c1 (ColorRect)
│   ├── Cell_r1c0 (ColorRect)
│   ├── Cell_r1c1 (ColorRect)
│   │
│   ├── Doors/ (Node2D 容器)
│   │   ├── Door_North_r0c0 (DoorNode2D)
│   │   │   ├── DoorDirection = North (0)
│   │   │   ├── Row = 0, Column = 0
│   │   │   └── Position = (48, 16)  // 格子内偏移
│   │   └── Door_North_r0c1 (DoorNode2D)
│   │       ├── DoorDirection = North (0)
│   │       ├── Row = 0, Column = 1
│   │       └── Position = (144, 16)
│   │
│   └── (策划可自由添加的节点)
│       ├── SpawnPoint (Marker2D)
│       ├── EnemySpawner (Area2D)
│       └── Decorations/ (装饰物)
```

---

## 十、关键设计决策与限制

### 10.1 门匹配是硬约束

两个房间必须通过匹配的门对连接。如果房间模板只有少量门，匹配成功概率低，生成失败率高。

**建议：** 每种尺寸/形状提供多个门配置变体，或让大部分格子外边界都有门。

### 10.2 环路图的限制

`FindChains` 对环路的处理有限制。如果布局图有大量交叉重叠的环路（如完整的 4×4 网格），chain 排序可能失败（`InvalidChainOrderException`）。

**建议：** 布局图采用蛇形或树形拓扑，少量小环路（4 节点环），避免密集网格。

### 10.3 位置约束是后验证

位置约束不是在放置时强制执行的，而是：
1. 排序时偏好满足约束的位置（软约束，提高成功率）
2. 全部放置完成后验证（硬约束，不满足则重来）

**后果：** 约束越严格，重试次数越多，可能失败。

### 10.4 坐标映射

```
ManiaMap Position (Row, Col, Z)
  ↓
Row → 引擎 Y 轴 (或 UE 的 -Y 轴，取决于坐标系)
Col → 引擎 X 轴
Z   → 引擎 Z 轴 (层级)

CellSize 默认 96×96 像素 (2D)
UE 中可按需调整为 3D 单位
```

---

## 十一、UE 重实现建议

### 11.1 推荐实现顺序

```
Phase 1: 基础数据结构
  Vector2DInt, Vector3DInt, Array2D<T>
  DoorDirection, EdgeDirection, DoorCode 枚举
  Door, Cell, RoomTemplate 类
  PositionConstraint, DistanceConstraint

Phase 2: 配置空间
  DoorPair, DoorPosition, Configuration
  ConfigurationSpace (预计算门对齐)

Phase 3: 布局图
  LayoutNode, LayoutEdge, LayoutGraph
  邻接表管理

Phase 4: 图分解
  GraphCycleDecomposer (DFS 环路检测)
  GraphBranchDecomposer (分支检测)
  GraphChainDecomposer (8 步链分解)

Phase 5: 生成器核心
  Layout, Room, DoorConnection
  LayoutGenerator (主循环 + 回溯)
  AddFirstRoom, AddRoom, InsertRoom
  SortConfigurations

Phase 6: 约束验证
  ValidatePositionConstraints
  ValidateDistanceConstraints
  ValidateMapSize

Phase 7: 管线
  Pipeline 多步骤框架
  重试机制
  
Phase 8: 运行时
  从 Layout 实例化 UE Actor/Component
  坐标映射
```

### 11.2 可简化的部分

- **Z 轴/层级：** 如果只做 2D 单层地图，可忽略所有 Z 相关逻辑和 Shaft
- **DoorCode：** 如果不需要门代码过滤，可简化为全部 None
- **EdgeDirection：** 如果所有连接双向，可固定为 Both
- **CollectableGenerator：** 可选模块，初期可跳过
- **VariationGroup：** 节点随机交换，初期可跳过

### 11.3 不可简化的核心

- **ConfigurationSpace 预计算** — 性能关键，不可省略
- **Chain 分解和排序** — 决定放置顺序的正确性
- **栈回溯 + 指数衰减** — 保证终止性和成功率
- **重叠检测** — 正确性基础
- **门对齐匹配** — 房间连接的物理基础
