# ManiaMap.Godot 项目深度分析

## 一、项目概览

**ManiaMap.Godot** 是一个专业级的 Godot .NET 插件，用于**基于有限房间模板的程序化地牢/地图生成**。它封装了 `MPewsey.ManiaMap` C# 核心库，提供完整的编辑器集成和运行时支持。

| 属性 | 值 |
|------|-----|
| **引擎** | Godot 4.2+ (.NET 8) |
| **语言** | C# |
| **核心算法** | 约束满足 + 智能回溯（Rebase 策略） |
| **仓库** | https://github.com/mpewsey/ManiaMap.Godot |

---

## 二、三层架构

整个系统分为三个清晰的层次：

```
┌──────────────────────────────────────────────────────────┐
│  Layer 1: 设计时 (Design-Time)                            │
│  ── 房间模板编辑、布局图编辑、模板组定义                       │
├──────────────────────────────────────────────────────────┤
│  Layer 2: 生成时 (Generation-Time)                        │
│  ── Pipeline 编排、图随机化、核心房间放置、物品分配             │
├──────────────────────────────────────────────────────────┤
│  Layer 3: 运行时 (Runtime)                                │
│  ── 场景实例化、门连接、碰撞检测、状态追踪                     │
└──────────────────────────────────────────────────────────┘
```

---

## 三、核心数据结构

### 1. 房间模板系统

**RoomNode2D / RoomNode3D** — 房间设计节点
- 房间被划分为 **M×N 的格子网格**（Cell Grid）
- 每个格子可以是"激活"（可通行）或"非激活"（墙壁）
- `CellSize` 定义每个格子的像素大小（默认 96×96）

**DoorNode2D / DoorNode3D** — 门连接点
- 定义在格子边界上的连接点
- 属性：`Row`, `Column`（格子坐标）、`DoorDirection`（N/S/E/W/U/D）、`DoorCode`（位标志）
- 运行时通过静态字典 `ActiveRoomDoors` 追踪门的匹配关系

**RoomTemplateResource** — 序列化模板
- 包含：ID、名称、场景路径、**JSON 序列化的格子与门数据**
- 支持异步加载：`LoadSceneAsync()` 用于并行房间实例化

**TemplateGroup** — 加权模板集合
- 将多个 `RoomTemplateResource` 按权重分组
- 被布局图节点引用，约束可用模板范围

### 2. 布局图系统（Layout Graph）

布局图是指导房间放置的**有向图蓝图**：

**LayoutGraphResource** — 图数据结构
```
Nodes: Dictionary<int, LayoutGraphNode>   // 房间槽位
Edges: Dictionary<Vector2I, LayoutGraphEdge>  // 连接定义
```

**LayoutGraphNode** — 图节点（房间槽位）
| 属性 | 说明 |
|------|------|
| `TemplateGroup` | 约束该槽位可用的模板集合 |
| `VariationGroup` | 同组节点可随机交换 |
| `Z` | 层级坐标（支持多层地牢） |
| `Tags` | 语义标记（"boss"、"treasure"等） |

**LayoutGraphEdge** — 图边（潜在连接）
| 属性 | 说明 |
|------|------|
| `RequireRoom` | `true` = 必须成功生成，否则整体失败 |
| `RoomChance` | 生成概率 [0-1]（如 0.5 = 50% 概率的密室） |
| `DoorCode` | 位标志，用于门兼容性检查 |
| `EdgeDirection` | 无/单向/双向 |

---

## 四、生成管线（Pipeline）

### Pipeline 结构

```
GenerationPipeline
├── Inputs（输入）
│   ├── LayoutIdInput          // 布局 ID
│   ├── RandomSeedInput        // 随机种子
│   ├── LayoutGraphsInput      // 布局图 + 模板组
│   └── CollectableGroupsInput // 可收集物品组
└── Steps（步骤）
    ├── LayoutGraphSelectorStep      // 随机选择一个布局图
    ├── LayoutGraphRandomizerStep    // 变体组随机洗牌
    ├── LayoutGeneratorStep          // ⭐ 核心房间放置算法
    └── CollectableGeneratorStep     // 物品分配
```

### 执行流程

```
Pipeline.RunAttemptsAsync(seed, attempts=10, timeout=5000ms)

循环 i = 0 到 9：
  ① seed_i = seed + i × 1447（素数偏移，确保分布均匀）
  ② 执行管线：
     → 选择布局图
     → 随机化变体组
     → 核心房间放置 ← 最关键的步骤
     → 分配物品
  ③ 成功 → 返回 Layout
  ④ 失败/超时 → 尝试下一个种子
```

---

## 五、核心算法：约束满足 + Rebase 回溯

这是整个项目最核心的部分。算法将房间逐个放置，同时满足一系列约束条件。

### 放置流程

```
对布局图中的每个节点（拓扑排序）：
  1. 从该节点的 TemplateGroup 中选择一个模板
  2. 对每个已放置的邻居房间：
     ├─ 检查两个房间是否能共享一扇门
     ├─ 匹配格子网格的边界
     ├─ 验证门代码兼容性（按位与：codeA & codeB ≠ 0）
     └─ 匹配失败 → 标记邻居连接失败
  3. 放置成功 → 继续下一个节点
  4. 放置失败 → 触发 Rebase 策略
```

### Rebase 策略（智能回溯）

这是算法的核心创新——**不是完全回溯，而是从稳定状态重新开始，并逐层衰减重试次数**：

```
Level 0（顶层）：MaxRebases = 100
Level 1：        MaxRebases = 100 × (1 - 0.25) = 75
Level 2：        MaxRebases = 75  × (1 - 0.25) ≈ 56
Level 3：        MaxRebases ≈ 42
Level 4：        MaxRebases ≈ 31
...
```

**核心参数：**
| 参数 | 默认值 | 作用 |
|------|--------|------|
| `MaxRebases` | 100 | 每个分支的最大回溯次数 |
| `RebaseDecayRate` | 0.25 | 每层递归的衰减率 |
| `MaxBranchLength` | -1 | 分支最大长度（-1 = 不限） |

**效果：** 深层递归的重试机会更少，鼓励算法进行更广泛的搜索空间探索，而不是在某个失败分支上反复纠缠。

### 约束条件汇总

```
┌─────────────────────────────────────────────┐
│  1. 门匹配：相邻格子边界必须有兼容的门         │
│  2. 门代码：位标志兼容 (codeA & codeB) ≠ 0    │
│  3. 边要求：RequireRoom=true 的边必须成功      │
│  4. 边概率：RoomChance 随机检查通过才生成       │
│  5. 模板约束：只能使用指定 TemplateGroup 的模板  │
└─────────────────────────────────────────────┘
```

### 门匹配算法

```
放置房间 B 到房间 A 旁边时：
  1. 获取 A 和 B 的共享边界格子
  2. 对每个边界格子：
     a. 获取 A 在该边界的门
     b. 获取 B 在该边界的门
     c. 如果双方都有门：
        → 检查门代码兼容性：(codeA & codeB) ≠ 0
        → 兼容 → 记录成功匹配
  3. 至少一个匹配 → 放置有效
  4. 无匹配且非可选边 → 放置失败
```

---

## 六、物品分配算法

在布局生成完成后，`CollectableGeneratorStep` 使用加权随机采样分配物品：

```
对每个房间 r：
  door_distance = min(到任何有门房间的距离)
  neighbors = 通过门连接的邻居数量

  weight[r] = (1 / door_distance)^DoorPower
            × neighbors^NeighborPower
            × InitialNeighborWeight
```

**效果：** 宝藏倾向于放置在中心/枢纽型房间中（连接多、距离门近的房间权重更高）。

---

## 七、运行时实例化

### LayoutPack — 运行时容器

```csharp
LayoutPack {
    Layout layout          // 生成的房间/门结构
    LayoutState state      // 追踪已访问/可见的格子
    ManiaMapSettings settings  // 运行时配置

    // 内部缓存（构造时构建）
    Dictionary<Uid, List<DoorConnection>> RoomConnections
    Dictionary<int, List<Room>> RoomsByLayer
    Dictionary<Uid, List<DoorPosition>> RoomDoors
}
```

### 场景实例化过程

```
对 Layout 中的每个 Room：
  ① 通过 ID 查找 RoomTemplateResource
  ② 异步加载 PackedScene
  ③ 实例化到容器节点
  ④ 定位：cell_position × CellSize
  ⑤ 添加子节点：
     ├─ DoorNode2D（门连接）
     ├─ CellArea2D（碰撞检测）
     ├─ CollectableSpot2D（物品位置）
     └─ Feature2D（装饰）
  ⑥ 初始化 RoomLayout 和 RoomState
```

---

## 八、完整工作流总结

```
 设计阶段                    生成阶段                     运行阶段
┌──────────┐              ┌──────────────┐             ┌──────────────┐
│ 设计房间   │              │ 输入随机种子    │             │ 实例化场景     │
│ 模板      │──────────▶  │ + 布局图       │──────────▶  │ 树           │
│ (格子+门)  │              │              │             │              │
│          │              │ Pipeline 执行  │             │ 玩家可        │
│ 创建布局图  │              │ ┌────────────┐│             │ 探索地牢      │
│ (节点+边)  │              │ │选择图       ││             │              │
│          │              │ │随机化变体    ││             │ 门连接导航     │
│ 定义模板组  │              │ │⭐放置房间   ││             │ 物品收集       │
│ (加权列表)  │              │ │分配物品     ││             │ 状态追踪       │
│          │              │ └────────────┘│             │              │
└──────────┘              └──────────────┘             └──────────────┘
```

---

## 九、关键设计亮点

1. **Rebase 策略**：不同于传统的完全回溯，通过衰减率控制搜索深度，平衡了成功率和多样性
2. **格子网格抽象**：将房间划分为离散格子，使门匹配和碰撞检测都精确可控
3. **管线架构**：图选择、随机化、生成、物品分配各步骤完全解耦
4. **多次重试机制**：最多 10 次不同种子尝试（素数偏移），大幅提高生成成功率
5. **异步支持**：完整的 async/await 支持，避免阻塞游戏主线程
6. **变体组系统**：同组房间可随机交换，增加布局多样性而不改变拓扑结构

---

## 十、关键文件清单

### 核心生成（~470 行）
| 文件 | 行数 | 说明 |
|------|------|------|
| `GenerationPipeline.cs` | ~343 | 管线编排器 |
| `LayoutGeneratorStep.cs` | ~47 | 核心房间放置算法封装 |
| `CollectableGeneratorStep.cs` | ~48 | 物品分配 |
| `LayoutGraphRandomizerStep.cs` | ~32 | 变体组随机化 |

### 图结构（~599 行）
| 文件 | 行数 | 说明 |
|------|------|------|
| `LayoutGraphResource.cs` | ~342 | 布局图数据结构 |
| `LayoutGraphNode.cs` | ~115 | 图节点定义 |
| `LayoutGraphEdge.cs` | ~142 | 图边定义 |

### 模板与房间（~750+ 行）
| 文件 | 行数 | 说明 |
|------|------|------|
| `RoomTemplateResource.cs` | ~134 | 序列化模板 |
| `RoomNode2D.cs` | ~400+ | 2D 房间节点 |
| `DoorNode2D.cs` | ~128 | 2D 门节点 |
| `TemplateGroup.cs` | ~41 | 加权模板集合 |

### 运行时（~430 行）
| 文件 | 行数 | 说明 |
|------|------|------|
| `RoomTemplateDatabase.cs` | ~270 | 模板查找与工厂 |
| `LayoutPack.cs` | ~160 | 运行时布局容器 |

### 输入节点（~186 行）
| 文件 | 行数 | 说明 |
|------|------|------|
| `LayoutGraphsInput.cs` | ~109 | 布局图输入 |
| `RandomSeedInput.cs` | ~30 | 随机种子输入 |
| `CollectableGroupsInput.cs` | ~47 | 物品组输入 |

### 可视化
| 文件 | 说明 |
|------|------|
| `LayoutTileMapBase.cs` | 瓦片地图基类 |
| `LayoutTileMap.cs` | 单层瓦片地图 |
| `LayoutTileMapBook.cs` | 多层瓦片地图 |

---

## 十一、示例项目

| 示例 | 说明 |
|------|------|
| `RoomLayout2DSample` | 完整的 2D 工作流 |
| `RoomLayout3DSample` | 多层 3D 生成 |
| `LayoutTileMapSample` | 单层瓦片地图可视化 |
| `LayoutTileMapBookSample` | 多层洋葱皮显示 |
| `Collectable/Door/Feature Samples` | 各组件专用示例 |

---

## 十二、数据序列化

### RoomTemplateResource JSON 格式

```json
{
  "Id": 12345,
  "Name": "TreasureRoom",
  "Width": 4,
  "Height": 5,
  "Cells": [
    [true, true, true, false],
    [true, true, true, false]
  ],
  "Doors": [
    {
      "Row": 2,
      "Column": 3,
      "Direction": 1,
      "Code": 3
    }
  ]
}
```

### 布局导出

```csharp
// JSON 导出
var json = JsonSerialization.GetJsonString(layout);
File.WriteAllText("layout.json", json);

// XML 导出
var xml = XmlSerialization.Write(layout);
File.WriteAllText("layout.xml", xml);

// 重新加载
var layout = JsonSerialization.LoadJsonString<Layout>(json);
```
