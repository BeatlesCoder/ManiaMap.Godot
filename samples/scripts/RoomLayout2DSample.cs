using Godot;
using MPewsey.ManiaMap;
using MPewsey.ManiaMapGodot.Generators;
using System.Collections.Generic;

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

        /// <summary>
        /// Mapping of room tags to display labels and colors for visual identification.
        /// </summary>
        private static readonly Dictionary<string, (string Label, Color Color)> ImportantTags = new()
        {
            { "spawn",      ("SPAWN",   new Color(0.2f, 0.8f, 0.2f)) },   // Green
            { "extraction", ("EXIT",    new Color(0.8f, 0.2f, 0.2f)) },   // Red
            { "boss",       ("BOSS",    new Color(0.8f, 0.1f, 0.8f)) },   // Purple
            { "treasure",   ("TREASURE", new Color(1.0f, 0.8f, 0.0f)) },  // Gold
        };

        /// <summary>
        /// Preset colors assigned to rooms in sequence so adjacent rooms are visually distinct.
        /// </summary>
        private static readonly Color[] RoomColors = new Color[]
        {
            new Color(0.40f, 0.70f, 1.00f), // Light blue
            new Color(1.00f, 0.60f, 0.30f), // Orange
            new Color(0.50f, 0.90f, 0.50f), // Light green
            new Color(0.90f, 0.50f, 0.90f), // Pink
            new Color(1.00f, 0.90f, 0.40f), // Yellow
            new Color(0.40f, 0.90f, 0.90f), // Cyan
            new Color(0.90f, 0.55f, 0.55f), // Salmon
            new Color(0.70f, 0.70f, 1.00f), // Lavender
        };

        private static readonly Color DoorColor = new Color(1.0f, 1.0f, 1.0f, 0.9f); // Bright white
        private const float DoorLineWidth = 5.0f;
        private const float DoorLinePadding = 8.0f; // Inset from cell edge corners
        private const float OutlineInset = 4.0f;    // How far inside the cell boundary the outline is drawn

        public override void _Ready()
        {
            base._Ready();
            GenerateButton.GrabFocus();
            GenerateButton.Pressed += OnGenerateButtonPressed;
        }

        private void ClearContainer()
        {
            var count = Container.GetChildCount();

            for (int i = 0; i < count; i++)
            {
                Container.GetChild(i).QueueFree();
            }
        }

        private void OnGenerateButtonPressed()
        {
            GenerateLayoutAsync();
        }

        private async void GenerateLayoutAsync()
        {
            MessageLabel.Text = "Generating...";
            GenerateButton.Disabled = true;
            var seed = Rand.Random.Next(1, int.MaxValue);
            var results = await Pipeline.RunAttemptsAsync(seed, logger: msg => GD.Print(msg));
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
            var rooms = RoomTemplateDatabase.CreateRoom2DInstances(Container, layoutPack);
            AddRoomOutlines(rooms);
            AddDoorIndicators(rooms, layout);
            AddRoomLabels(rooms);
            Camera.CenterCameraView(layout, CellSize);
        }

        /// <summary>
        /// Draws an outline around each room's active cells using Line2D.
        /// Lines are inset into the cell so adjacent rooms have a visible gap between them.
        /// Each room gets a distinct color from a rotating palette.
        /// </summary>
        private void AddRoomOutlines(List<RoomNode2D> roomNodes)
        {
            for (int idx = 0; idx < roomNodes.Count; idx++)
            {
                var roomNode = roomNodes[idx];
                if (!roomNode.IsInitialized)
                    continue;

                var cells = roomNode.ActiveCells;
                int rows = roomNode.Rows;
                int cols = roomNode.Columns;
                float cw = roomNode.CellSize.X;
                float ch = roomNode.CellSize.Y;
                float inset = OutlineInset;

                var color = RoomColors[idx % RoomColors.Length];

                bool CellActive(int r, int c)
                {
                    if (r < 0 || r >= rows || c < 0 || c >= cols) return false;
                    if (r >= cells.Count || c >= cells[r].Count) return false;
                    return cells[r][c];
                }

                // For each active cell, draw the outer boundary edges inset into the cell.
                // Merge consecutive collinear segments on the same row/column for cleaner lines.
                // Horizontal outer edges (top/bottom of cells)
                for (int r = 0; r < rows; r++)
                {
                    // Top edges: scan left to right, merge consecutive
                    int c = 0;
                    while (c < cols)
                    {
                        if (CellActive(r, c) && !CellActive(r - 1, c))
                        {
                            int cStart = c;
                            while (c < cols && CellActive(r, c) && !CellActive(r - 1, c))
                                c++;
                            // Merged segment from cStart to c
                            float y = r * ch + inset;
                            float xa = cStart * cw + inset;
                            float xb = c * cw - inset;
                            AddOutlineSeg(roomNode, new Vector2(xa, y), new Vector2(xb, y), color);
                        }
                        else c++;
                    }

                    // Bottom edges
                    c = 0;
                    while (c < cols)
                    {
                        if (CellActive(r, c) && !CellActive(r + 1, c))
                        {
                            int cStart = c;
                            while (c < cols && CellActive(r, c) && !CellActive(r + 1, c))
                                c++;
                            float y = (r + 1) * ch - inset;
                            float xa = cStart * cw + inset;
                            float xb = c * cw - inset;
                            AddOutlineSeg(roomNode, new Vector2(xa, y), new Vector2(xb, y), color);
                        }
                        else c++;
                    }
                }

                // Vertical outer edges (left/right of cells)
                for (int c = 0; c < cols; c++)
                {
                    // Left edges
                    int r = 0;
                    while (r < rows)
                    {
                        if (CellActive(r, c) && !CellActive(r, c - 1))
                        {
                            int rStart = r;
                            while (r < rows && CellActive(r, c) && !CellActive(r, c - 1))
                                r++;
                            float x = c * cw + inset;
                            float ya = rStart * ch + inset;
                            float yb = r * ch - inset;
                            AddOutlineSeg(roomNode, new Vector2(x, ya), new Vector2(x, yb), color);
                        }
                        else r++;
                    }

                    // Right edges
                    r = 0;
                    while (r < rows)
                    {
                        if (CellActive(r, c) && !CellActive(r, c + 1))
                        {
                            int rStart = r;
                            while (r < rows && CellActive(r, c) && !CellActive(r, c + 1))
                                r++;
                            float x = (c + 1) * cw - inset;
                            float ya = rStart * ch + inset;
                            float yb = r * ch - inset;
                            AddOutlineSeg(roomNode, new Vector2(x, ya), new Vector2(x, yb), color);
                        }
                        else r++;
                    }
                }

                // Corner connectors: at each active cell's inset corner, if both adjacent
                // outer edges meet, draw a small corner segment to close the shape visually.
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (!CellActive(r, c)) continue;

                        float x0 = c * cw + inset;
                        float y0 = r * ch + inset;
                        float x1 = (c + 1) * cw - inset;
                        float y1 = (r + 1) * ch - inset;

                        bool noTop = !CellActive(r - 1, c);
                        bool noBot = !CellActive(r + 1, c);
                        bool noLft = !CellActive(r, c - 1);
                        bool noRgt = !CellActive(r, c + 1);

                        // Top-left corner
                        if (noTop && noLft)
                            AddOutlineSeg(roomNode, new Vector2(x0, y0), new Vector2(x0, y0), color);
                        // Top-right corner
                        if (noTop && noRgt)
                            AddOutlineSeg(roomNode, new Vector2(x1, y0), new Vector2(x1, y0), color);
                        // Bottom-left corner
                        if (noBot && noLft)
                            AddOutlineSeg(roomNode, new Vector2(x0, y1), new Vector2(x0, y1), color);
                        // Bottom-right corner
                        if (noBot && noRgt)
                            AddOutlineSeg(roomNode, new Vector2(x1, y1), new Vector2(x1, y1), color);

                        // Concave (inner) corners: where a diagonal neighbor is missing
                        // but both orthogonal neighbors are present, creating an inward notch.
                        // Top-left concave: top and left neighbors exist, but top-left diagonal doesn't
                        if (!noTop && !noLft && !CellActive(r - 1, c - 1))
                        {
                            float cx = c * cw - inset;  // right edge of left neighbor
                            float cy = r * ch - inset;  // bottom edge of top neighbor
                            AddOutlineSeg(roomNode, new Vector2(cx, y0), new Vector2(cx, cy), color);
                            AddOutlineSeg(roomNode, new Vector2(cx, cy), new Vector2(x0, cy), color);
                        }
                        // Top-right concave
                        if (!noTop && !noRgt && !CellActive(r - 1, c + 1))
                        {
                            float cx = (c + 1) * cw + inset;
                            float cy = r * ch - inset;
                            AddOutlineSeg(roomNode, new Vector2(x1, cy), new Vector2(cx, cy), color);
                            AddOutlineSeg(roomNode, new Vector2(cx, cy), new Vector2(cx, y0), color);
                        }
                        // Bottom-left concave
                        if (!noBot && !noLft && !CellActive(r + 1, c - 1))
                        {
                            float cx = c * cw - inset;
                            float cy = (r + 1) * ch + inset;
                            AddOutlineSeg(roomNode, new Vector2(x0, cy), new Vector2(cx, cy), color);
                            AddOutlineSeg(roomNode, new Vector2(cx, cy), new Vector2(cx, y1), color);
                        }
                        // Bottom-right concave
                        if (!noBot && !noRgt && !CellActive(r + 1, c + 1))
                        {
                            float cx = (c + 1) * cw + inset;
                            float cy = (r + 1) * ch + inset;
                            AddOutlineSeg(roomNode, new Vector2(cx, y1), new Vector2(cx, cy), color);
                            AddOutlineSeg(roomNode, new Vector2(cx, cy), new Vector2(x1, cy), color);
                        }
                    }
                }

                // Room name label at top-left.
                var room = roomNode.RoomLayout;
                var nameLabel = new Label();
                nameLabel.Text = room.Name;
                nameLabel.AddThemeColorOverride("font_color", color);
                nameLabel.AddThemeFontSizeOverride("font_size", 11);
                nameLabel.Position = new Vector2(inset + 2, inset);
                nameLabel.ZIndex = 2;
                roomNode.AddChild(nameLabel);
            }
        }

        /// <summary>
        /// Adds a single Line2D segment to a room node as part of its outline.
        /// </summary>
        private static void AddOutlineSeg(RoomNode2D parent, Vector2 a, Vector2 b, Color color)
        {
            var line = new Line2D();
            line.Width = 2.0f;
            line.DefaultColor = color;
            line.ZIndex = 1;
            line.Antialiased = true;
            line.BeginCapMode = Line2D.LineCapMode.Box;
            line.EndCapMode = Line2D.LineCapMode.Box;
            line.AddPoint(a);
            line.AddPoint(b);
            parent.AddChild(line);
        }

        /// <summary>
        /// Draws thick line segments at each connected door location so you can see
        /// where rooms are joined. Only doors that are actually connected in the layout
        /// are drawn (not all possible door slots).
        /// </summary>
        private void AddDoorIndicators(List<RoomNode2D> roomNodes, Layout layout)
        {
            // Build a lookup: room Uid -> RoomNode2D
            var roomLookup = new Dictionary<Uid, RoomNode2D>();
            foreach (var roomNode in roomNodes)
            {
                if (roomNode.IsInitialized)
                    roomLookup[roomNode.RoomLayout.Id] = roomNode;
            }

            foreach (var connection in layout.DoorConnections.Values)
            {
                // Draw on both sides of the connection
                DrawDoorOnRoom(roomLookup, connection.FromRoom, connection.FromDoor);
                DrawDoorOnRoom(roomLookup, connection.ToRoom, connection.ToDoor);
            }
        }

        /// <summary>
        /// Draws a single thick line segment on a room's cell edge where a door is connected.
        /// </summary>
        private void DrawDoorOnRoom(Dictionary<Uid, RoomNode2D> roomLookup, Uid roomId, DoorPosition doorPos)
        {
            if (!roomLookup.TryGetValue(roomId, out var roomNode))
                return;

            float cw = roomNode.CellSize.X;
            float ch = roomNode.CellSize.Y;
            int row = doorPos.Position.X;  // ManiaMap: X = row
            int col = doorPos.Position.Y;  // ManiaMap: Y = column
            float pad = DoorLinePadding;
            float inset = OutlineInset;

            // Cell bounds inset to match the outline
            float x0 = col * cw + inset;
            float y0 = row * ch + inset;
            float x1 = (col + 1) * cw - inset;
            float y1 = (row + 1) * ch - inset;

            Vector2 a, b;
            switch (doorPos.Direction)
            {
                case DoorDirection.North:
                    a = new Vector2(x0 + pad, y0);
                    b = new Vector2(x1 - pad, y0);
                    break;
                case DoorDirection.South:
                    a = new Vector2(x0 + pad, y1);
                    b = new Vector2(x1 - pad, y1);
                    break;
                case DoorDirection.West:
                    a = new Vector2(x0, y0 + pad);
                    b = new Vector2(x0, y1 - pad);
                    break;
                case DoorDirection.East:
                    a = new Vector2(x1, y0 + pad);
                    b = new Vector2(x1, y1 - pad);
                    break;
                default:
                    return;
            }

            var line = new Line2D();
            line.Width = DoorLineWidth;
            line.DefaultColor = DoorColor;
            line.ZIndex = 3;
            line.Antialiased = true;
            line.AddPoint(a);
            line.AddPoint(b);
            roomNode.AddChild(line);
        }

        /// <summary>
        /// Adds colored labels and background highlights to important rooms based on their tags.
        /// </summary>
        private void AddRoomLabels(List<RoomNode2D> roomNodes)
        {
            foreach (var roomNode in roomNodes)
            {
                if (!roomNode.IsInitialized)
                    continue;

                var room = roomNode.RoomLayout;

                foreach (var tag in room.Tags)
                {
                    if (ImportantTags.TryGetValue(tag, out var info))
                    {
                        var rows = room.Template.Cells.Rows;
                        var cols = room.Template.Cells.Columns;

                        var bg = new ColorRect();
                        bg.Color = new Color(info.Color.R, info.Color.G, info.Color.B, 0.15f);
                        bg.Size = new Vector2(cols * CellSize.X, rows * CellSize.Y);
                        bg.ZIndex = -1;
                        roomNode.AddChild(bg);

                        var label = new Label();
                        label.Text = info.Label;
                        label.AddThemeColorOverride("font_color", info.Color);
                        label.AddThemeFontSizeOverride("font_size", 24);
                        label.HorizontalAlignment = HorizontalAlignment.Center;
                        label.VerticalAlignment = VerticalAlignment.Center;
                        label.Size = new Vector2(cols * CellSize.X, rows * CellSize.Y);
                        label.Position = Vector2.Zero;
                        roomNode.AddChild(label);

                        break;
                    }
                }
            }
        }
    }
}
