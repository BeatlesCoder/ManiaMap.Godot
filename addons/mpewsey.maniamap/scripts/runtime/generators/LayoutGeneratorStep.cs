using Godot;
using MPewsey.Common.Pipelines;
using MPewsey.ManiaMap;
using MPewsey.ManiaMap.Generators;
using System.Collections.Generic;

namespace MPewsey.ManiaMapGodot.Generators
{
    /// <summary>
    /// A GenerationStep that produces a procedurally generated `Layout`.
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class LayoutGeneratorStep : GenerationStep
    {
        /// <summary>
        /// The maximum number of times a layout may be used as a base.
        /// </summary>
        [Export(PropertyHint.Range, "1,100,1,or_greater")] public int MaxRebases { get; set; } = 100;

        /// <summary>
        /// The decay rate applied to the maximum number of rebases.
        /// </summary>
        [Export(PropertyHint.Range, "0,1,0.05,or_greater")] public float RebaseDecayRate { get; set; } = 0.25f;

        /// <summary>
        /// The maximum length for graph branches. If less than or equal to zero, branches will not be split.
        /// </summary>
        [Export(PropertyHint.Range, "-1,10,1,or_greater")] public int MaxBranchLength { get; set; } = -1;

        /// <summary>
        /// Preferred maximum map width in cell columns. 0 = no limit.
        /// Soft limit: generator prefers to stay within but allows minor overflow.
        /// </summary>
        [ExportGroup("Map Constraints")]
        [Export(PropertyHint.Range, "0,200,1")] public int MaxMapWidth { get; set; } = 0;

        /// <summary>
        /// Preferred maximum map height in cell rows. 0 = no limit.
        /// Soft limit: generator prefers to stay within but allows minor overflow.
        /// </summary>
        [Export(PropertyHint.Range, "0,200,1")] public int MaxMapHeight { get; set; } = 0;

        /// <summary>
        /// How many cells a room is allowed to exceed Max size before rejecting.
        /// E.g. MaxMapHeight=60, Tolerance=3 → layouts up to 63 rows accepted.
        /// </summary>
        [Export(PropertyHint.Range, "0,10,1")] public int MapSizeTolerance { get; set; } = 3;

        /// <summary>
        /// Distance constraints between tagged rooms.
        /// Each entry should be a Dictionary with keys: "TagA" (string), "TagB" (string), "MinDistance" (int).
        /// </summary>
        [Export] public Godot.Collections.Array<Godot.Collections.Dictionary> DistanceConstraints { get; set; } = new();

        /// <inheritdoc/>
        public override IPipelineStep CreateStep()
        {
            var constraints = new List<DistanceConstraint>();

            if (DistanceConstraints != null)
            {
                foreach (var dict in DistanceConstraints)
                {
                    if (dict.ContainsKey("TagA") && dict.ContainsKey("TagB") && dict.ContainsKey("MinDistance"))
                    {
                        constraints.Add(new DistanceConstraint(
                            dict["TagA"].AsString(),
                            dict["TagB"].AsString(),
                            dict["MinDistance"].AsInt32()));
                    }
                }
            }

            return new LayoutGenerator(MaxRebases, RebaseDecayRate, MaxBranchLength,
                MaxMapWidth, MaxMapHeight, MapSizeTolerance, constraints);
        }

        /// <inheritdoc/>
        public override string[] RequiredInputNames()
        {
            return new string[] { "LayoutId", "LayoutGraph", "TemplateGroups", "RandomSeed" };
        }

        /// <inheritdoc/>
        public override string[] OutputNames()
        {
            return new string[] { "Layout" };
        }
    }
}
