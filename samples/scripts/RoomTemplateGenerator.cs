#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Text;

namespace MPewsey.ManiaMapGodot.Samples
{
    /// <summary>
    /// Editor tool to batch-generate test room templates of various sizes and shapes.
    /// Attach this script to any Node in a scene, then click "Generate All Test Rooms" in the Inspector.
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class RoomTemplateGenerator : Node
    {
        private const string OutputDir = "res://samples/2d_rooms/test_rooms/";
        private const string TemplateDir = "res://samples/2d_rooms/test_rooms/templates/";
        private const string TemplateGroupDir = "res://samples/2d_rooms/template_groups/";
        private const int CellPx = 96;

#if GODOT4_4_0_OR_GREATER
        [ExportToolButton("Generate All Test Rooms")]
        public Callable GenerateButton => Callable.From(GenerateAll);
#else
        [Export]
        public bool GenerateAllTestRooms { get => false; set { if (value) GenerateAll(); } }
#endif

        private int _nextId = 2000001;
        private int NextId() => _nextId++;

        public void GenerateAll()
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDir));
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(TemplateDir));

            var all = new List<string>(); // paths to .room_template.tres

            // ===== Rectangular rooms =====
            all.Add(MakeRoom("room_1x1", 1, 1, Full(1, 1)));
            all.Add(MakeRoom("room_1x2", 1, 2, Full(1, 2)));
            all.Add(MakeRoom("room_2x1", 2, 1, Full(2, 1)));
            all.Add(MakeRoom("room_2x2", 2, 2, Full(2, 2)));
            all.Add(MakeRoom("room_1x3", 1, 3, Full(1, 3)));
            all.Add(MakeRoom("room_3x2", 3, 2, Full(3, 2)));
            all.Add(MakeRoom("room_2x3", 2, 3, Full(2, 3)));
            all.Add(MakeRoom("room_3x3", 3, 3, Full(3, 3)));
            all.Add(MakeRoom("room_4x3", 4, 3, Full(4, 3)));

            // ===== L-shaped rooms =====
            all.Add(MakeRoom("room_L_2x2", 2, 2, new[] {
                new[] { true, false },
                new[] { true, true },
            }));
            all.Add(MakeRoom("room_L_3x2", 3, 2, new[] {
                new[] { true, false },
                new[] { true, false },
                new[] { true, true },
            }));
            all.Add(MakeRoom("room_L_3x3", 3, 3, new[] {
                new[] { true, false, false },
                new[] { true, false, false },
                new[] { true, true, true },
            }));

            // ===== T-shaped rooms =====
            all.Add(MakeRoom("room_T_2x3", 2, 3, new[] {
                new[] { true, true, true },
                new[] { false, true, false },
            }));
            all.Add(MakeRoom("room_T_3x3", 3, 3, new[] {
                new[] { true, true, true },
                new[] { false, true, false },
                new[] { false, true, false },
            }));
            all.Add(MakeRoom("room_T_inv_3x3", 3, 3, new[] {
                new[] { false, true, false },
                new[] { false, true, false },
                new[] { true, true, true },
            }));
            all.Add(MakeRoom("room_T_4x3", 4, 3, new[] {
                new[] { true, true, true },
                new[] { false, true, false },
                new[] { false, true, false },
                new[] { false, true, false },
            }));

            // Create template group .tres by writing text file
            SaveTemplateGroup("test_all_rooms", "Test All Rooms", all);

            GD.Print($"[RoomTemplateGenerator] Done! Generated {all.Count} room templates.");
            GD.Print("[RoomTemplateGenerator] Next steps:");
            GD.Print("  1. Open 2d_room_template_database.tres, add test_all_rooms.tres to TemplateGroups array");
            GD.Print("  2. Open cross_graph_2d.tres, change each node's TemplateGroup to 'test_all_rooms'");
            GD.Print("  3. Run the scene and click Generate");
        }

        // ==================== Room creation ====================

        private struct DoorDef
        {
            public int Row, Col, Dir; // 0=North, 1=South, 2=East, 3=West
        }

        private string MakeRoom(string name, int rows, int cols, bool[][] cells)
        {
            int id = NextId();
            var doors = FindOuterDoors(rows, cols, cells);

            // Write .tscn file directly as text
            var scenePath = OutputDir + name + ".tscn";
            WriteTscn(scenePath, name, rows, cols, cells, doors);

            // Write .room_template.tres with serialized ManiaMap JSON
            var tplPath = TemplateDir + name + ".room_template.tres";
            var json = BuildTemplateJson(id, name, rows, cols, cells, doors);
            WriteTres(tplPath, id, name, scenePath, json);

            GD.Print($"  {name} ({rows}x{cols}), {doors.Count} doors");
            return tplPath;
        }

        // ==================== .tscn generation ====================

        private static void WriteTscn(string path, string name, int rows, int cols, bool[][] cells, List<DoorDef> doors)
        {
            var sb = new StringBuilder();

            // Count sub-resources: door nodes + cell ColorRects
            int doorCount = doors.Count;

            sb.AppendLine("[gd_scene load_steps=2 format=3]");
            sb.AppendLine();
            sb.AppendLine("[ext_resource type=\"Script\" path=\"res://addons/mpewsey.maniamap/scripts/runtime/RoomNode2D.cs\" id=\"1_room\"]");
            sb.AppendLine("[ext_resource type=\"Script\" path=\"res://addons/mpewsey.maniamap/scripts/runtime/DoorNode2D.cs\" id=\"2_door\"]");
            sb.AppendLine();

            // Root node: RoomNode2D
            sb.AppendLine($"[node name=\"{name}\" type=\"Node2D\"]");
            sb.AppendLine("script = ExtResource(\"1_room\")");
            sb.AppendLine($"Rows = {rows}");
            sb.AppendLine($"Columns = {cols}");
            sb.Append("ActiveCells = [");
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) sb.Append(", ");
                sb.Append('[');
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append(cells[r][c] ? "true" : "false");
                }
                sb.Append(']');
            }
            sb.AppendLine("]");
            sb.AppendLine();

            // Visual cells: ColorRect per active cell
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!cells[r][c]) continue;
                    float x = c * CellPx + 2;
                    float y = r * CellPx + 2;
                    float w = CellPx - 4;
                    float h = CellPx - 4;
                    sb.AppendLine($"[node name=\"Cell_r{r}c{c}\" type=\"ColorRect\" parent=\".\"]");
                    sb.AppendLine($"offset_left = {x:F1}");
                    sb.AppendLine($"offset_top = {y:F1}");
                    sb.AppendLine($"offset_right = {x + w:F1}");
                    sb.AppendLine($"offset_bottom = {y + h:F1}");
                    sb.AppendLine("color = Color(0.25, 0.28, 0.35, 1)");
                    sb.AppendLine();
                }
            }

            // Doors container
            sb.AppendLine("[node name=\"Doors\" type=\"Node2D\" parent=\".\"]");
            sb.AppendLine();

            // Door nodes
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors[i];
                string dirName = d.Dir switch { 0 => "North", 1 => "South", 2 => "East", _ => "West" };

                float x = d.Col * CellPx + CellPx / 2f;
                float y = d.Row * CellPx + CellPx / 2f;
                switch (d.Dir)
                {
                    case 0: y = d.Row * CellPx + 16; break;
                    case 1: y = (d.Row + 1) * CellPx - 16; break;
                    case 2: x = (d.Col + 1) * CellPx - 16; break;
                    case 3: x = d.Col * CellPx + 16; break;
                }

                sb.AppendLine($"[node name=\"Door_{dirName}_r{d.Row}c{d.Col}\" type=\"Node2D\" parent=\"Doors\"]");
                sb.AppendLine($"position = Vector2({x:F0}, {y:F0})");
                sb.AppendLine("script = ExtResource(\"2_door\")");
                sb.AppendLine("AutoAssignDirection = false");
                sb.AppendLine($"DoorDirection = {d.Dir}");
                sb.AppendLine($"Row = {d.Row}");
                sb.AppendLine($"Column = {d.Col}");
                sb.AppendLine();
            }

            // Write file
            var globalPath = ProjectSettings.GlobalizePath(path);
            var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(sb.ToString());
            file.Close();
        }

        // ==================== .room_template.tres generation ====================

        private static void WriteTres(string path, int id, string name, string scenePath, string json)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[gd_resource type=\"Resource\" script_class=\"RoomTemplateResource\" load_steps=2 format=3]");
            sb.AppendLine();
            sb.AppendLine("[ext_resource type=\"Script\" path=\"res://addons/mpewsey.maniamap/scripts/runtime/RoomTemplateResource.cs\" id=\"1_script\"]");
            sb.AppendLine();
            sb.AppendLine("[resource]");
            sb.AppendLine("script = ExtResource(\"1_script\")");
            sb.AppendLine("EditId = false");
            sb.AppendLine($"Id = {id}");
            sb.AppendLine($"TemplateName = \"{name}\"");
            sb.AppendLine($"ScenePath = \"{scenePath}\"");
            sb.AppendLine("SceneUidPath = \"\"");
            // JSON needs to be escaped for .tres format (double quotes inside string)
            sb.AppendLine($"SerializedText = \"{EscapeTresString(json)}\"");

            var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(sb.ToString());
            file.Close();
        }

        private static string EscapeTresString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // ==================== Template group .tres ====================

        private static void SaveTemplateGroup(string fileName, string groupName, List<string> templatePaths)
        {
            var sb = new StringBuilder();

            // Count: 1 for TemplateGroup script + 1 for TemplateGroupEntry script + N for template resources
            int loadSteps = 2 + templatePaths.Count;
            sb.AppendLine($"[gd_resource type=\"Resource\" script_class=\"TemplateGroup\" load_steps={loadSteps} format=3]");
            sb.AppendLine();
            sb.AppendLine("[ext_resource type=\"Script\" path=\"res://addons/mpewsey.maniamap/scripts/runtime/TemplateGroup.cs\" id=\"1_tg\"]");
            sb.AppendLine("[ext_resource type=\"Script\" path=\"res://addons/mpewsey.maniamap/scripts/runtime/TemplateGroupEntry.cs\" id=\"2_tge\"]");

            for (int i = 0; i < templatePaths.Count; i++)
            {
                sb.AppendLine($"[ext_resource type=\"Resource\" path=\"{templatePaths[i]}\" id=\"tpl_{i}\"]");
            }
            sb.AppendLine();

            // Sub-resources: TemplateGroupEntry for each template
            for (int i = 0; i < templatePaths.Count; i++)
            {
                sb.AppendLine($"[sub_resource type=\"Resource\" id=\"entry_{i}\"]");
                sb.AppendLine("script = ExtResource(\"2_tge\")");
                sb.AppendLine($"RoomTemplate = ExtResource(\"tpl_{i}\")");
                sb.AppendLine();
            }

            // Main resource
            sb.AppendLine("[resource]");
            sb.AppendLine("script = ExtResource(\"1_tg\")");
            sb.AppendLine($"Name = \"{groupName}\"");
            sb.Append("Entries = [");
            for (int i = 0; i < templatePaths.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"SubResource(\"entry_{i}\")");
            }
            sb.AppendLine("]");

            var path = TemplateGroupDir + fileName + ".tres";
            var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(sb.ToString());
            file.Close();
            GD.Print($"  Template group saved: {path}");
        }

        // ==================== Door auto-detection ====================

        private static List<DoorDef> FindOuterDoors(int rows, int cols, bool[][] cells)
        {
            var list = new List<DoorDef>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!cells[r][c]) continue;
                    if (r == 0 || !cells[r - 1][c])
                        list.Add(new DoorDef { Row = r, Col = c, Dir = 0 });
                    if (r == rows - 1 || !cells[r + 1][c])
                        list.Add(new DoorDef { Row = r, Col = c, Dir = 1 });
                    if (c == cols - 1 || !cells[r][c + 1])
                        list.Add(new DoorDef { Row = r, Col = c, Dir = 2 });
                    if (c == 0 || !cells[r][c - 1])
                        list.Add(new DoorDef { Row = r, Col = c, Dir = 3 });
                }
            }
            return list;
        }

        // ==================== ManiaMap JSON serialization ====================

        private static string BuildTemplateJson(int id, string name, int rows, int cols,
            bool[][] cells, List<DoorDef> doors)
        {
            var doorMap = new Dictionary<(int, int), List<int>>();
            foreach (var d in doors)
            {
                var key = (d.Row, d.Col);
                if (!doorMap.ContainsKey(key))
                    doorMap[key] = new List<int>();
                doorMap[key].Add(d.Dir);
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"Id\":{id},\"Name\":\"{name}\",");
            sb.Append($"\"Cells\":{{\"Rows\":{rows},\"Columns\":{cols},\"Array\":[");

            bool firstCell = true;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!firstCell) sb.Append(',');
                    firstCell = false;

                    if (!cells[r][c])
                    {
                        sb.Append("null");
                        continue;
                    }

                    sb.Append("{\"Doors\":{\"Map\":[");
                    if (doorMap.TryGetValue((r, c), out var dirList))
                    {
                        bool firstDoor = true;
                        foreach (var dir in dirList)
                        {
                            if (!firstDoor) sb.Append(',');
                            firstDoor = false;
                            sb.Append($"{{\"Key\":{dir},\"Value\":{{\"Type\":0,\"Code\":0}}}}");
                        }
                    }
                    sb.Append("]},\"Features\":[]}");
                }
            }

            sb.Append("]},\"CollectableSpots\":{\"Map\":[]}}");
            return sb.ToString();
        }

        private static bool[][] Full(int rows, int cols)
        {
            var c = new bool[rows][];
            for (int r = 0; r < rows; r++)
            {
                c[r] = new bool[cols];
                for (int j = 0; j < cols; j++)
                    c[r][j] = true;
            }
            return c;
        }
    }
}
#endif
