using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AIGameFramework.Architecture
{
    public sealed partial class ArchitectureGraphWindow
    {
        private const float CardWidth = 230f;
        private const float CardMinHeight = 76f;
        private const float InterfaceRowHeight = 30f;
        private const float InterfaceRowGap = 4f;
        private const float InterfaceTopGap = 8f;
        private const float InterfaceBottomGap = 8f;

        private enum ConnectionKind { None, Ownership, InterfaceReference }

        private Vector2 _pan = new Vector2(40, 30);
        private float _zoom = 1f;
        private bool _panning;
        private bool _marqueeActive;
        private int _marqueeControlId;
        private Vector2 _marqueeStart;
        private Vector2 _marqueeCurrent;
        private readonly HashSet<string> _marqueeBaseSelection = new HashSet<string>();
        private string _movingNodeId;
        private string _moveSnapshot;
        private ConnectionKind _connectionKind;
        private int _connectionControlId;
        private string _connectionSourceId;
        private string _connectionInterfaceId;
        private Vector2 _connectionStartMouse;
        private Vector2 _connectionMouse;
        private string _strongSelectedInterfaceNodeId;
        private string _strongSelectedInterfaceId;
        private readonly HashSet<string> _collapsedNodeIds = new HashSet<string>();
        private readonly HashSet<string> _visibleNodeIds = new HashSet<string>();
        private readonly HashSet<string> _nodesWithChildren = new HashSet<string>();
        private Texture2D _normalCardTexture;
        private Texture2D _selectedCardTexture;
        private Texture2D _strongCardTexture;
        private GUIStyle _normalCardStyle;
        private GUIStyle _selectedCardStyle;
        private GUIStyle _strongCardStyle;

        private void DrawCanvas(Rect rect)
        {
            RefreshCanvasVisibility();
            PruneHiddenSelection();
            GUI.BeginGroup(rect);
            var bounds = new Rect(0, 0, rect.width, rect.height);
            EditorGUI.DrawRect(bounds, new Color(0.11f, 0.13f, 0.16f));
            HandleCanvasInput(bounds);
            DrawGrid(bounds);
            DrawOwnershipLines();
            foreach (var node in _document.nodes.Where(item => _visibleNodeIds.Contains(item.id)))
                DrawNodeCard(node);
            DrawInterfaceReferenceLines();
            DrawPorts();
            DrawConnectionPreview();
            DrawMarquee();
            GUI.EndGroup();
        }

        private void HandleCanvasInput(Rect bounds)
        {
            var e = Event.current;
            var mouse = e.mousePosition;
            var canvasControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.MouseLeaveWindow && _marqueeActive)
            {
                CancelMarquee();
                return;
            }
            if (e.type == EventType.MouseLeaveWindow && _connectionKind != ConnectionKind.None)
            {
                CancelConnection();
                return;
            }

            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                var paletteId = DragAndDrop.GetGenericData(PaletteDragKey) as string;
                if (bounds.Contains(mouse) && (paletteId == OrdinaryPaletteId || ArchitectureModel.FindTemplate(_document, paletteId) != null))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    var perform = e.type == EventType.DragPerform;
                    if (perform)
                    {
                        DragAndDrop.AcceptDrag();
                        ArchitectureNode created = null;
                        var worldPosition = CanvasToWorld(mouse);
                        if (Change(() =>
                        {
                            created = paletteId == OrdinaryPaletteId
                                ? ArchitectureModel.AddOrdinaryNode(_document, null, worldPosition)
                                : ArchitectureModel.InstantiateTemplate(_document, paletteId, null, worldPosition);
                        }))
                        {
                            SelectNode(created.id);
                            _status = string.Empty;
                        }
                    }
                    e.Use();
                    Repaint();
                    if (perform) GUIUtility.ExitGUI();
                }
                return;
            }

            if (e.type == EventType.ScrollWheel && bounds.Contains(mouse))
            {
                var world = CanvasToWorld(mouse);
                _zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.12f, -e.delta.y), 0.45f, 1.8f);
                _pan = mouse - world * _zoom;
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDown && bounds.Contains(mouse))
            {
                if (e.button == 2 || (e.button == 0 && e.alt))
                {
                    _panning = true;
                    e.Use();
                    return;
                }
                if (e.button != 0) return;

                var collapseNode = HitCollapseToggle(mouse);
                if (collapseNode != null)
                {
                    ToggleNodeCollapsed(collapseNode);
                    e.Use();
                    GUIUtility.ExitGUI();
                    return;
                }

                var interfaceSource = HitInterfaceRow(mouse, out var sourceInterface);
                if (interfaceSource != null && !e.shift && !e.control && !e.command)
                {
                    if (e.clickCount >= 2)
                    {
                        SelectNode(interfaceSource.id);
                        _strongSelectedInterfaceNodeId = interfaceSource.id;
                        _strongSelectedInterfaceId = sourceInterface.id;
                        _movingNodeId = _moveSnapshot = null;
                        e.Use();
                        GUIUtility.ExitGUI();
                        return;
                    }
                    _connectionKind = ConnectionKind.InterfaceReference;
                    _connectionControlId = canvasControlId;
                    GUIUtility.hotControl = canvasControlId;
                    _connectionSourceId = interfaceSource.id;
                    _connectionInterfaceId = sourceInterface.id;
                    _connectionStartMouse = _connectionMouse = mouse;
                    SelectNode(interfaceSource.id);
                    e.Use();
                    GUIUtility.ExitGUI();
                    return;
                }
                var ownershipSource = HitOwnershipOutput(mouse);
                if (ownershipSource != null)
                {
                    _connectionKind = ConnectionKind.Ownership;
                    _connectionControlId = canvasControlId;
                    GUIUtility.hotControl = canvasControlId;
                    _connectionSourceId = ownershipSource.id;
                    _connectionMouse = mouse;
                    e.Use();
                    return;
                }
                var nodeHit = HitNode(mouse);
                if (nodeHit != null)
                {
                    if (e.shift || e.control || e.command)
                    {
                        var selection = new HashSet<string>(_selectedNodeIds);
                        if (!selection.Add(nodeHit.id)) selection.Remove(nodeHit.id);
                        SetNodeSelection(selection);
                        e.Use();
                        GUIUtility.ExitGUI();
                        return;
                    }
                    if (!_selectedNodeIds.Contains(nodeHit.id) || _strongSelectedInterfaceId != null)
                        SelectNode(nodeHit.id);
                    _movingNodeId = nodeHit.id;
                    _moveSnapshot = JsonUtility.ToJson(_document);
                    e.Use();
                    GUIUtility.ExitGUI();
                    return;
                }
                _marqueeActive = true;
                _marqueeControlId = canvasControlId;
                GUIUtility.hotControl = canvasControlId;
                _marqueeStart = _marqueeCurrent = mouse;
                _marqueeBaseSelection.Clear();
                if (e.shift || e.control || e.command)
                    _marqueeBaseSelection.UnionWith(_selectedNodeIds);
                else
                    SetNodeSelection(Array.Empty<string>());
                e.Use();
                GUIUtility.ExitGUI();
                return;
            }

            if (e.type == EventType.MouseDrag)
            {
                if (_panning)
                {
                    _pan += e.delta;
                    e.Use();
                    Repaint();
                    return;
                }
                if (_marqueeActive)
                {
                    _marqueeCurrent = mouse;
                    UpdateMarqueeSelection();
                    e.Use();
                    GUIUtility.ExitGUI();
                    return;
                }
                if (_connectionKind != ConnectionKind.None)
                {
                    _connectionMouse = mouse;
                    e.Use();
                    Repaint();
                    return;
                }
                if (!string.IsNullOrEmpty(_movingNodeId))
                {
                    var ids = new HashSet<string>();
                    foreach (var selectedId in _selectedNodeIds)
                        ids.UnionWith(ArchitectureModel.SubtreeIds(_document, selectedId));
                    foreach (var node in _document.nodes.Where(item => ids.Contains(item.id)))
                        node.position += e.delta / _zoom;
                    e.Use();
                    Repaint();
                    return;
                }
            }

            if (e.type == EventType.MouseUp)
            {
                if (_marqueeActive)
                {
                    _marqueeCurrent = mouse;
                    _marqueeActive = false;
                    if (GUIUtility.hotControl == _marqueeControlId) GUIUtility.hotControl = 0;
                    _marqueeControlId = 0;
                    UpdateMarqueeSelection();
                    _marqueeBaseSelection.Clear();
                    e.Use();
                    Repaint();
                    GUIUtility.ExitGUI();
                    return;
                }
                if (_panning || !string.IsNullOrEmpty(_movingNodeId) ||
                    _connectionKind != ConnectionKind.None)
                {
                    var hadConnection = _connectionKind != ConnectionKind.None;
                    if (_connectionKind == ConnectionKind.Ownership) CompleteOwnershipConnection(mouse);
                    else if (_connectionKind == ConnectionKind.InterfaceReference &&
                             (mouse - _connectionStartMouse).sqrMagnitude >= 16f)
                        CompleteInterfaceReference(mouse);
                    if (!string.IsNullOrEmpty(_movingNodeId)) CommitCanvasMove(_moveSnapshot);
                    _panning = false;
                    _movingNodeId = _moveSnapshot = null;
                    if (hadConnection) CancelConnection();
                    e.Use();
                    Repaint();
                    if (hadConnection) GUIUtility.ExitGUI();
                }
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && _connectionKind != ConnectionKind.None)
            {
                CancelConnection();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && _marqueeActive)
            {
                CancelMarquee();
                e.Use();
            }
        }

        private void CancelMarquee()
        {
            _marqueeActive = false;
            if (_marqueeControlId != 0 && GUIUtility.hotControl == _marqueeControlId)
                GUIUtility.hotControl = 0;
            _marqueeControlId = 0;
            _marqueeBaseSelection.Clear();
            Repaint();
        }

        private void CancelConnection()
        {
            _connectionKind = ConnectionKind.None;
            _connectionSourceId = _connectionInterfaceId = null;
            if (_connectionControlId != 0 && GUIUtility.hotControl == _connectionControlId)
                GUIUtility.hotControl = 0;
            _connectionControlId = 0;
            Repaint();
        }

        private void ClearStrongInterfaceSelection()
        {
            _strongSelectedInterfaceNodeId = _strongSelectedInterfaceId = null;
        }

        private string CollapsedNodesPrefsKey => "AIArchitecture.CollapsedNodes." + Application.dataPath;

        private void LoadCollapsedNodeIds()
        {
            _collapsedNodeIds.Clear();
            foreach (var id in EditorPrefs.GetString(CollapsedNodesPrefsKey, string.Empty)
                         .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                _collapsedNodeIds.Add(id);
        }

        private void SaveCollapsedNodeIds()
        {
            EditorPrefs.SetString(CollapsedNodesPrefsKey, string.Join("\n", _collapsedNodeIds.OrderBy(id => id)));
        }

        private void PruneCollapsedNodeIds()
        {
            if (_document == null) return;
            var ids = new HashSet<string>(_document.nodes.Select(node => node.id));
            if (_collapsedNodeIds.RemoveWhere(id => !ids.Contains(id)) > 0) SaveCollapsedNodeIds();
        }

        private void RefreshCanvasVisibility()
        {
            _visibleNodeIds.Clear();
            _nodesWithChildren.Clear();
            if (_document == null) return;

            var childrenByParent = new Dictionary<string, List<ArchitectureNode>>();
            var pending = new Stack<ArchitectureNode>();
            foreach (var node in _document.nodes)
            {
                if (string.IsNullOrEmpty(node.parentId))
                {
                    pending.Push(node);
                    continue;
                }
                _nodesWithChildren.Add(node.parentId);
                if (!childrenByParent.TryGetValue(node.parentId, out var children))
                    childrenByParent[node.parentId] = children = new List<ArchitectureNode>();
                children.Add(node);
            }
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!_visibleNodeIds.Add(node.id) || _collapsedNodeIds.Contains(node.id) ||
                    !childrenByParent.TryGetValue(node.id, out var children)) continue;
                foreach (var child in children) pending.Push(child);
            }
        }

        private void PruneHiddenSelection()
        {
            var hidden = _selectedNodeIds.Where(id => !_visibleNodeIds.Contains(id)).ToArray();
            if (hidden.Length == 0) return;
            var visibleSelection = new HashSet<string>(_selectedNodeIds.Where(id => _visibleNodeIds.Contains(id)));
            foreach (var id in hidden)
            {
                for (var node = ArchitectureModel.FindNode(_document, id); node != null;
                     node = ArchitectureModel.FindNode(_document, node.parentId))
                {
                    if (!_visibleNodeIds.Contains(node.id)) continue;
                    visibleSelection.Add(node.id);
                    break;
                }
            }
            SetNodeSelection(visibleSelection);
        }

        private void ToggleNodeCollapsed(ArchitectureNode node)
        {
            if (!_collapsedNodeIds.Add(node.id))
                _collapsedNodeIds.ExceptWith(ArchitectureModel.SubtreeIds(_document, node.id));
            SaveCollapsedNodeIds();
            RefreshCanvasVisibility();
            PruneHiddenSelection();
            Repaint();
        }

        private void ExpandAncestors(ArchitectureNode node)
        {
            var changed = false;
            for (var parent = ArchitectureModel.FindNode(_document, node.parentId); parent != null;
                 parent = ArchitectureModel.FindNode(_document, parent.parentId))
                changed |= _collapsedNodeIds.Remove(parent.id);
            if (changed) SaveCollapsedNodeIds();
        }

        private void CompleteOwnershipConnection(Vector2 mouse)
        {
            var source = ArchitectureModel.FindNode(_document, _connectionSourceId);
            if (source == null) return;
            var target = HitNodeInput(mouse);
            if (target == null) return;
            if (!ArchitectureModel.CanReparent(_document, target, source))
            {
                _status = "归属连线无效：不能形成环，Root 和预制内部固定子节点也不能改挂。";
                return;
            }
            if (target.parentId != source.id)
            {
                if (Change(() =>
                    {
                        target.parentId = source.id;
                    }))
                {
                    _status = "模块归属已更新。";
                }
            }
        }

        private void CompleteInterfaceReference(Vector2 mouse)
        {
            var source = ArchitectureModel.FindNode(_document, _connectionSourceId);
            var consumer = HitNode(mouse);
            if (source == null || consumer == null || source.id == consumer.id) return;
            if (_document.interfaceReferences.Any(reference =>
                    reference.sourceNodeId == source.id && reference.interfaceId == _connectionInterfaceId &&
                    reference.consumerNodeId == consumer.id))
            {
                _status = "此接口引用已存在。";
                return;
            }
            if (Change(() => ArchitectureModel.AddInterfaceReference(_document,
                    source.id, _connectionInterfaceId, consumer.id)))
                _status = "接口引用已记录。";
        }

        private Vector2 WorldToCanvas(Vector2 world) { return _pan + world * _zoom; }
        private Vector2 CanvasToWorld(Vector2 canvas) { return (canvas - _pan) / _zoom; }

        private float NodeHeight(ArchitectureNode node)
        {
            return NodeHeightWorld(node) * _zoom;
        }

        private float NodeHeightWorld(ArchitectureNode node)
        {
            var count = ArchitectureModel.InterfaceItems(_document, node)?.Count ?? 0;
            return CardMinHeight + InterfaceListHeightWorld(count);
        }

        private static float InterfaceListHeightWorld(int count)
        {
            return count == 0 ? 0f : InterfaceTopGap + count * InterfaceRowHeight +
                (count - 1) * InterfaceRowGap + InterfaceBottomGap;
        }

        private Rect HeaderRect(ArchitectureNode node)
        {
            var point = WorldToCanvas(node.position);
            return new Rect(point.x, point.y, CardWidth * _zoom, CardMinHeight * _zoom);
        }

        private Rect NodeRect(ArchitectureNode node)
        {
            var point = WorldToCanvas(node.position);
            return new Rect(point.x, point.y, CardWidth * _zoom, NodeHeight(node));
        }

        private Rect InterfaceRect(ArchitectureNode node, int index)
        {
            return InterfaceRectAt(node.position, index);
        }

        private Rect InterfaceRectAt(Vector2 worldPosition, int index)
        {
            var point = WorldToCanvas(worldPosition + new Vector2(8f,
                CardMinHeight + InterfaceTopGap + index * (InterfaceRowHeight + InterfaceRowGap)));
            return new Rect(point.x, point.y, (CardWidth - 16f) * _zoom, InterfaceRowHeight * _zoom);
        }

        private static Vector2 OwnershipInput(Rect rect, float zoom) { return new Vector2(rect.center.x, rect.y + 5f * zoom); }
        private static Vector2 OwnershipOutput(Rect rect, float zoom) { return new Vector2(rect.center.x, rect.yMax - 5f * zoom); }

        private void DrawGrid(Rect bounds)
        {
            var step = 40f * _zoom;
            if (step < 18) step *= 2;
            var xStart = Mathf.Repeat(_pan.x, step);
            var yStart = Mathf.Repeat(_pan.y, step);
            var color = new Color(0.21f, 0.24f, 0.28f, 0.45f);
            for (var x = xStart; x < bounds.width; x += step)
                EditorGUI.DrawRect(new Rect(x, 0, 1, bounds.height), color);
            for (var y = yStart; y < bounds.height; y += step)
                EditorGUI.DrawRect(new Rect(0, y, bounds.width, 1), color);
        }

        private void DrawOwnershipLines()
        {
            foreach (var node in _document.nodes)
            {
                if (!_visibleNodeIds.Contains(node.id)) continue;
                var parent = ArchitectureModel.FindNode(_document, node.parentId);
                if (parent == null || !_visibleNodeIds.Contains(parent.id)) continue;
                var from = OwnershipOutput(NodeRect(parent), _zoom);
                var to = OwnershipInput(HeaderRect(node), _zoom);
                DrawVerticalConnection(from, to, new Color(0.35f, 0.72f, 1f), Mathf.Max(2f, 3f * _zoom));
            }
        }

        private void DrawInterfaceReferenceLines()
        {
            if (string.IsNullOrEmpty(_strongSelectedInterfaceNodeId) ||
                string.IsNullOrEmpty(_strongSelectedInterfaceId)) return;
            var color = new Color(1f, 0.66f, 0.28f);
            foreach (var reference in _document.interfaceReferences)
            {
                if (reference.sourceNodeId != _strongSelectedInterfaceNodeId ||
                    reference.interfaceId != _strongSelectedInterfaceId) continue;
                var source = ArchitectureModel.FindNode(_document, reference.sourceNodeId);
                var consumer = ArchitectureModel.FindNode(_document, reference.consumerNodeId);
                if (source == null || consumer == null || !_visibleNodeIds.Contains(source.id) ||
                    !_visibleNodeIds.Contains(consumer.id)) continue;
                var items = ArchitectureModel.InterfaceItems(_document, source);
                var index = items?.FindIndex(item => item != null && item.id == reference.interfaceId) ?? -1;
                if (index < 0) continue;
                DrawStraightLine(InterfaceRect(source, index).center, HeaderRect(consumer).center,
                    color, Mathf.Max(2f, 2.5f * _zoom));
            }
        }

        private static void DrawStraightLine(Vector2 from, Vector2 to, Color color, float width)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 0.1f) return;
            var previousMatrix = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
                EditorGUI.DrawRect(new Rect(from.x, from.y - width * 0.5f, length, width), color);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        // All strokes use IMGUI rectangles inside the canvas group. This keeps them in
        // the same clipped coordinate space as the cards and their hit targets.
        private static void DrawAxisSegment(Vector2 from, Vector2 to, Color color, float width)
        {
            var half = width * 0.5f;
            if (Mathf.Abs(from.x - to.x) < 0.1f)
                EditorGUI.DrawRect(new Rect(from.x - half, Mathf.Min(from.y, to.y) - half,
                    width, Mathf.Abs(to.y - from.y) + width), color);
            else
                EditorGUI.DrawRect(new Rect(Mathf.Min(from.x, to.x) - half, from.y - half,
                    Mathf.Abs(to.x - from.x) + width, width), color);
        }

        private static void DrawVerticalConnection(Vector2 from, Vector2 to, Color color, float width)
        {
            var middleY = (from.y + to.y) * 0.5f;
            var firstBend = new Vector2(from.x, middleY);
            var secondBend = new Vector2(to.x, middleY);
            DrawAxisSegment(from, firstBend, color, width);
            DrawAxisSegment(firstBend, secondBend, color, width);
            DrawAxisSegment(secondBend, to, color, width);
        }

        private void DrawNodeCard(ArchitectureNode node)
        {
            var template = ArchitectureModel.FindTemplate(_document, node.templateId);
            var moduleType = node.id == ArchitectureModel.RootId ? "根模块" :
                template == null ? "普通模块" : "预制模块 · " + template.name;
            DrawCard(HeaderRect(node), ArchitectureModel.DisplayName(_document, node), moduleType,
                _selectedNodeIds.Contains(node.id), _nodesWithChildren.Contains(node.id));
            if (_nodesWithChildren.Contains(node.id)) DrawCollapseToggle(node);
            DrawInterfaceRows(node.id, node.position, ArchitectureModel.InterfaceItems(_document, node),
                _selectedNodeIds.Contains(node.id));
        }

        private Rect CollapseToggleRect(ArchitectureNode node)
        {
            var header = HeaderRect(node);
            var size = Mathf.Max(20f, 24f * _zoom);
            return new Rect(header.xMax - size - 10f * _zoom, header.y + 9f * _zoom, size, size);
        }

        private void DrawCollapseToggle(ArchitectureNode node)
        {
            var rect = CollapseToggleRect(node);
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(17f * _zoom), 12, 22),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, _collapsedNodeIds.Contains(node.id) ? "+" : "-", style);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        private ArchitectureNode HitCollapseToggle(Vector2 mouse)
        {
            return _document.nodes.LastOrDefault(node => _visibleNodeIds.Contains(node.id) &&
                _nodesWithChildren.Contains(node.id) && CollapseToggleRect(node).Contains(mouse));
        }

        private void DrawInterfaceRows(string nodeId, Vector2 position,
            List<ArchitectureInterfaceItem> items, bool selected)
        {
            if (items == null) return;
            EnsureCardStyles();
            var labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(11 * _zoom), 9, 16),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };
            for (var index = 0; index < items.Count; index++)
            {
                var rect = InterfaceRectAt(position, index);
                var strong = nodeId == _strongSelectedInterfaceNodeId &&
                    items[index]?.id == _strongSelectedInterfaceId;
                var style = strong ? _strongCardStyle : selected
                    ? _selectedCardStyle : _normalCardStyle;
                GUI.Box(rect, GUIContent.none, style);
                GUI.Label(new Rect(rect.x + 12f * _zoom, rect.y + 3f * _zoom,
                    rect.width - 35f * _zoom, rect.height - 6f * _zoom),
                    items[index]?.title ?? string.Empty, labelStyle);
            }
        }

        private Rect MarqueeRect()
        {
            return Rect.MinMaxRect(Mathf.Min(_marqueeStart.x, _marqueeCurrent.x),
                Mathf.Min(_marqueeStart.y, _marqueeCurrent.y),
                Mathf.Max(_marqueeStart.x, _marqueeCurrent.x),
                Mathf.Max(_marqueeStart.y, _marqueeCurrent.y));
        }

        private void UpdateMarqueeSelection()
        {
            var selection = new HashSet<string>(_marqueeBaseSelection);
            var rect = MarqueeRect();
            if (rect.width >= 3f && rect.height >= 3f)
                foreach (var node in _document.nodes)
                    if (_visibleNodeIds.Contains(node.id) && rect.Overlaps(NodeRect(node)))
                        selection.Add(node.id);
            SetNodeSelection(selection);
        }

        private void DrawMarquee()
        {
            if (!_marqueeActive) return;
            var rect = MarqueeRect();
            if (rect.width < 3f || rect.height < 3f) return;
            EditorGUI.DrawRect(rect, new Color(0.35f, 0.68f, 1f, 0.12f));
            var border = new Color(0.60f, 0.82f, 1f, 0.9f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);
        }

        private void DrawCard(Rect rect, string name, string moduleType, bool selected, bool hasChildren)
        {
            EnsureCardStyles();
            GUI.Box(rect, GUIContent.none, selected ? _selectedCardStyle : _normalCardStyle);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(14 * _zoom), 10, 21),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };
            var typeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(11 * _zoom), 9, 16),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.78f, 0.85f, 0.91f) }
            };
            var left = rect.x + 17f * _zoom;
            var width = rect.width - 34f * _zoom;
            var titleWidth = hasChildren ? width - Mathf.Max(20f, 24f * _zoom) - 3f * _zoom : width;
            GUI.Label(new Rect(left, rect.y + 10f * _zoom, titleWidth, 26f * _zoom),
                (name ?? string.Empty).Replace('\n', ' '), titleStyle);
            GUI.Label(new Rect(left, rect.y + 39f * _zoom, width, 20f * _zoom),
                (moduleType ?? string.Empty).Replace('\n', ' '), typeStyle);
        }

        private void EnsureCardStyles()
        {
            if (_normalCardStyle != null && _selectedCardStyle != null && _strongCardStyle != null) return;
            _normalCardTexture = CreateCardTexture(new Color32(255, 255, 255, 255));
            _selectedCardTexture = CreateCardTexture(new Color32(174, 219, 255, 255));
            _strongCardTexture = CreateCardTexture(new Color32(51, 214, 255, 255));
            _normalCardStyle = CreateCardStyle(_normalCardTexture);
            _selectedCardStyle = CreateCardStyle(_selectedCardTexture);
            _strongCardStyle = CreateCardStyle(_strongCardTexture);
        }

        private static GUIStyle CreateCardStyle(Texture2D texture)
        {
            var style = new GUIStyle();
            style.normal.background = texture;
            style.border = new RectOffset(12, 12, 12, 12);
            return style;
        }

        private static Texture2D CreateCardTexture(Color32 borderColor)
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var px = x + 0.5f - size * 0.5f;
                    var py = y + 0.5f - size * 0.5f;
                    var outer = Mathf.Clamp01(0.5f - RoundedRectDistance(px, py, 15.5f, 10f));
                    var inner = Mathf.Clamp01(0.5f - RoundedRectDistance(px, py, 13.5f, 8f));
                    var border = outer > 0f ? Mathf.Clamp01((outer - inner) / outer) : 0f;
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.RoundToInt(borderColor.r * border),
                        (byte)Mathf.RoundToInt(borderColor.g * border),
                        (byte)Mathf.RoundToInt(borderColor.b * border),
                        (byte)Mathf.RoundToInt(outer * 255f));
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float RoundedRectDistance(float x, float y, float halfExtent, float radius)
        {
            var qx = Mathf.Abs(x) - (halfExtent - radius);
            var qy = Mathf.Abs(y) - (halfExtent - radius);
            return Mathf.Min(Mathf.Max(qx, qy), 0f) +
                   Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                              Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) - radius;
        }

        private void ReleaseCardStyles()
        {
            if (_normalCardTexture != null) UnityEngine.Object.DestroyImmediate(_normalCardTexture);
            if (_selectedCardTexture != null) UnityEngine.Object.DestroyImmediate(_selectedCardTexture);
            if (_strongCardTexture != null) UnityEngine.Object.DestroyImmediate(_strongCardTexture);
            _normalCardTexture = _selectedCardTexture = _strongCardTexture = null;
            _normalCardStyle = _selectedCardStyle = _strongCardStyle = null;
        }

        private void DrawPorts()
        {
            foreach (var node in _document.nodes)
            {
                if (!_visibleNodeIds.Contains(node.id)) continue;
                DrawCircle(OwnershipInput(HeaderRect(node), _zoom), new Color(0.37f, 0.72f, 1f));
                DrawCircle(OwnershipOutput(NodeRect(node), _zoom), new Color(0.37f, 0.72f, 1f));
            }
        }

        private void DrawCircle(Vector2 point, Color color)
        {
            var outer = Mathf.Max(5f, 7f * _zoom);
            var inner = Mathf.Max(3.4f, 4.9f * _zoom);
            DrawRoundedSquare(point, outer, new Color(0.05f, 0.08f, 0.10f));
            DrawRoundedSquare(point, inner, color);
        }

        private static void DrawRoundedSquare(Vector2 point, float radius, Color color)
        {
            EditorGUI.DrawRect(new Rect(point.x - radius * 0.72f, point.y - radius,
                radius * 1.44f, radius * 2f), color);
            EditorGUI.DrawRect(new Rect(point.x - radius, point.y - radius * 0.72f,
                radius * 2f, radius * 1.44f), color);
        }

        private void DrawConnectionPreview()
        {
            if (_connectionKind == ConnectionKind.None) return;
            var source = ArchitectureModel.FindNode(_document, _connectionSourceId);
            if (source == null || !_visibleNodeIds.Contains(source.id)) return;
            if (_connectionKind == ConnectionKind.InterfaceReference)
            {
                if ((_connectionMouse - _connectionStartMouse).sqrMagnitude < 16f) return;
                var items = ArchitectureModel.InterfaceItems(_document, source);
                var index = items?.FindIndex(item => item != null && item.id == _connectionInterfaceId) ?? -1;
                if (index >= 0)
                {
                    var consumer = HitNode(_connectionMouse);
                    var target = consumer != null && consumer.id != source.id
                        ? HeaderRect(consumer).center : _connectionMouse;
                    DrawStraightLine(InterfaceRect(source, index).center, target,
                        new Color(1f, 0.66f, 0.28f), 2.5f);
                }
                return;
            }
            var from = OwnershipOutput(NodeRect(source), _zoom);
            DrawVerticalConnection(from, _connectionMouse, new Color(0.38f, 0.73f, 1f), 2.5f);
        }

        private bool Near(Vector2 a, Vector2 b) { return (a - b).sqrMagnitude <= 12f * 12f; }

        private ArchitectureNode HitOwnershipOutput(Vector2 mouse)
        {
            return _document.nodes.LastOrDefault(node => _visibleNodeIds.Contains(node.id) &&
                Near(mouse, OwnershipOutput(NodeRect(node), _zoom)));
        }

        private ArchitectureNode HitNodeInput(Vector2 mouse)
        {
            return _document.nodes.LastOrDefault(node => _visibleNodeIds.Contains(node.id) &&
                Near(mouse, OwnershipInput(HeaderRect(node), _zoom)));
        }

        private ArchitectureNode HitInterfaceRow(Vector2 mouse, out ArchitectureInterfaceItem item)
        {
            foreach (var node in _document.nodes.AsEnumerable().Reverse())
            {
                if (!_visibleNodeIds.Contains(node.id)) continue;
                var items = ArchitectureModel.InterfaceItems(_document, node);
                if (items == null) continue;
                for (var index = items.Count - 1; index >= 0; index--)
                {
                    if (!InterfaceRect(node, index).Contains(mouse)) continue;
                    item = items[index];
                    return node;
                }
            }
            item = null;
            return null;
        }

        private ArchitectureNode HitNode(Vector2 mouse)
        {
            return _document.nodes.LastOrDefault(node => _visibleNodeIds.Contains(node.id) &&
                HeaderRect(node).Contains(mouse)) ??
                   _document.nodes.LastOrDefault(node => _visibleNodeIds.Contains(node.id) &&
                       NodeRect(node).Contains(mouse));
        }

        private void LayoutTree()
        {
            RefreshCanvasVisibility();
            var collapsedOrigins = _document.nodes
                .Where(node => _visibleNodeIds.Contains(node.id) && _collapsedNodeIds.Contains(node.id))
                .ToDictionary(node => node.id, node => node.position);
            var roots = _document.nodes.Where(node => string.IsNullOrEmpty(node.parentId))
                .OrderBy(node => node.id == ArchitectureModel.RootId ? 0 : 1)
                .ThenBy(node => node.position.x).ToArray();
            var left = 75f;
            foreach (var root in roots)
            {
                var levelHeights = new Dictionary<int, float>();
                MeasureLevelHeights(root, 0, levelHeights);
                var levelTops = new Dictionary<int, float>();
                var top = 75f;
                foreach (var depth in levelHeights.Keys.OrderBy(value => value))
                {
                    levelTops[depth] = top;
                    top += levelHeights[depth] + 125f;
                }
                var subtreeWidths = new Dictionary<string, float>();
                var width = MeasureSubtreeWidth(root, subtreeWidths);
                PlaceBranch(root, left, 0, levelTops, subtreeWidths);
                left += width + 90f;
            }
            foreach (var origin in collapsedOrigins)
            {
                var collapsedNode = ArchitectureModel.FindNode(_document, origin.Key);
                var delta = collapsedNode.position - origin.Value;
                foreach (var id in ArchitectureModel.SubtreeIds(_document, origin.Key))
                {
                    if (id == origin.Key) continue;
                    ArchitectureModel.FindNode(_document, id).position += delta;
                }
            }
        }

        private void MeasureLevelHeights(ArchitectureNode node, int depth, Dictionary<int, float> heights)
        {
            var height = NodeHeightWorld(node);
            heights[depth] = Mathf.Max(heights.TryGetValue(depth, out var previous) ? previous : 0f, height);
            foreach (var child in _document.nodes.Where(item => item.parentId == node.id &&
                         _visibleNodeIds.Contains(item.id)))
                MeasureLevelHeights(child, depth + 1, heights);
        }

        private float MeasureSubtreeWidth(ArchitectureNode node, Dictionary<string, float> widths)
        {
            var children = _document.nodes.Where(item => item.parentId == node.id &&
                _visibleNodeIds.Contains(item.id)).OrderBy(item => item.position.x).ToArray();
            if (children.Length == 0) return widths[node.id] = CardWidth;
            var childrenWidth = children.Sum(child => MeasureSubtreeWidth(child, widths)) + (children.Length - 1) * 55f;
            return widths[node.id] = Mathf.Max(CardWidth, childrenWidth);
        }

        private void PlaceBranch(ArchitectureNode node, float left, int depth,
            Dictionary<int, float> levelTops, Dictionary<string, float> widths)
        {
            var width = widths[node.id];
            node.position = new Vector2(left + (width - CardWidth) / 2f, levelTops[depth]);
            var children = _document.nodes.Where(item => item.parentId == node.id &&
                _visibleNodeIds.Contains(item.id)).OrderBy(item => item.position.x).ToArray();
            if (children.Length == 0) return;
            var childrenWidth = children.Sum(child => widths[child.id]) + (children.Length - 1) * 55f;
            var cursor = left + (width - childrenWidth) / 2f;
            foreach (var child in children)
            {
                PlaceBranch(child, cursor, depth + 1, levelTops, widths);
                cursor += widths[child.id] + 55f;
            }
        }

        private void FitCanvas()
        {
            if (_document.nodes.Count == 0 || _canvasRect.width <= 0 || _canvasRect.height <= 0) return;
            RefreshCanvasVisibility();
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var node in _document.nodes)
            {
                if (!_visibleNodeIds.Contains(node.id)) continue;
                min = Vector2.Min(min, node.position);
                max = Vector2.Max(max, node.position + new Vector2(CardWidth, NodeHeightWorld(node)));
            }
            var size = max - min + new Vector2(130, 130);
            _zoom = Mathf.Clamp(Mathf.Min(_canvasRect.width / size.x, _canvasRect.height / size.y), 0.45f, 1.35f);
            _pan = new Vector2(_canvasRect.width, _canvasRect.height) / 2f - (min + max) / 2f * _zoom;
            Repaint();
        }

        private void FocusNode(ArchitectureNode node)
        {
            ExpandAncestors(node);
            if (_canvasRect.width <= 0 || _canvasRect.height <= 0) return;
            var center = node.position + new Vector2(CardWidth / 2f, NodeHeightWorld(node) / 2f);
            _pan = new Vector2(_canvasRect.width, _canvasRect.height) / 2f - center * _zoom;
            Repaint();
        }
    }
}
