using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIGameFramework.Architecture
{
    public sealed partial class ArchitectureGraphWindow : EditorWindow
    {
        private enum LeftPaneMode { Types, Directory }

        private static readonly string[] LeftPaneTabs = { "类型", "目录" };
        private const float ToolbarHeight = 34f;
        private const float SplitterWidth = 5f;
        private const string PaletteDragKey = "AIGameFramework.Architecture.PaletteNode";
        private const string OrdinaryPaletteId = "ordinary";
        private const string NodeClipboardPrefix = "AIGameFramework.Architecture.Nodes\n";

        [Serializable]
        private sealed class NodeClipboard
        {
            public int version = 2;
            public List<string> rootIds = new List<string>();
            public List<ArchitectureNode> nodes = new List<ArchitectureNode>();
            public List<InterfaceReference> interfaceReferences = new List<InterfaceReference>();
        }

        private ArchitectureDocument _document;
        private string _loadError;
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();
        private string _lastChangeKey;
        private double _lastChangeTime;
        private bool _dirty;
        private double _nextSave;
        private double _nextExternalCheck;
        private DateTime _knownWriteTime;
        private bool _externalConflict;
        private string _status = string.Empty;

        private float _leftWidth = 270f;
        private float _rightWidth = 370f;
        private bool _resizingLeft;
        private bool _resizingRight;
        private Rect _canvasRect;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private GUIStyle _wrappedTextAreaStyle;
        private string _search = string.Empty;
        private LeftPaneMode _leftPaneMode = LeftPaneMode.Types;
        private string _palettePressed;
        private Vector2 _paletteDown;

        private string _selectedNodeId;
        private readonly HashSet<string> _selectedNodeIds = new HashSet<string>();
        private string _selectedTemplateId;
        private bool _selectedOrdinaryPalette;
        private bool _canvasShortcutFocus;
        private int _pasteCount;

        public static void Open()
        {
            var window = GetWindow<ArchitectureGraphWindow>();
            window.titleContent = new GUIContent("架构编辑器");
            window.minSize = new Vector2(980, 620);
            window.Show();
        }

        private void OnEnable()
        {
            _leftWidth = EditorPrefs.GetFloat("AIArchitecture.LeftWidth", 270f);
            _rightWidth = EditorPrefs.GetFloat("AIArchitecture.RightWidth", 370f);
            _leftPaneMode = (LeftPaneMode)Mathf.Clamp(EditorPrefs.GetInt("AIArchitecture.LeftPaneMode", 0), 0, 1);
            LoadCollapsedNodeIds();
            Reload();
        }

        private void OnDisable()
        {
            if (_dirty && _document != null && !_externalConflict) SaveNow();
            EditorPrefs.SetFloat("AIArchitecture.LeftWidth", _leftWidth);
            EditorPrefs.SetFloat("AIArchitecture.RightWidth", _rightWidth);
            EditorPrefs.SetInt("AIArchitecture.LeftPaneMode", (int)_leftPaneMode);
            SaveCollapsedNodeIds();
            ReleaseCardStyles();
        }

        private void Update()
        {
            if (EditorApplication.timeSinceStartup >= _nextExternalCheck)
            {
                _nextExternalCheck = EditorApplication.timeSinceStartup + 1;
                CheckExternalChange();
            }
            if (_dirty && !_externalConflict && EditorApplication.timeSinceStartup >= _nextSave) SaveNow();
        }

        private void Reload()
        {
            _document = ArchitectureStore.Load(out _loadError);
            PruneCollapsedNodeIds();
            _knownWriteTime = ArchitectureStore.LastWriteTimeUtc();
            _externalConflict = false;
            _dirty = false;
            _undo.Clear();
            _redo.Clear();
            _selectedNodeId = _document != null ? ArchitectureModel.RootId : null;
            _selectedNodeIds.Clear();
            if (_selectedNodeId != null) _selectedNodeIds.Add(_selectedNodeId);
            _selectedTemplateId = null;
            _selectedOrdinaryPalette = false;
            ClearStrongInterfaceSelection();
            _canvasShortcutFocus = false;
            _pasteCount = 0;
            Repaint();
        }

        private void CheckExternalChange()
        {
            if (_document == null || !string.IsNullOrEmpty(_movingNodeId)) return;
            if (ArchitectureStore.LastWriteTimeUtc() == _knownWriteTime) return;
            if (_dirty)
            {
                _externalConflict = true;
                _status = "架构文件已被外部修改。按 Ctrl+S 解决版本冲突。";
                Repaint();
            }
            else
            {
                Reload();
                _status = "已读取外部更新的架构文件。";
            }
        }

        private void SaveNow(bool force = false)
        {
            try
            {
                if (!force && ArchitectureStore.LastWriteTimeUtc() != _knownWriteTime)
                {
                    _externalConflict = true;
                    _status = "架构文件已被外部修改。按 Ctrl+S 解决版本冲突。";
                    Repaint();
                    return;
                }
                ArchitectureStore.Save(_document);
                _knownWriteTime = ArchitectureStore.LastWriteTimeUtc();
                _externalConflict = false;
                _dirty = false;
                _status = string.Empty;
            }
            catch (Exception exception)
            {
                _status = "保存失败：" + exception.Message;
                _nextSave = EditorApplication.timeSinceStartup + 3;
                Debug.LogError(_status);
            }
            Repaint();
        }

        private void ResolveExternalConflict()
        {
            var choice = EditorUtility.DisplayDialogComplex("架构文件已更新",
                "外部工具修改了架构文件，同时编辑器里有未保存更改。请选择保留哪一份。",
                "读取外部版本", "取消", "用本地版本覆盖");
            if (choice == 0) Reload();
            else if (choice == 2) SaveNow(true);
        }

        private bool Change(Action mutation, string key = null)
        {
            var before = JsonUtility.ToJson(_document);
            try
            {
                mutation();
                var errors = ArchitectureModel.Validate(_document);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            }
            catch (Exception exception)
            {
                _document = JsonUtility.FromJson<ArchitectureDocument>(before);
                _status = "修改未应用：" + exception.Message;
                ShowNotification(new GUIContent("修改未应用，请查看窗口底部提示。"));
                Repaint();
                return false;
            }

            var now = EditorApplication.timeSinceStartup;
            if (string.IsNullOrEmpty(key) || key != _lastChangeKey || now - _lastChangeTime > 0.8)
            {
                _undo.Push(before);
                if (_undo.Count > 80) TrimUndo();
            }
            _redo.Clear();
            _lastChangeKey = key;
            _lastChangeTime = now;
            MarkDirty();
            return true;
        }

        private void TrimUndo()
        {
            var items = _undo.ToArray();
            _undo.Clear();
            for (var index = Math.Min(items.Length, 80) - 1; index >= 0; index--) _undo.Push(items[index]);
        }

        private void CommitCanvasMove(string before)
        {
            if (string.IsNullOrEmpty(before) || before == JsonUtility.ToJson(_document)) return;
            _undo.Push(before);
            _redo.Clear();
            _lastChangeKey = null;
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _nextSave = EditorApplication.timeSinceStartup + 1.5;
            Repaint();
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Push(JsonUtility.ToJson(_document));
            _document = JsonUtility.FromJson<ArchitectureDocument>(_undo.Pop());
            _lastChangeKey = null;
            PruneNodeSelection();
            MarkDirty();
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Push(JsonUtility.ToJson(_document));
            _document = JsonUtility.FromJson<ArchitectureDocument>(_redo.Pop());
            _lastChangeKey = null;
            PruneNodeSelection();
            MarkDirty();
        }

        private void OnGUI()
        {
            HandleShortcuts(Event.current);
            if (_document == null)
            {
                EditorGUILayout.HelpBox(_loadError ?? "无法读取架构文件。", MessageType.Error);
                if (GUILayout.Button("重新读取")) Reload();
                return;
            }

            _leftWidth = Mathf.Clamp(_leftWidth, 215f, Mathf.Min(440f, position.width - _rightWidth - 350f));
            _rightWidth = Mathf.Clamp(_rightWidth, 305f, Mathf.Min(530f, position.width - _leftWidth - 350f));
            var content = new Rect(0, ToolbarHeight, position.width, position.height - ToolbarHeight - 25);
            var left = new Rect(0, content.y, _leftWidth, content.height);
            var leftSplitter = new Rect(left.xMax, content.y, SplitterWidth, content.height);
            var right = new Rect(position.width - _rightWidth, content.y, _rightWidth, content.height);
            var rightSplitter = new Rect(right.x - SplitterWidth, content.y, SplitterWidth, content.height);
            _canvasRect = new Rect(leftSplitter.xMax, content.y, rightSplitter.x - leftSplitter.xMax, content.height);
            HandleSplitters(leftSplitter, rightSplitter);
            DrawToolbar();
            DrawLeft(left);
            DrawCanvas(_canvasRect);
            DrawRight(right);
            EditorGUI.DrawRect(leftSplitter, new Color(0.17f, 0.19f, 0.22f));
            EditorGUI.DrawRect(rightSplitter, new Color(0.17f, 0.19f, 0.22f));
            EditorGUIUtility.AddCursorRect(leftSplitter, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(rightSplitter, MouseCursor.ResizeHorizontal);

            if (!string.IsNullOrEmpty(_status))
            {
                var footer = new Rect(8, position.height - 23, position.width - 16, 19);
                GUI.Label(footer, _status, EditorStyles.miniLabel);
            }
        }

        private void HandleShortcuts(Event e)
        {
            if (e.type == EventType.MouseDown)
            {
                _canvasShortcutFocus = _canvasRect.Contains(e.mousePosition);
                if (_canvasShortcutFocus && e.button == 0) GUI.FocusControl(null);
            }
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Delete && !EditorGUIUtility.editingTextField)
            {
                var roots = SelectedDeletableRoots();
                if (roots.Count > 0)
                {
                    var deleted = DeleteSelectedNodes(roots);
                    e.Use();
                    if (deleted) GUIUtility.ExitGUI();
                    return;
                }
                if (!string.IsNullOrEmpty(_selectedTemplateId))
                {
                    var deleted = DeleteSelectedTemplate();
                    e.Use();
                    if (deleted) GUIUtility.ExitGUI();
                    return;
                }
                return;
            }
            if (!(e.control || e.command)) return;
            if (e.keyCode == KeyCode.S)
            {
                if (_dirty && _document != null)
                {
                    if (_externalConflict) ResolveExternalConflict();
                    else SaveNow();
                }
                e.Use();
                return;
            }
            if (EditorGUIUtility.editingTextField) return;
            if (_canvasShortcutFocus && e.keyCode == KeyCode.C)
            {
                if (CopySelectedNodes()) e.Use();
            }
            else if (_canvasShortcutFocus && e.keyCode == KeyCode.V)
            {
                if (PasteCopiedNodes())
                {
                    e.Use();
                    GUIUtility.ExitGUI();
                }
            }
            else if (e.keyCode == KeyCode.Z)
            {
                if (e.shift ? _redo.Count == 0 : _undo.Count == 0) return;
                if (e.shift) Redo(); else Undo();
                e.Use();
                GUIUtility.ExitGUI();
            }
            else if (e.keyCode == KeyCode.Y)
            {
                if (_redo.Count == 0) return;
                Redo();
                e.Use();
                GUIUtility.ExitGUI();
            }
        }

        private bool CopySelectedNodes()
        {
            var roots = SelectedDeletableRoots();
            if (roots.Count == 0) return false;
            var copiedIds = new HashSet<string>();
            foreach (var id in roots) copiedIds.UnionWith(ArchitectureModel.SubtreeIds(_document, id));
            var payload = new NodeClipboard
            {
                rootIds = roots,
                nodes = _document.nodes.Where(node => copiedIds.Contains(node.id)).ToList(),
                interfaceReferences = _document.interfaceReferences.Where(reference =>
                    copiedIds.Contains(reference.sourceNodeId) && copiedIds.Contains(reference.consumerNodeId)).ToList()
            };
            EditorGUIUtility.systemCopyBuffer = NodeClipboardPrefix + JsonUtility.ToJson(payload);
            _pasteCount = 0;
            return true;
        }

        private List<string> SelectedDeletableRoots()
        {
            if (_document == null || _selectedNodeIds.Count == 0) return new List<string>();
            var candidates = new HashSet<string>();
            foreach (var id in _selectedNodeIds)
            {
                var node = ArchitectureModel.FindNode(_document, id);
                if (node == null || node.id == ArchitectureModel.RootId) continue;
                if (!string.IsNullOrEmpty(node.templateId) && node.templateInstanceId != node.id)
                    node = ArchitectureModel.FindNode(_document, node.templateInstanceId);
                if (node != null && node.id != ArchitectureModel.RootId) candidates.Add(node.id);
            }
            var roots = candidates.Where(id =>
            {
                for (var parent = ArchitectureModel.FindNode(_document, id)?.parentId;
                     !string.IsNullOrEmpty(parent);
                     parent = ArchitectureModel.FindNode(_document, parent)?.parentId)
                    if (candidates.Contains(parent)) return false;
                return true;
            }).ToList();
            return roots;
        }

        private bool DeleteSelectedNodes(List<string> roots)
        {
            if (!Change(() =>
                {
                    foreach (var id in roots) ArchitectureModel.DeleteNode(_document, id);
                })) return false;
            SetNodeSelection(Array.Empty<string>());
            return true;
        }

        private bool PasteCopiedNodes()
        {
            if (_document == null) return false;
            var clipboard = EditorGUIUtility.systemCopyBuffer;
            if (clipboard == null || !clipboard.StartsWith(NodeClipboardPrefix, StringComparison.Ordinal)) return false;
            try
            {
                var payload = JsonUtility.FromJson<NodeClipboard>(clipboard.Substring(NodeClipboardPrefix.Length));
                if (payload == null || (payload.version != 1 && payload.version != 2))
                    throw new InvalidOperationException("剪贴板中的节点版本不受支持。");
                List<string> pastedRoots = null;
                var nextPaste = _pasteCount + 1;
                if (Change(() => pastedRoots = ArchitectureModel.PasteSubtrees(_document,
                    payload.nodes, payload.rootIds, new Vector2(45f * nextPaste, 65f * nextPaste),
                    payload.interfaceReferences)))
                {
                    _pasteCount = nextPaste;
                    SetNodeSelection(pastedRoots);
                }
            }
            catch (Exception exception)
            {
                _status = "粘贴未应用：" + exception.Message;
                Repaint();
            }
            return true;
        }

        private void HandleSplitters(Rect left, Rect right)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (left.Contains(e.mousePosition)) { _resizingLeft = true; e.Use(); }
                else if (right.Contains(e.mousePosition)) { _resizingRight = true; e.Use(); }
            }
            else if (e.type == EventType.MouseDrag && (_resizingLeft || _resizingRight))
            {
                if (_resizingLeft) _leftWidth = Mathf.Clamp(e.mousePosition.x, 215f, position.width - _rightWidth - 350f);
                else _rightWidth = Mathf.Clamp(position.width - e.mousePosition.x, 305f, position.width - _leftWidth - 350f);
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && (_resizingLeft || _resizingRight))
            {
                _resizingLeft = _resizingRight = false;
                e.Use();
            }
        }

        private void DrawToolbar()
        {
            GUILayout.BeginArea(new Rect(5, 3, position.width - 10, ToolbarHeight - 4));
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("AI 架构", EditorStyles.boldLabel, GUILayout.Width(74));
            if (GUILayout.Button("自动排版", EditorStyles.toolbarButton, GUILayout.Width(72)))
                Change(LayoutTree);
            if (GUILayout.Button("适应画布", EditorStyles.toolbarButton, GUILayout.Width(72))) FitCanvas();
            GUILayout.FlexibleSpace();
            GUILayout.Label(_dirty ? "未保存" : "已保存", EditorStyles.miniLabel, GUILayout.Width(54));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLeft(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.18f, 0.21f));
            GUILayout.BeginArea(new Rect(rect.x + 9, rect.y + 10, rect.width - 18, rect.height - 20));
            _search = EditorGUILayout.TextField(_search);
            GUILayout.Space(6);
            var nextMode = (LeftPaneMode)GUILayout.Toolbar((int)_leftPaneMode, LeftPaneTabs, EditorStyles.toolbarButton);
            if (nextMode != _leftPaneMode)
            {
                _leftPaneMode = nextMode;
                _leftScroll = Vector2.zero;
            }
            GUILayout.Space(6);
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            if (_leftPaneMode == LeftPaneMode.Types)
            {
                if (GUILayout.Button("+ 新建预制模块"))
                {
                    NodeTemplate created = null;
                    if (Change(() => created = ArchitectureModel.AddTemplate(_document))) SelectTemplate(created.id);
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(5);
                if (Matches("普通模块")) DrawPaletteRow(OrdinaryPaletteId, "普通模块", 0, false);
                foreach (var template in _document.templates.Where(item => string.IsNullOrEmpty(item.parentTemplateId)).ToArray())
                    DrawTemplateTree(template, 0);
            }
            else
            {
                var root = ArchitectureModel.FindNode(_document, ArchitectureModel.RootId);
                if (root != null) DrawInstanceTree(root, 0);
                foreach (var node in _document.nodes.Where(item => item.id != ArchitectureModel.RootId &&
                             string.IsNullOrEmpty(item.parentId)).ToArray())
                    DrawInstanceTree(node, 0);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private bool Matches(string text)
        {
            return string.IsNullOrEmpty(_search) || (text ?? string.Empty).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawTemplateTree(NodeTemplate template, int depth)
        {
            if (!TemplateSubtreeMatches(template)) return;
            DrawPaletteRow(template.id, template.name, depth, true);
            foreach (var child in _document.templates.Where(item => item.parentTemplateId == template.id).ToArray())
                DrawTemplateTree(child, depth + 1);
        }

        private bool TemplateSubtreeMatches(NodeTemplate template)
        {
            if (Matches(template.name)) return true;
            return _document.templates.Where(item => item.parentTemplateId == template.id)
                .Any(TemplateSubtreeMatches);
        }

        private void DrawInstanceTree(ArchitectureNode node, int depth)
        {
            if (!InstanceSubtreeMatches(node)) return;
            var title = ArchitectureModel.DisplayName(_document, node);
            var row = GUILayoutUtility.GetRect(0, 25, GUILayout.ExpandWidth(true));
            if (_selectedNodeIds.Contains(node.id)) EditorGUI.DrawRect(row, new Color(0.22f, 0.34f, 0.45f));
            GUI.Label(new Rect(row.x + 5 + depth * 13, row.y + 3, row.width - depth * 13 - 5, 21),
                title, EditorStyles.label);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                SelectNode(node.id);
                FocusNode(node);
                Event.current.Use();
                GUIUtility.ExitGUI();
            }
            foreach (var child in _document.nodes.Where(item => item.parentId == node.id).ToArray())
                DrawInstanceTree(child, depth + 1);
        }

        private bool InstanceSubtreeMatches(ArchitectureNode node)
        {
            if (Matches(ArchitectureModel.DisplayName(_document, node))) return true;
            return _document.nodes.Where(item => item.parentId == node.id).Any(InstanceSubtreeMatches);
        }

        private void DrawPaletteRow(string id, string title, int depth, bool isTemplate)
        {
            var row = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            var selected = isTemplate ? _selectedTemplateId == id : _selectedOrdinaryPalette;
            EditorGUI.DrawRect(row, selected ? new Color(0.23f, 0.36f, 0.48f) : new Color(0.21f, 0.24f, 0.28f));
            GUI.Label(new Rect(row.x + 8 + depth * 15, row.y + 5, row.width - 16 - depth * 15, 21),
                title, EditorStyles.label);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                _palettePressed = id;
                _paletteDown = e.mousePosition;
                if (isTemplate) SelectTemplate(id);
                else
                {
                    _selectedOrdinaryPalette = true;
                    _selectedNodeId = _selectedTemplateId = null;
                    _selectedNodeIds.Clear();
                    ClearStrongInterfaceSelection();
                    Repaint();
                }
                e.Use();
                GUIUtility.ExitGUI();
            }
            else if (e.type == EventType.MouseDrag && _palettePressed == id &&
                     (e.mousePosition - _paletteDown).sqrMagnitude > 16)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(PaletteDragKey, id);
                DragAndDrop.StartDrag("拖入架构画布：" + title);
                _palettePressed = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _palettePressed == id) _palettePressed = null;
        }

        private void DrawRight(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.20f, 0.23f));
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            if (!string.IsNullOrEmpty(_selectedTemplateId))
            {
                var template = ArchitectureModel.FindTemplate(_document, _selectedTemplateId);
                if (template != null) DrawTemplateDetails(template);
            }
            else if (!string.IsNullOrEmpty(_selectedNodeId))
            {
                var node = ArchitectureModel.FindNode(_document, _selectedNodeId);
                if (node != null) DrawNodeDetails(node);
            }
            else if (_selectedNodeIds.Count > 1)
            {
                GUILayout.Label("已选择 " + _selectedNodeIds.Count + " 个节点", EditorStyles.boldLabel);
            }
            else if (_selectedOrdinaryPalette)
            {
                GUILayout.Label("普通模块", EditorStyles.boldLabel);
            }
            else
            {
                GUILayout.Label("详细信息", EditorStyles.boldLabel);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTemplateDetails(NodeTemplate template)
        {
            DrawNameField(template.name, value => template.name = value,
                "template-name-" + template.id, () => ShowTemplateMenu(template));
            DrawLargeTextField("预制文本", template.presetText,
                value => template.presetText = value, "template-text-" + template.id);
            DrawInterfaceList(template.interfaceItems, "template-interface-" + template.id);
        }

        private void DrawNodeDetails(ArchitectureNode node)
        {
            var template = ArchitectureModel.FindTemplate(_document, node.templateId);
            var title = ArchitectureModel.DisplayName(_document, node);
            DrawNameField(title, value => node.alias = value, "node-name-" + node.id);
            if (template != null)
            {
                DrawLargeTextField("预制文本", template.presetText,
                    value => template.presetText = value, "template-text-" + template.id);
            }
            DrawLargeTextField("功能", node.functionText,
                value => node.functionText = value, "node-function-" + node.id);
            if (template != null)
            {
                DrawInterfaceList(template.interfaceItems, "template-interface-" + template.id, node.id);
            }
            else
            {
                DrawInterfaceList(node.interfaceItems, "node-interface-" + node.id, node.id);
            }
        }

        private void DrawNameField(string current, Action<string> setter, string key, Action openMenu = null)
        {
            GUILayout.BeginHorizontal();
            var next = EditorGUILayout.TextField(current ?? string.Empty, GUILayout.ExpandWidth(true));
            if (openMenu != null && GUILayout.Button("...", EditorStyles.miniButton, GUILayout.Width(30)))
                openMenu();
            GUILayout.EndHorizontal();
            if (next != (current ?? string.Empty)) Change(() => setter(next), key);
            GUILayout.Space(8);
        }

        private void DrawLargeTextField(string label, string current, Action<string> setter, string key)
        {
            GUILayout.Label(label, EditorStyles.boldLabel);
            var height = Mathf.Max(200f, (_canvasRect.height - 110f) * 0.5f);
            var next = EditorGUILayout.TextArea(current ?? string.Empty, WrappedTextAreaStyle,
                GUILayout.MinHeight(height), GUILayout.ExpandWidth(true));
            if (next != (current ?? string.Empty)) Change(() => setter(next), key);
            GUILayout.Space(10);
        }

        private GUIStyle WrappedTextAreaStyle
        {
            get
            {
                if (_wrappedTextAreaStyle == null)
                    _wrappedTextAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                return _wrappedTextAreaStyle;
            }
        }

        private void DrawInterfaceList(List<ArchitectureInterfaceItem> items, string key, string sourceNodeId = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("接口", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(28)))
            {
                if (Change(() => items.Add(new ArchitectureInterfaceItem
                    { id = ArchitectureModel.NewId(), title = "新接口", content = string.Empty })))
                    GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            foreach (var item in items.ToArray())
            {
                GUILayout.BeginHorizontal();
                var nextTitle = EditorGUILayout.TextField("标题", item.title ?? string.Empty,
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(25), GUILayout.Height(22)))
                {
                    if (Change(() => ArchitectureModel.DeleteInterfaceItem(_document, items, item.id)))
                    {
                        PruneNodeSelection();
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.EndHorizontal();
                if (nextTitle != (item.title ?? string.Empty))
                    Change(() => item.title = nextTitle, key + "-title-" + item.id);
                var nextContent = EditorGUILayout.TextArea(item.content ?? string.Empty, WrappedTextAreaStyle,
                    GUILayout.MinHeight(105f), GUILayout.ExpandWidth(true));
                if (nextContent != (item.content ?? string.Empty))
                    Change(() => item.content = nextContent, key + "-content-" + item.id);
                if (!string.IsNullOrEmpty(sourceNodeId))
                {
                    foreach (var reference in _document.interfaceReferences.Where(reference =>
                                 reference.sourceNodeId == sourceNodeId && reference.interfaceId == item.id).ToArray())
                    {
                        var consumer = ArchitectureModel.FindNode(_document, reference.consumerNodeId);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("使用：" + (consumer == null ? "未知模块" :
                            ArchitectureModel.DisplayName(_document, consumer)), EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(25), GUILayout.Height(20)))
                        {
                            if (Change(() => _document.interfaceReferences.Remove(reference))) GUIUtility.ExitGUI();
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.Space(8);
            }
        }

        private void ShowTemplateMenu(NodeTemplate template)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("添加固定子节点"), false, () =>
            {
                NodeTemplate created = null;
                if (Change(() =>
                    {
                        created = ArchitectureModel.AddTemplate(_document, template.id);
                    }))
                {
                    SelectTemplate(created.id);
                }
            });
            menu.ShowAsContext();
        }

        private bool DeleteSelectedTemplate()
        {
            var template = ArchitectureModel.FindTemplate(_document, _selectedTemplateId);
            if (template == null) return false;
            var templateIds = ArchitectureModel.TemplateSubtreeIds(_document, template.id);
            var affectedNodes = new HashSet<string>();
            foreach (var instance in _document.nodes.Where(node => templateIds.Contains(node.templateId)))
                affectedNodes.UnionWith(ArchitectureModel.SubtreeIds(_document, instance.id));
            if (!EditorUtility.DisplayDialog("删除预制模块",
                "将删除 " + templateIds.Count + " 个预制定义和 " + affectedNodes.Count + " 个架构节点。确定继续？",
                "删除", "取消")) return false;
            if (Change(() =>
                {
                    ArchitectureModel.DeleteTemplate(_document, template.id);
                }))
            {
                SelectNode(ArchitectureModel.RootId);
                return true;
            }
            return false;
        }

        private void SelectNode(string id)
        {
            GUI.FocusControl(null);
            ClearStrongInterfaceSelection();
            _selectedNodeId = id;
            _selectedNodeIds.Clear();
            if (id != null) _selectedNodeIds.Add(id);
            _selectedTemplateId = null;
            _selectedOrdinaryPalette = false;
            _rightScroll = Vector2.zero;
            Repaint();
        }

        private void SetNodeSelection(IEnumerable<string> ids)
        {
            ClearStrongInterfaceSelection();
            _selectedNodeIds.Clear();
            foreach (var id in ids) _selectedNodeIds.Add(id);
            _selectedNodeId = _selectedNodeIds.Count == 1 ? _selectedNodeIds.First() : null;
            _selectedTemplateId = null;
            _selectedOrdinaryPalette = false;
            _rightScroll = Vector2.zero;
            Repaint();
        }

        private void PruneNodeSelection()
        {
            _selectedNodeIds.RemoveWhere(id => ArchitectureModel.FindNode(_document, id) == null);
            _selectedNodeId = _selectedNodeIds.Count == 1 ? _selectedNodeIds.First() : null;
            var strongNode = ArchitectureModel.FindNode(_document, _strongSelectedInterfaceNodeId);
            if (strongNode == null || !_selectedNodeIds.Contains(_strongSelectedInterfaceNodeId) ||
                ArchitectureModel.InterfaceItems(_document, strongNode)?.Any(item =>
                    item != null && item.id == _strongSelectedInterfaceId) != true)
                ClearStrongInterfaceSelection();
        }

        private void SelectTemplate(string id)
        {
            GUI.FocusControl(null);
            ClearStrongInterfaceSelection();
            _selectedTemplateId = id;
            _selectedNodeId = null;
            _selectedNodeIds.Clear();
            _selectedOrdinaryPalette = false;
            _rightScroll = Vector2.zero;
            Repaint();
        }

    }

    [InitializeOnLoad]
    internal static class DocumentationToolbarEntry
    {
        private const string ButtonName = "AIArchitectureDocumentationButton";
        private static readonly Type ToolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        private static readonly FieldInfo ToolbarInstance = ToolbarType?.GetField("get",
            BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo ToolbarRoot = ToolbarType?.GetField("m_Root",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static EditorToolbarButton _button;
        private static double _nextCheck;

        static DocumentationToolbarEntry()
        {
            EditorApplication.update += EnsureButton;
        }

        private static void EnsureButton()
        {
            if (EditorApplication.timeSinceStartup < _nextCheck) return;
            _nextCheck = EditorApplication.timeSinceStartup + 1;
            var toolbar = ToolbarInstance?.GetValue(null);
            if (toolbar == null || ToolbarRoot == null) return;
            var root = ToolbarRoot.GetValue(toolbar) as VisualElement;
            var zone = root?.Q<VisualElement>("ToolbarZoneRightAlign");
            if (zone == null) return;
            var existing = zone.Q<VisualElement>(ButtonName);
            if (existing != null && ReferenceEquals(existing, _button)) return;
            existing?.RemoveFromHierarchy();
            _button = new EditorToolbarButton(ArchitectureGraphWindow.Open)
            {
                name = ButtonName,
                text = "Documentation",
                tooltip = "打开架构文档"
            };
            _button.style.flexShrink = 0;
            zone.Add(_button);
        }
    }
}
