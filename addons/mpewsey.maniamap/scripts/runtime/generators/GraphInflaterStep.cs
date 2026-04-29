using Godot;
using MPewsey.Common.Pipelines;
using MPewsey.ManiaMap.Generators;

namespace MPewsey.ManiaMapGodot.Generators
{
    /// <summary>
    /// A GenerationStep that inflates the `LayoutGraph` by inserting intermediate nodes
    /// along edges to lengthen the exploration path from spawn to exit.
    /// This allows controlling gameplay duration independently of the topology graph structure.
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class GraphInflaterStep : GenerationStep
    {
        /// <summary>
        /// If false, the step is skipped and the graph passes through unchanged.
        /// </summary>
        [Export] public bool Enabled { get; set; } = true;

        /// <summary>
        /// The desired number of rooms on the shortest path from spawn to exit.
        /// For example, 15 means the player must traverse approximately 15 rooms.
        /// </summary>
        [ExportGroup("Path Length")]
        [Export(PropertyHint.Range, "1,100,1,or_greater")] public int TargetPathLength { get; set; } = 15;

        /// <summary>
        /// Multiplier for side branch inflation relative to the main path.
        /// 0.0 = no side branch inflation, 1.0 = same rate as main path.
        /// </summary>
        [Export(PropertyHint.Range, "0,2,0.1")] public float SideBranchRatio { get; set; } = 0.5f;

        /// <inheritdoc/>
        public override IPipelineStep CreateStep()
        {
            return new GraphInflater(TargetPathLength, SideBranchRatio, Enabled);
        }

        /// <inheritdoc/>
        public override string[] RequiredInputNames()
        {
            return new string[] { "LayoutGraph", "RandomSeed" };
        }

        /// <inheritdoc/>
        public override string[] OutputNames()
        {
            return new string[] { "LayoutGraph" };
        }
    }
}
