using Godot;
using MPewsey.ManiaMap;
using System.Collections.Generic;

namespace MPewsey.ManiaMapGodot.Samples
{
    /// <summary>
    /// Runtime scene that displays all room templates in a grid layout.
    /// Shows cell shapes, door positions, and template names for quick visual reference.
    /// Supports mouse drag to pan and scroll wheel to zoom.
    /// </summary>
    [GlobalClass]
    public partial class TemplateViewer : Node2D
    {
        [Export] public RoomTemplateDatabase RoomTemplateDatabase { get; set; }
        [Export] public float CellSize { get; set; } = 48f;
        [Export] public float Spacing { get; set; } = 32f;
        [Export] public int ColumnsPerRow { get; set; } = 6;

        private static readonly Color CellColor = new Color(0.22f, 0.25f, 0.32f);
        private static readonly Color OutlineColor = new Color(0.55f, 0.75f, 1.0f);
        private static readonly Color DoorColor = new Color(1.0f, 0.85f, 0.2f);
        private static readonly Color LabelColor = new Color(0.9f, 0.9f, 0.9f);
        private static readonly Color DimLabelColor = new Color(0.6f, 0.6f, 0.6f);
        private const float OutlineWidth = 1.5f;
        private const float DoorWidth = 3.0f;
        private const float DoorPad = 4.0f;
        private const float Inset = 2.0f;

        private Camera2D _camera;
        private bool _dragging;
        private Vector2 _dragStart;

        public override void _Ready()
        {
            base._Ready();
            _camera = GetNode<Camera2D>("Camera2D");
            BuildView();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb)
            {
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    _dragging = mb.Pressed;
                    _dragStart = mb.GlobalPosition;
                }
                else if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
                {
                    _camera.Zoom *= 1.1f;
                }
                else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
                {
                    _camera.Zoom *= 0.9f;
                }
            }
            else if (@event is InputEventMouseMotion mm && _dragging)
            {
                _camera.Position -= mm.Relative / _camera.Zoom;
            }
        }

        private void BuildView()
        {
            if (RoomTemplateDatabase == null)
                return;

            // Gather all unique templates across all groups.
            var seen = new HashSet<int>();
            var templates = new List<(string GroupName, RoomTemplateResource Res, RoomTemplate Template)>();

            foreach (var group in RoomTemplateDatabase.TemplateGroups)
            {
                foreach (var entry in group.Entries)
                {
                    var res = entry.RoomTemplate;
                    if (seen.Add(res.Id))
                    {
                        var tmpl = res.GetMMRoomTemplate();
                        templates.Add((group.Name, res, tmpl));
                    }
                }
            }

            // Title
            var title = new Label();
            title.Text = $"Room Templates ({templates.Count})";
            title.AddThemeColorOverride("font_color", LabelColor);
            title.AddThemeFontSizeOverride("font_size", 20);
            title.Position = new Vector2(Spacing, Spacing);
            AddChild(title);

            // Lay out templates in a grid, track total bounds.
            float startY = Spacing + 40;
            float curX = Spacing;
            float curY = startY;
            float rowMaxH = 0;
            float totalW = 0;
            float totalH = startY;
            int col = 0;

            for (int i = 0; i < templates.Count; i++)
            {
                var (groupName, res, tmpl) = templates[i];
                int rows = tmpl.Cells.Rows;
                int cols = tmpl.Cells.Columns;

                float blockW = cols * CellSize;
                float blockH = rows * CellSize + 32; // extra space for labels below

                var container = new Node2D();
                container.Position = new Vector2(curX, curY);
                AddChild(container);

                DrawTemplate(container, tmpl, res.TemplateName, groupName);

                float rightEdge = curX + blockW;
                if (rightEdge > totalW) totalW = rightEdge;

                curX += blockW + Spacing;
                if (blockH > rowMaxH) rowMaxH = blockH;
                col++;

                if (col >= ColumnsPerRow)
                {
                    col = 0;
                    curX = Spacing;
                    curY += rowMaxH + Spacing;
                    rowMaxH = 0;
                }
            }

            // Account for the last row
            totalH = curY + rowMaxH + Spacing;
            totalW += Spacing;

            // Fit camera to show all content
            FitCamera(totalW, totalH);

            GD.Print($"[TemplateViewer] Displaying {templates.Count} templates.");
        }

        /// <summary>
        /// Centers the camera and sets zoom so all templates fit within the viewport.
        /// </summary>
        private void FitCamera(float contentW, float contentH)
        {
            var viewport = GetViewport().GetVisibleRect().Size;
            float zoomX = viewport.X / contentW;
            float zoomY = viewport.Y / contentH;
            float zoom = Mathf.Min(zoomX, zoomY) * 0.95f; // 5% margin
            zoom = Mathf.Clamp(zoom, 0.1f, 3.0f);

            _camera.Zoom = new Vector2(zoom, zoom);
            _camera.Position = new Vector2(contentW / 2f, contentH / 2f);
        }

        private void DrawTemplate(Node2D parent, RoomTemplate tmpl, string name, string groupName)
        {
            int rows = tmpl.Cells.Rows;
            int cols = tmpl.Cells.Columns;
            float cs = CellSize;

            // Draw cells
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var cell = tmpl.Cells[r, c];
                    if (cell == null) continue;

                    // Cell background
                    var rect = new ColorRect();
                    rect.Color = CellColor;
                    rect.Position = new Vector2(c * cs + Inset, r * cs + Inset);
                    rect.Size = new Vector2(cs - 2 * Inset, cs - 2 * Inset);
                    parent.AddChild(rect);
                }
            }

            // Draw outlines on outer edges
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (tmpl.Cells[r, c] == null) continue;

                    bool CellExists(int rr, int cc)
                    {
                        if (rr < 0 || rr >= rows || cc < 0 || cc >= cols) return false;
                        return tmpl.Cells[rr, cc] != null;
                    }

                    float x0 = c * cs + Inset;
                    float y0 = r * cs + Inset;
                    float x1 = (c + 1) * cs - Inset;
                    float y1 = (r + 1) * cs - Inset;

                    if (!CellExists(r - 1, c))
                        AddSeg(parent, new Vector2(x0, y0), new Vector2(x1, y0), OutlineColor, OutlineWidth);
                    if (!CellExists(r + 1, c))
                        AddSeg(parent, new Vector2(x0, y1), new Vector2(x1, y1), OutlineColor, OutlineWidth);
                    if (!CellExists(r, c - 1))
                        AddSeg(parent, new Vector2(x0, y0), new Vector2(x0, y1), OutlineColor, OutlineWidth);
                    if (!CellExists(r, c + 1))
                        AddSeg(parent, new Vector2(x1, y0), new Vector2(x1, y1), OutlineColor, OutlineWidth);
                }
            }

            // Draw doors
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var cell = tmpl.Cells[r, c];
                    if (cell == null) continue;

                    foreach (var kvp in cell.Doors)
                    {
                        var dir = kvp.Key;
                        DrawDoor(parent, r, c, dir);
                    }
                }
            }

            // Template name label
            var label = new Label();
            label.Text = name;
            label.AddThemeColorOverride("font_color", LabelColor);
            label.AddThemeFontSizeOverride("font_size", 11);
            label.Position = new Vector2(0, rows * cs + 2);
            parent.AddChild(label);

            // Size label
            var sizeLabel = new Label();
            sizeLabel.Text = $"{rows}x{cols}";
            sizeLabel.AddThemeColorOverride("font_color", DimLabelColor);
            sizeLabel.AddThemeFontSizeOverride("font_size", 10);
            sizeLabel.Position = new Vector2(0, rows * cs + 16);
            parent.AddChild(sizeLabel);
        }

        private void DrawDoor(Node2D parent, int row, int col, DoorDirection dir)
        {
            float cs = CellSize;
            float pad = DoorPad;
            float x0 = col * cs + Inset;
            float y0 = row * cs + Inset;
            float x1 = (col + 1) * cs - Inset;
            float y1 = (row + 1) * cs - Inset;

            Vector2 a, b;
            switch (dir)
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

            AddSeg(parent, a, b, DoorColor, DoorWidth);
        }

        private static void AddSeg(Node2D parent, Vector2 a, Vector2 b, Color color, float width)
        {
            var line = new Line2D();
            line.Width = width;
            line.DefaultColor = color;
            line.Antialiased = true;
            line.BeginCapMode = Line2D.LineCapMode.Box;
            line.EndCapMode = Line2D.LineCapMode.Box;
            line.AddPoint(a);
            line.AddPoint(b);
            parent.AddChild(line);
        }
    }
}
