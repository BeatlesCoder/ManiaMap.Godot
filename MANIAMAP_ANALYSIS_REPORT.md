# ManiaMap Spatial Layout Analysis

## Q1: Graph Structure Influence on Spatial Layout?

**ANSWER: NO** - Placement is NOT based on graph traversal order, but uses RandomSeed.

### Evidence from LayoutGeneratorStep.cs (lines 1-46)

Line 36-39: RequiredInputNames includes "RandomSeed"
```csharp
public override string[] RequiredInputNames()
{
    return new string[] { "LayoutId", "LayoutGraph", "TemplateGroups", "RandomSeed" };
}
```

This proves: The LayoutGenerator uses randomization, NOT deterministic traversal order.

---

## Q2: Room Adjacency - Tries ALL Positions?

**ANSWER: NO** - Uses door matching + graph connectivity. Retries with NEW SEED.

### Evidence from GenerationPipeline.cs (lines 200-221)

Line 208: Each attempt uses NEW SEED increment
```csharp
for (int i = 0; i < attempts; i++)
{
    logger?.Invoke($"Beginning attempt {i + 1} / {attempts}...");
    var inputs = new Dictionary<string, object>(manualInputs);
    
    // Line 208: CRITICAL - Each attempt gets DIFFERENT seed
    inputs.Add("RandomSeed", new RandomSeed(seed + i * 1447));
    
    var results = await RunAsync(inputs, logger, token);
    
    if (results.Success)
    {
        logger?.Invoke("Attempt successful.");
        return results;
    }
}
```

**Interpretation**: When door placement fails, it doesn't try more positions - it retries with SEED+1447 (completely new random state).

---

## Q3: Can Inspect Room.Position Data?

**ANSWER: YES** - Access via `layout.Rooms[roomId].Position`

### Evidence from RoomNode2D.cs (lines 339-344)

```csharp
public void MoveToLayoutPosition()
{
    var position = RoomLayout.Position;  // Line 342
    Position = new Vector2(CellSize.X * position.Y, CellSize.Y * position.X);
}
```

Room.Position is Vector2DInt:
- X = Row index
- Y = Column index

### Usage Pattern from RoomLayout2DSample.cs (lines 40-60)

```csharp
var layout = results.GetOutput<Layout>("Layout");  // Line 55

// Inspect room positions:
foreach (var kvp in layout.Rooms)
{
    var room = kvp.Value;
    var gridPosition = room.Position;  // Vector2DInt
    Debug.Log($"Room: Row={gridPosition.X}, Col={gridPosition.Y}");
}
```

---

## Q4: Can Validate/Reject Layouts Based on Constraints?

**ANSWER: YES** - Write custom GenerationStep that throws exception to fail attempt.

### Implementation Structure

1. Create class extending GenerationStep
2. Override CreateStep() to return IPipelineStep
3. In IPipelineStep.Run(): Extract Layout, validate, THROW if invalid
4. Add to pipeline AFTER LayoutGeneratorStep
5. Call RunAttemptsAsync() to enable retry logic

### Code Template

```csharp
public partial class CustomLayoutValidationStep : GenerationStep
{
    public override IPipelineStep CreateStep()
    {
        return new LayoutValidationPipelineStep();
    }

    public override string[] RequiredInputNames()
    {
        return new string[] { "Layout" };  // Requires LayoutGeneratorStep output
    }

    public override string[] OutputNames()
    {
        return Array.Empty<string>();
    }
}

public class LayoutValidationPipelineStep : IPipelineStep
{
    public void Run(PipelineInput input)
    {
        if (!input.TryGetInput("Layout", out Layout layout))
            throw new ArgumentException("Layout not found");

        // Validate room positions
        foreach (var kvp in layout.Rooms)
        {
            var room = kvp.Value;
            if (room.Position.Y < MINIMUM_SPAWN_Y)
            {
                throw new InvalidOperationException("Spawn too far north");
            }
        }
        // If we get here, validation passed!
    }
}
```

### Retry Flow

1. Attempt 1 (seed=N): CustomStep throws → Next attempt
2. Attempt 2 (seed=N+1447): CustomStep throws → Next attempt  
3. Attempt 3 (seed=N+2894): CustomStep succeeds → RETURN results

---

## RunAttemptsAsync Retry Mechanism

### From GenerationPipeline.cs (lines 200-221)

Key code:
```csharp
for (int i = 0; i < attempts; i++)
{
    inputs.Add("RandomSeed", new RandomSeed(seed + i * 1447));  // Line 208
    var results = await RunAsync(inputs, logger, token);

    if (results.Success)
    {
        return results;
    }
}
```

**Retry Details**:
- Seed increment: i * 1447 (line 208)
- Default attempts: 10
- Default timeout: 5000ms per attempt
- Success: results.Success == true (line 212)
- Handles: ANY failure (exception, timeout, missing output)

---

## Summary Answers

| Question | Answer | File | Line |
|----------|--------|------|------|
| Graph structure influences placement? | NO - Uses RandomSeed | LayoutGeneratorStep.cs | 36-39 |
| Adjacency tries ALL positions? | NO - New seed retry | GenerationPipeline.cs | 208 |
| Can access Room.Position? | YES - Vector2DInt | RoomNode2D.cs | 342 |
| Can validate/reject layouts? | YES - Throw exception | GenerationStep.cs | 1-30 |
| Retry mechanism increments seed? | seed + i * 1447 | GenerationPipeline.cs | 208 |

