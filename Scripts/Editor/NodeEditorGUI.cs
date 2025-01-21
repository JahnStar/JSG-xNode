using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XNodeEditor.Internal;
#if UNITY_2019_1_OR_NEWER && USE_ADVANCED_GENERIC_MENU
using GenericMenu = XNodeEditor.AdvancedGenericMenu;
#endif

namespace XNodeEditor {
    /// <summary> Contains GUI methods </summary>
    public partial class NodeEditorWindow {
        public NodeGraphEditor graphEditor;
        private readonly HashSet<UnityEngine.Object> selectionCache = new();
        private readonly HashSet<XNode.Node> culledNodes = new();
        /// <summary> before fix: 19 if docked, 22 if not </summary>
        private int topPadding { get { return isDocked() ? 24 : 27; } } // v1.9.3 fix
        /// <summary> Executed after all other window GUI. Useful if Zoom is ruining your day. Automatically resets after being run.</summary>
        public event Action onLateGUI;
        private static readonly Vector3[] polyLineTempArray = new Vector3[2];

        protected virtual void OnGUI() {
            Event e = Event.current;
            Matrix4x4 m = GUI.matrix;
            if (graph == null) return;
            ValidateGraphEditor();
            Controls();

            DrawGrid(position, zoom, panOffset);
            DrawConnections();
            DrawDraggedConnection();
            DrawNodes();
            DrawSelectionBox();
            DrawTooltip();
            graphEditor.OnGUI();
            DrawToolbar();

            // Run and reset onLateGUI
            if (onLateGUI != null) {
                onLateGUI();
                onLateGUI = null;
            }

            GUI.matrix = m;
        }

        public static void BeginZoomed(Rect rect, float zoom, float topPadding) {
            GUI.EndClip();

            GUIUtility.ScaleAroundPivot(Vector2.one / zoom, rect.size * 0.5f);
            Vector4 padding = new Vector4(0, topPadding, 0, 0);
            padding *= zoom;
            GUI.BeginClip(new Rect(-((rect.width * zoom) - rect.width) * 0.5f, -(((rect.height * zoom) - rect.height) * 0.5f) + (topPadding * zoom),
                rect.width * zoom,
                rect.height * zoom));
        }

        public static void EndZoomed(Rect rect, float zoom, float topPadding) {
            GUIUtility.ScaleAroundPivot(Vector2.one * zoom, rect.size * 0.5f);
            Vector3 offset = new Vector3(
                (((rect.width * zoom) - rect.width) * 0.5f),
                (((rect.height * zoom) - rect.height) * 0.5f) + (-topPadding * zoom) + topPadding,
                0);
            GUI.matrix = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one);
        }

        public void DrawGrid(Rect rect, float zoom, Vector2 panOffset) {

            rect.position = Vector2.zero;

            Vector2 center = rect.size / 2f;
            Texture2D gridTex = graphEditor.GetGridTexture();
            Texture2D crossTex = graphEditor.GetSecondaryGridTexture();

            // Offset from origin in tile units
            float xOffset = -(center.x * zoom + panOffset.x) / gridTex.width;
            float yOffset = ((center.y - rect.size.y) * zoom + panOffset.y) / gridTex.height;

            Vector2 tileOffset = new Vector2(xOffset, yOffset);

            // Amount of tiles
            float tileAmountX = Mathf.Round(rect.size.x * zoom) / gridTex.width;
            float tileAmountY = Mathf.Round(rect.size.y * zoom) / gridTex.height;

            Vector2 tileAmount = new Vector2(tileAmountX, tileAmountY);

            // Draw tiled background
            GUI.DrawTextureWithTexCoords(rect, gridTex, new Rect(tileOffset, tileAmount));
            GUI.DrawTextureWithTexCoords(rect, crossTex, new Rect(tileOffset + new Vector2(0.5f, 0.5f), tileAmount));
        }

        public void DrawSelectionBox() {
            if (currentActivity == NodeActivity.DragGrid) {
                Vector2 curPos = WindowToGridPosition(Event.current.mousePosition);
                Vector2 size = curPos - dragBoxStart;
                Rect r = new Rect(dragBoxStart, size);
                r.position = GridToWindowPosition(r.position);
                r.size /= zoom;
                Handles.DrawSolidRectangleWithOutline(r, new Color(0, 0, 0, 0.1f), new Color(1, 1, 1, 0.6f));
            }
        }

        public static bool DropdownButton(string name, float width) {
            return GUILayout.Button(name, EditorStyles.toolbarDropDown, GUILayout.Width(width));
        }

        /// <summary> Show right-click context menu for hovered reroute </summary>
        void ShowRerouteContextMenu(RerouteReference reroute) {
            GenericMenu contextMenu = new GenericMenu();
            contextMenu.AddItem(new GUIContent("Remove"), false, () => reroute.RemovePoint());
            contextMenu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
        }

        /// <summary> Show right-click context menu for hovered port </summary>
        void ShowPortContextMenu(XNode.NodePort hoveredPort) {
            GenericMenu contextMenu = new GenericMenu();
            foreach (var port in hoveredPort.GetConnections()) {
                var name = port.node.name;
                var index = hoveredPort.GetConnectionIndex(port);
                contextMenu.AddItem(new GUIContent(string.Format("Disconnect({0})", name)), false, () => hoveredPort.Disconnect(index));
            }
            contextMenu.AddItem(new GUIContent("Clear Connections"), false, () => hoveredPort.ClearConnections());
            //Get compatible nodes with this port
            if (NodeEditorPreferences.GetSettings().createFilter) {
                contextMenu.AddSeparator("");

                if (hoveredPort.direction == XNode.NodePort.IO.Input)
                    graphEditor.AddContextMenuItems(contextMenu, hoveredPort.ValueType, XNode.NodePort.IO.Output);
                else
                    graphEditor.AddContextMenuItems(contextMenu, hoveredPort.ValueType, XNode.NodePort.IO.Input);
            }
            contextMenu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
        }

        static Vector2 CalculateBezierPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            float u = 1 - t;
            float tt = t * t, uu = u * u;
            float uuu = uu * u, ttt = tt * t;
            return new Vector2(
                (uuu * p0.x) + (3 * uu * t * p1.x) + (3 * u * tt * p2.x) + (ttt * p3.x),
                (uuu * p0.y) + (3 * uu * t * p1.y) + (3 * u * tt * p2.y) + (ttt * p3.y)
            );
        }

        /// <summary> Draws a line segment without allocating temporary arrays </summary>
        static void DrawAAPolyLineNonAlloc(float thickness, Vector2 p0, Vector2 p1) {
            polyLineTempArray[0].x = p0.x;
            polyLineTempArray[0].y = p0.y;
            polyLineTempArray[1].x = p1.x;
            polyLineTempArray[1].y = p1.y;
            Handles.DrawAAPolyLine(thickness, polyLineTempArray);
        }

        /// <summary> Draw a bezier from output to input in grid coordinates </summary>
        public void DrawNoodle(Gradient gradient, NoodlePath path, NoodleStroke stroke, float thickness, List<Vector2> gridPoints) {
            // convert grid points to window points
            for (int i = 0; i < gridPoints.Count; ++i)
                gridPoints[i] = GridToWindowPosition(gridPoints[i]);

            Color originalHandlesColor = Handles.color;
            Handles.color = gradient.Evaluate(0f);
            int length = gridPoints.Count;
            switch (path) {
                case NoodlePath.Curvy:
                    Vector2 outputTangent = Vector2.right;
                    for (int i = 0; i < length - 1; i++) {
                        Vector2 inputTangent;
                        // Cached most variables that repeat themselves here to avoid so many indexer calls :p
                        Vector2 point_a = gridPoints[i];
                        Vector2 point_b = gridPoints[i + 1];
                        float dist_ab = Vector2.Distance(point_a, point_b);
                        if (i == 0) outputTangent = zoom * dist_ab * 0.01f * Vector2.right;
                        if (i < length - 2) {
                            Vector2 point_c = gridPoints[i + 2];
                            Vector2 ab = (point_b - point_a).normalized;
                            Vector2 cb = (point_b - point_c).normalized;
                            Vector2 ac = (point_c - point_a).normalized;
                            Vector2 p = (ab + cb) * 0.5f;
                            float tangentLength = (dist_ab + Vector2.Distance(point_b, point_c)) * 0.005f * zoom;
                            float side = ((ac.x * (point_b.y - point_a.y)) - (ac.y * (point_b.x - point_a.x)));

                            p = tangentLength * Mathf.Sign(side) * new Vector2(-p.y, p.x);
                            inputTangent = p;
                        } else {
                            inputTangent = zoom * dist_ab * 0.01f * Vector2.left;
                        }

                        // Calculates the tangents for the bezier's curves.
                        float zoomCoef = 50 / zoom;
                        Vector2 tangent_a = point_a + outputTangent * zoomCoef;
                        Vector2 tangent_b = point_b + inputTangent * zoomCoef;
                        // Hover effect.
                        int division = Mathf.RoundToInt(.2f * dist_ab) + 3;
                        // Coloring and bezier drawing.
                        int draw = 0;
                        Vector2 bezierPrevious = point_a;
                        for (int j = 1; j <= division; ++j) {
                            if (stroke == NoodleStroke.Dashed) {
                                draw++;
                                if (draw >= 2) draw = -2;
                                if (draw < 0) continue;
                                if (draw == 0) bezierPrevious = CalculateBezierPoint(point_a, tangent_a, tangent_b, point_b, (j - 1f) / (float) division);
                            }
                            if (i == length - 2)
                                Handles.color = gradient.Evaluate((j + 1f) / division);
                            Vector2 bezierNext = CalculateBezierPoint(point_a, tangent_a, tangent_b, point_b, j / (float) division);
                            DrawAAPolyLineNonAlloc(thickness, bezierPrevious, bezierNext);
                            bezierPrevious = bezierNext;
                        }
                        outputTangent = -inputTangent;
                    }
                    break;
                case NoodlePath.Straight:
                    for (int i = 0; i < length - 1; i++) {
                        Vector2 point_a = gridPoints[i];
                        Vector2 point_b = gridPoints[i + 1];
                        // Draws the line with the coloring.
                        Vector2 prev_point = point_a;
                        // Approximately one segment per 5 pixels
                        int segments = (int) Vector2.Distance(point_a, point_b) / 5;
                        segments = Math.Max(segments, 1);

                        int draw = 0;
                        for (int j = 0; j <= segments; j++) {
                            draw++;
                            float t = j / (float) segments;
                            Vector2 lerp = Vector2.Lerp(point_a, point_b, t);
                            if (draw > 0) {
                                if (i == length - 2) Handles.color = gradient.Evaluate(t);
                                DrawAAPolyLineNonAlloc(thickness, prev_point, lerp);
                            }
                            prev_point = lerp;
                            if (stroke == NoodleStroke.Dashed && draw >= 2) draw = -2;
                        }
                    }
                    break;
                case NoodlePath.Angled:
                    for (int i = 0; i < length - 1; i++) {
                        if (i == length - 1) continue; // Skip last index
                        if (gridPoints[i].x <= gridPoints[i + 1].x - (50 / zoom)) {
                            float midpoint = (gridPoints[i].x + gridPoints[i + 1].x) * 0.5f;
                            Vector2 start_1 = gridPoints[i];
                            Vector2 end_1 = gridPoints[i + 1];
                            start_1.x = midpoint;
                            end_1.x = midpoint;
                            if (i == length - 2) {
                                DrawAAPolyLineNonAlloc(thickness, gridPoints[i], start_1);
                                Handles.color = gradient.Evaluate(0.5f);
                                DrawAAPolyLineNonAlloc(thickness, start_1, end_1);
                                Handles.color = gradient.Evaluate(1f);
                                DrawAAPolyLineNonAlloc(thickness, end_1, gridPoints[i + 1]);
                            } else {
                                DrawAAPolyLineNonAlloc(thickness, gridPoints[i], start_1);
                                DrawAAPolyLineNonAlloc(thickness, start_1, end_1);
                                DrawAAPolyLineNonAlloc(thickness, end_1, gridPoints[i + 1]);
                            }
                        } else {
                            float midpoint = (gridPoints[i].y + gridPoints[i + 1].y) * 0.5f;
                            Vector2 start_1 = gridPoints[i];
                            Vector2 end_1 = gridPoints[i + 1];
                            start_1.x += 25 / zoom;
                            end_1.x -= 25 / zoom;
                            Vector2 start_2 = start_1;
                            Vector2 end_2 = end_1;
                            start_2.y = midpoint;
                            end_2.y = midpoint;
                            if (i == length - 2) {
                                DrawAAPolyLineNonAlloc(thickness, gridPoints[i], start_1);
                                Handles.color = gradient.Evaluate(0.25f);
                                DrawAAPolyLineNonAlloc(thickness, start_1, start_2);
                                Handles.color = gradient.Evaluate(0.5f);
                                DrawAAPolyLineNonAlloc(thickness, start_2, end_2);
                                Handles.color = gradient.Evaluate(0.75f);
                                DrawAAPolyLineNonAlloc(thickness, end_2, end_1);
                                Handles.color = gradient.Evaluate(1f);
                                DrawAAPolyLineNonAlloc(thickness, end_1, gridPoints[i + 1]);
                            } else {
                                DrawAAPolyLineNonAlloc(thickness, gridPoints[i], start_1);
                                DrawAAPolyLineNonAlloc(thickness, start_1, start_2);
                                DrawAAPolyLineNonAlloc(thickness, start_2, end_2);
                                DrawAAPolyLineNonAlloc(thickness, end_2, end_1);
                                DrawAAPolyLineNonAlloc(thickness, end_1, gridPoints[i + 1]);
                            }
                        }
                    }
                    break;
                case NoodlePath.ShaderLab:
                    Vector2 start = gridPoints[0];
                    Vector2 end = gridPoints[length - 1];
                    //Modify first and last point in array so we can loop trough them nicely.
                    gridPoints[0] = gridPoints[0] + Vector2.right * (20 / zoom);
                    gridPoints[length - 1] = gridPoints[length - 1] + Vector2.left * (20 / zoom);
                    //Draw first vertical lines going out from nodes
                    Handles.color = gradient.Evaluate(0f);
                    DrawAAPolyLineNonAlloc(thickness, start, gridPoints[0]);
                    Handles.color = gradient.Evaluate(1f);
                    DrawAAPolyLineNonAlloc(thickness, end, gridPoints[length - 1]);
                    for (int i = 0; i < length - 1; i++) {
                        Vector2 point_a = gridPoints[i];
                        Vector2 point_b = gridPoints[i + 1];
                        // Draws the line with the coloring.
                        Vector2 prev_point = point_a;
                        // Approximately one segment per 5 pixels
                        int segments = (int) Vector2.Distance(point_a, point_b) / 5;
                        segments = Math.Max(segments, 1);

                        int draw = 0;
                        for (int j = 0; j <= segments; j++) {
                            draw++;
                            float t = j / (float) segments;
                            Vector2 lerp = Vector2.Lerp(point_a, point_b, t);
                            if (draw > 0) {
                                if (i == length - 2) Handles.color = gradient.Evaluate(t);
                                DrawAAPolyLineNonAlloc(thickness, prev_point, lerp);
                            }
                            prev_point = lerp;
                            if (stroke == NoodleStroke.Dashed && draw >= 2) draw = -2;
                        }
                    }
                    gridPoints[0] = start;
                    gridPoints[length - 1] = end;
                    break;
            }
            Handles.color = originalHandlesColor;
        }

        /// <summary> Draws all connections </summary>
        public void DrawConnections() {
            Vector2 mousePos = Event.current.mousePosition;
            List<RerouteReference> selection = preBoxSelectionReroute != null ? new List<RerouteReference>(preBoxSelectionReroute) : new List<RerouteReference>();
            hoveredReroute = new RerouteReference();

            List<Vector2> gridPoints = new List<Vector2>(2);

            Color col = GUI.color;
            foreach (XNode.Node node in graph.nodes) {
                //If a null node is found, return. This can happen if the nodes associated script is deleted. It is currently not possible in Unity to delete a null asset.
                if (node == null) continue;

                // Draw full connections and output > reroute
                foreach (XNode.NodePort output in node.Outputs) {
                    //Needs cleanup. Null checks are ugly
                    Rect fromRect;
                    if (!_portConnectionPoints.TryGetValue(output, out fromRect))
                    {
                        // continue;
                        #region v1.8.2
                        if (node.drawNode) continue;
                        // Fallback to node position if rect not found
                        var position = node.position;
                        // get node width from the attribute
                        if (!NodeEditorReflection.TryGetAttributeWidth(node.GetType(), out int nodeWidth)) nodeWidth = 100; // handle default width
                        position.x += nodeWidth - 10;
                        position.y += 25;
                        fromRect = new Rect(position, Vector2.zero);
                        #endregion
                    }

                    Color portColor = graphEditor.GetPortColor(output);
                    GUIStyle portStyle = graphEditor.GetPortStyle(output);

                    for (int k = 0; k < output.ConnectionCount; k++) {
                        XNode.NodePort input = output.GetConnection(k);

                        Gradient noodleGradient = graphEditor.GetNoodleGradient(output, input);
                        float noodleThickness = graphEditor.GetNoodleThickness(output, input);
                        NoodlePath noodlePath = graphEditor.GetNoodlePath(output, input);
                        NoodleStroke noodleStroke = graphEditor.GetNoodleStroke(output, input);

                        #region v1.8.4
                        XNode.NodeGraph graph = output.node.graph;
                        if (graph.customNoodlePath != 0) noodlePath = (NoodlePath)(graph.customNoodlePath - 1);
                        #endregion

                        // Error handling
                        if (input == null) continue; //If a script has been updated and the port doesn't exist, it is removed and null is returned. If this happens, return.
                        if (!input.IsConnectedTo(output)) input.Connect(output);
                        Rect toRect;
                        if (!_portConnectionPoints.TryGetValue(input, out toRect))
                        {
                            // continue;
                            #region v1.8.2
                            // Fallback to connected node position if rect not found
                            var position = input.node.position;
                            position.y += 25;
                            position.x += 5;
                            toRect = new Rect(position, Vector2.zero);
                            #region v1.8.3
                            // Draw a text showing the number of connected inputs
                            // var endPosition = GridToWindowPosition(new Vector2(toRect.x + toRect.width - 5, toRect.y + toRect.height / 2 - 10f));
                            // int connectedInputs = input.node.Ports.Count(x => x.direction == XNode.NodePort.IO.Input && x.IsConnected);
                            // var headerPortCountStyle = new GUIStyle(NodeEditorResources.styles.nodeHeader) { fontSize = (int)(10 / zoom), normal = { textColor = portColor }};
                            // GUI.Label(new Rect(endPosition.x, endPosition.y, 0, 0), connectedInputs.ToString(), headerPortCountStyle);
                            #endregion
                            #endregion
                        }

                        List<Vector2> reroutePoints = output.GetReroutePoints(k);

                        gridPoints.Clear();
                        gridPoints.Add(fromRect.center);
                        gridPoints.AddRange(reroutePoints);
                        gridPoints.Add(toRect.center);
                        DrawNoodle(noodleGradient, noodlePath, noodleStroke, noodleThickness, gridPoints);

                        // Loop through reroute points again and draw the points
                        for (int i = 0; i < reroutePoints.Count; i++) {
                            RerouteReference rerouteRef = new RerouteReference(output, k, i);
                            // Draw reroute point at position
                            Rect rect = new Rect(reroutePoints[i], new Vector2(12, 12));
                            rect.position = new Vector2(rect.position.x - 6, rect.position.y - 6);
                            rect = GridToWindowRect(rect);

                            // Draw selected reroute points with an outline
                            if (selectedReroutes.Contains(rerouteRef)) {
                                GUI.color = NodeEditorPreferences.GetSettings().highlightColor;
                                GUI.DrawTexture(rect, portStyle.normal.background);
                            }

                            GUI.color = portColor;
                            GUI.DrawTexture(rect, portStyle.active.background);
                            if (rect.Overlaps(selectionBox)) selection.Add(rerouteRef);
                            if (rect.Contains(mousePos)) hoveredReroute = rerouteRef;

                        }
                    }
                }
            }
            GUI.color = col;
            if (Event.current.type != EventType.Layout && currentActivity == NodeActivity.DragGrid) selectedReroutes = selection;
        }

        private void DrawNodes() {
            Event e = Event.current;
            if (e.type == EventType.Layout) {
                selectionCache.Clear();
                var objs = Selection.objects;
                selectionCache.EnsureCapacity(objs.Length);
                foreach (var obj in objs)
                    selectionCache.Add(obj);
            }

            System.Reflection.MethodInfo onValidate = null;
            if (Selection.activeObject != null && Selection.activeObject is XNode.Node) {
                onValidate = Selection.activeObject.GetType().GetMethod("OnValidate");
                if (onValidate != null) EditorGUI.BeginChangeCheck();
            }

            BeginZoomed(position, zoom, topPadding);

            Vector2 mousePos = Event.current.mousePosition;

            if (e.type != EventType.Layout) {
                hoveredNode = null;
                hoveredPort = null;
            }

            List<UnityEngine.Object> preSelection = preBoxSelection != null ? new List<UnityEngine.Object>(preBoxSelection) : new List<UnityEngine.Object>();

            // Selection box stuff
            Vector2 boxStartPos = GridToWindowPositionNoClipped(dragBoxStart);
            Vector2 boxSize = mousePos - boxStartPos;
            if (boxSize.x < 0) { boxStartPos.x += boxSize.x; boxSize.x = Mathf.Abs(boxSize.x); }
            if (boxSize.y < 0) { boxStartPos.y += boxSize.y; boxSize.y = Mathf.Abs(boxSize.y); }
            Rect selectionBox = new Rect(boxStartPos, boxSize);

            //Save guiColor so we can revert it
            Color guiColor = GUI.color;

            List<XNode.NodePort> removeEntries = new List<XNode.NodePort>();

            if (e.type == EventType.Layout) culledNodes.Clear();
            for (int n = 0; n < graph.nodes.Count; n++) {
                // Skip null nodes. The user could be in the process of renaming scripts, so removing them at this point is not advisable.
                if (graph.nodes[n] == null) continue;
                if (n >= graph.nodes.Count) return;
                XNode.Node node = graph.nodes[n];

                // Culling
                if (e.type == EventType.Layout) {
                    // Cull unselected nodes outside view
                    if (!Selection.Contains(node) && ShouldBeCulled(node)) {
                        culledNodes.Add(node);
                        continue;
                    }
                } else if (culledNodes.Contains(node)) continue;

                if (e.type == EventType.Repaint) {
                    removeEntries.Clear();
                    foreach (var kvp in _portConnectionPoints)
                        if (kvp.Key.node == node) removeEntries.Add(kvp.Key);
                    foreach (var k in removeEntries) _portConnectionPoints.Remove(k);
                }

                NodeEditor nodeEditor = NodeEditor.GetEditor(node, this);

                NodeEditor.portPositions.Clear();

                // Set default label width. This is potentially overridden in OnBodyGUI
                EditorGUIUtility.labelWidth = 84;

                //Get node position
                Vector2 nodePos = GridToWindowPositionNoClipped(node.position);

                GUILayout.BeginArea(new Rect(nodePos, new Vector2(nodeEditor.GetWidth(), 4000)));

                bool selected = selectionCache.Contains(graph.nodes[n]);

                if (selected) {
                    GUIStyle style = new GUIStyle(nodeEditor.GetBodyStyle());
                    GUIStyle highlightStyle = new GUIStyle(nodeEditor.GetBodyHighlightStyle());
                    highlightStyle.padding = style.padding;
                    style.padding = new RectOffset();
                    GUI.color = nodeEditor.GetTint();
                    GUILayout.BeginVertical(style);
                    GUI.color = NodeEditorPreferences.GetSettings().highlightColor;
                    GUILayout.BeginVertical(new GUIStyle(highlightStyle));
                } else {
                    GUIStyle style = new GUIStyle(nodeEditor.GetBodyStyle());
                    GUI.color = nodeEditor.GetTint();
                    GUILayout.BeginVertical(style);
                }

                GUI.color = guiColor;
                EditorGUI.BeginChangeCheck();

                //Draw node contents
                nodeEditor.OnHeaderGUI();
                // nodeEditor.OnBodyGUI();
                #region v1.8.2
                var visibilityIconRect = GUILayoutUtility.GetLastRect();
                visibilityIconRect.size = new Vector2(16, 16);
                visibilityIconRect.x -= 8;
                visibilityIconRect.y += 25;
                if (node.drawNode)
                {
                    nodeEditor.OnBodyGUI();
                    var cachedNodeRect = GUILayoutUtility.GetLastRect();
                    visibilityIconRect.y = cachedNodeRect.y + cachedNodeRect.height - 5;
                }
                bool newDrawNode = GUI.Toggle(visibilityIconRect, node.drawNode, EditorGUIUtility.IconContent(node.drawNode ? "animationvisibilitytoggleon" : "animationvisibilitytoggleoff"), EditorStyles.label);
                if (newDrawNode != node.drawNode)
                {
                    node.drawNode = newDrawNode;
                    EditorUtility.SetDirty(Selection.activeObject = node);
                    nodeEditor.serializedObject.ApplyModifiedProperties();
                }
                #endregion
                //If user changed a value, notify other scripts through onUpdateNode
                if (EditorGUI.EndChangeCheck()) {
                    if (NodeEditor.onUpdateNode != null) NodeEditor.onUpdateNode(node);
                    EditorUtility.SetDirty(node);
                    nodeEditor.serializedObject.ApplyModifiedProperties();
                }

                GUILayout.EndVertical();

                //Cache data about the node for next frame
                if (e.type == EventType.Repaint) {
                    Vector2 size = GUILayoutUtility.GetLastRect().size;
                    if (nodeSizes.ContainsKey(node)) nodeSizes[node] = size;
                    else nodeSizes.Add(node, size);

                    foreach (var kvp in NodeEditor.portPositions) {
                        Vector2 portHandlePos = kvp.Value;
                        portHandlePos += node.position;
                        Rect rect = new Rect(portHandlePos.x - 8, portHandlePos.y - 8, 16, 16);
                        portConnectionPoints[kvp.Key] = rect;
                    }
                }

                if (selected) GUILayout.EndVertical();

                if (e.type != EventType.Layout) {
                    //Check if we are hovering this node
                    Vector2 nodeSize = GUILayoutUtility.GetLastRect().size;
                    Rect windowRect = new Rect(nodePos, nodeSize);
                    if (windowRect.Contains(mousePos)) hoveredNode = node;

                    //If dragging a selection box, add nodes inside to selection
                    if (currentActivity == NodeActivity.DragGrid) {
                        if (windowRect.Overlaps(selectionBox)) preSelection.Add(node);
                    }

                    //Check if we are hovering any of this nodes ports
                    //Check input ports
                    foreach (XNode.NodePort input in node.Inputs) {
                        //Check if port rect is available
                        if (!portConnectionPoints.ContainsKey(input)) continue;
                        Rect r = GridToWindowRectNoClipped(portConnectionPoints[input]);
                        if (r.Contains(mousePos)) hoveredPort = input;
                    }
                    //Check all output ports
                    foreach (XNode.NodePort output in node.Outputs) {
                        //Check if port rect is available
                        if (!portConnectionPoints.ContainsKey(output)) continue;
                        Rect r = GridToWindowRectNoClipped(portConnectionPoints[output]);
                        if (r.Contains(mousePos)) hoveredPort = output;
                    }
                }

                GUILayout.EndArea();
            }

            if (e.type != EventType.Layout && currentActivity == NodeActivity.DragGrid) Selection.objects = preSelection.ToArray();
            EndZoomed(position, zoom, topPadding);

            //If a change in is detected in the selected node, call OnValidate method.
            //This is done through reflection because OnValidate is only relevant in editor,
            //and thus, the code should not be included in build.
            if (onValidate != null && EditorGUI.EndChangeCheck()) onValidate.Invoke(Selection.activeObject, null);
        }

        private bool ShouldBeCulled(XNode.Node node) {

            Vector2 nodePos = GridToWindowPositionNoClipped(node.position);
            if (nodePos.x / _zoom > position.width) return true; // Right
            else if (nodePos.y / _zoom > position.height) return true; // Bottom
            else if (nodeSizes.ContainsKey(node)) {
                Vector2 size = nodeSizes[node];
                if (nodePos.x + size.x < 0) return true; // Left
                else if (nodePos.y + size.y < 0) return true; // Top
            }
            return false;
        }

        private void DrawTooltip() {
            if (!NodeEditorPreferences.GetSettings().portTooltips || graphEditor == null)
                return;
            string tooltip = null;
            if (hoveredPort != null) {
                tooltip = graphEditor.GetPortTooltip(hoveredPort);
            } else if (hoveredNode != null && IsHoveringNode && IsHoveringTitle(hoveredNode)) {
                tooltip = NodeEditor.GetEditor(hoveredNode, this).GetHeaderTooltip();
            }
            if (string.IsNullOrEmpty(tooltip)) return;
            GUIContent content = new GUIContent(tooltip);
            Vector2 size = NodeEditorResources.styles.tooltip.CalcSize(content);
            size.x += 8;
            Rect rect = new Rect(Event.current.mousePosition - (size), size);
            EditorGUI.LabelField(rect, content, NodeEditorResources.styles.tooltip);
            Repaint();
        }

        private XNode.Node objectDragHandler; // v1.9.1
        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.ExpandWidth(true));
            {
                GUILayout.Space(2);

                var leftAlignedButtonStyle = new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold }; // v1.8.6
                // Path toolbar
                #region v1.8.6
                if (graph?.parentGraph == graph || graph?.parentGraph?.parentGraph == graph)
                {
                    graph.parentGraph = null;
                    Debug.LogError("Circular reference error in graph hierarchy!");
                }

                var graphStack = new Stack<XNode.NodeGraph>();
                XNode.NodeGraph currentGraph = graph;
                while (currentGraph != null)
                {
                    graphStack.Push(currentGraph);
                    currentGraph = currentGraph.parentGraph;
                    if (graphStack.Contains(currentGraph))
                    {
                        graphStack = new();
                        GUILayout.Label(new GUIContent("...", "Circular reference detected!"), EditorStyles.helpBox);
                        graphStack.Push(graph);
                        break;
                    }
                }
                while (graphStack.Count > 0)
                {
                    var _graph = graphStack.Pop();
                    DrawGraphField(_graph);
                    if (_graph != graph) GUILayout.Label(">", EditorStyles.boldLabel);
                }
                // DrawGraphField(graph); // Removed
                #endregion

                void DrawGraphField(XNode.NodeGraph graph)
                {
                    var buttonContent = new GUIContent(graph.name, EditorGUIUtility.IconContent("d_ScriptableObject Icon").image);
                    var buttonWidth = leftAlignedButtonStyle.CalcSize(buttonContent).x - 45;

                    if (current != null && current.graph == graph) leftAlignedButtonStyle.fontStyle = FontStyle.Bold;
                    else leftAlignedButtonStyle.fontStyle = FontStyle.Normal;

                    if (GUILayout.Button(buttonContent, leftAlignedButtonStyle, GUILayout.Width(buttonWidth)))
                    {
                        if (current != null && current.graph != graph) current.graph = graph;
                        Selection.activeObject = graph;
                        EditorGUIUtility.PingObject(graph);
                    }
                }

                GUILayout.Space(10);

                GUILayout.FlexibleSpace();

                #region v1.8.4
                // Draw CustomNoodleStroke dropdown
                GUILayout.Label("Noodle", EditorStyles.miniLabel);
                graph.customNoodlePath = (XNode.NodeGraph.CustomNoodlePath)EditorGUILayout.EnumPopup("", graph.customNoodlePath, GUILayout.Width(64f));
                #endregion

                // Draw scale bar
                GUILayout.Label("Scale", EditorStyles.miniLabel);
                var newZoom = GUILayout.HorizontalSlider(zoom, 1f, 5f, GUILayout.MinWidth(40), GUILayout.MaxWidth(100) );
                GUILayout.Label(zoom.ToString("0.0#x"), EditorStyles.miniLabel, GUILayout.Width(30));
                if (Math.Abs(newZoom - zoom) > Mathf.Epsilon) zoom = newZoom;
            }
            GUILayout.EndHorizontal();

            #region v1.9.1 - Drag node object
            HandleSelectionDrag();
            #endregion

            void HandleSelectionDrag()
            {
                var selectedNode = Selection.activeObject as XNode.Node;
                if (selectedNode) objectDragHandler = selectedNode;
                if (objectDragHandler)
                {
                    GUILayout.Space(9);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    Event evt = Event.current;
                    GUILayout.Label($"{objectDragHandler.name} ({objectDragHandler.GetType().Name})", EditorStyles.miniLabel); // v1.9.2 - Show node type
                    Rect dragRect = GUILayoutUtility.GetRect(new GUIContent("Drag node"), GUI.skin.box, GUILayout.Height(19), GUILayout.Width(19));
                    dragRect.y -= 5;
                    dragRect.x -= 4;
                    GUI.Box(dragRect, EditorGUIUtility.IconContent("DotFill"), EditorStyles.miniLabel);
                    if (dragRect.Contains(evt.mousePosition)) EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.Link);

                    switch (evt.type) {
                        case EventType.MouseDown:
                            if (dragRect.Contains(evt.mousePosition)) {
                                DragAndDrop.PrepareStartDrag();
                                DragAndDrop.objectReferences = new UnityEngine.Object[] { objectDragHandler };
                                DragAndDrop.StartDrag("Dragging node: " + objectDragHandler.name);
                                evt.Use();
                            }
                            else objectDragHandler = null;
                            break;
                        case EventType.MouseDrag:
                            if (DragAndDrop.objectReferences.Length > 0) {
                                currentActivity = NodeActivity.DragNode;
                                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                                evt.Use();
                            }
                            break;
                        case EventType.MouseUp:
                            if (DragAndDrop.objectReferences.Length > 0) {
                                currentActivity = NodeActivity.Idle;
                                DragAndDrop.AcceptDrag();
                                evt.Use();
                            }
                            objectDragHandler = null;
                            break;
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }
    }
}
