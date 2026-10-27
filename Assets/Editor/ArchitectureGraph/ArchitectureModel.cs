using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIGameFramework.Architecture
{
    [Serializable]
    public sealed class ArchitectureDocument
    {
        public int schemaVersion = ArchitectureModel.CurrentSchemaVersion;
        public List<ArchitectureNode> nodes = new List<ArchitectureNode>();
        public List<NodeTemplate> templates = new List<NodeTemplate>();
        public List<InterfaceCall> calls = new List<InterfaceCall>();
        public List<InterfaceReference> interfaceReferences = new List<InterfaceReference>();
    }

    [Serializable]
    public sealed class ArchitectureNode
    {
        public string id;
        public string parentId;
        public string templateId;
        public string templateInstanceId;
        public string alias;
        // Belongs to this canvas node, including instances created from a template.
        public string functionText;
        public List<ArchitectureInterfaceItem> interfaceItems = new List<ArchitectureInterfaceItem>();
        // Kept for reading architecture files created before the interface list.
        public string interfaceText;
        // Kept for reading architecture files created before the free-text editor.
        public string purpose;
        public string owns;
        public string excludes;
        public string codePath;
        public Vector2 position;
        public List<ArchitectureInterface> interfaces = new List<ArchitectureInterface>();
    }

    [Serializable]
    public sealed class NodeTemplate
    {
        public string id;
        public string parentTemplateId;
        public string name;
        // Shared definition text. Each ArchitectureNode keeps its own functionText.
        public string presetText;
        // Kept only for migrating template text from schema versions before 5.
        public string functionText;
        public List<ArchitectureInterfaceItem> interfaceItems = new List<ArchitectureInterfaceItem>();
        // Kept for reading architecture files created before the interface list.
        public string interfaceText;
        // Kept for reading architecture files created before the free-text editor.
        public string purpose;
        public string owns;
        public string excludes;
        public List<ArchitectureInterface> interfaces = new List<ArchitectureInterface>();
    }

    [Serializable]
    public sealed class ArchitectureInterfaceItem
    {
        public string id;
        public string title;
        public string content;
    }

    [Serializable]
    public sealed class InterfaceReference
    {
        public string id;
        public string sourceNodeId;
        public string interfaceId;
        public string consumerNodeId;
    }

    [Serializable]
    public sealed class ArchitectureInterface
    {
        public string id;
        public string name;
        public string kind = "Method";
        public string signature;
        public string inputs;
        public string outputs;
        public string notes;
    }

    [Serializable]
    public sealed class InterfaceCall
    {
        public string id;
        public string callerNodeId;
        public string targetNodeId;
        public string targetInterfaceId;
        public string reason;
    }

    public static class ArchitectureModel
    {
        public const int CurrentSchemaVersion = 5;
        public const string RootId = "root";

        public static ArchitectureDocument CreateEmpty()
        {
            var document = new ArchitectureDocument();
            document.nodes.Add(new ArchitectureNode
            {
                id = RootId,
                alias = "Root",
                functionText = "项目架构根节点",
                position = new Vector2(80, 80)
            });
            return document;
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static ArchitectureNode FindNode(ArchitectureDocument document, string id)
        {
            return document.nodes.Find(node => node.id == id);
        }

        public static NodeTemplate FindTemplate(ArchitectureDocument document, string id)
        {
            return document.templates.Find(template => template.id == id);
        }

        public static string DisplayName(ArchitectureDocument document, ArchitectureNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.alias)) return node.alias;
            var template = FindTemplate(document, node.templateId);
            return template != null ? template.name : "未命名模块";
        }

        public static List<ArchitectureInterfaceItem> InterfaceItems(ArchitectureDocument document, ArchitectureNode node)
        {
            var template = FindTemplate(document, node.templateId);
            return template != null ? template.interfaceItems : node.interfaceItems;
        }

        public static string PresetText(ArchitectureDocument document, ArchitectureNode node)
        {
            return FindTemplate(document, node.templateId)?.presetText ?? string.Empty;
        }

        public static void UpgradeLegacyText(ArchitectureDocument document)
        {
            if (document == null) return;
            if (document.schemaVersion == 1)
            {
                foreach (var node in document.nodes ?? new List<ArchitectureNode>())
                {
                    if (node == null) continue;
                    if (string.IsNullOrEmpty(node.functionText))
                        node.functionText = LegacyFunctionText(node.purpose, node.owns, node.excludes);
                    if (string.IsNullOrEmpty(node.interfaceText))
                    {
                        var contents = LegacyInterfaceContents(node.interfaces);
                        node.interfaceText = string.Join("\n\n", contents);
                        node.interfaceItems = contents.Select(content =>
                            new ArchitectureInterfaceItem { id = NewId(), content = content }).ToList();
                    }
                }
                foreach (var template in document.templates ?? new List<NodeTemplate>())
                {
                    if (template == null) continue;
                    if (string.IsNullOrEmpty(template.functionText))
                        template.functionText = LegacyFunctionText(template.purpose, template.owns, template.excludes);
                    if (string.IsNullOrEmpty(template.interfaceText))
                    {
                        var contents = LegacyInterfaceContents(template.interfaces);
                        template.interfaceText = string.Join("\n\n", contents);
                        template.interfaceItems = contents.Select(content =>
                            new ArchitectureInterfaceItem { id = NewId(), content = content }).ToList();
                    }
                }
                document.schemaVersion = 2;
            }
            if (document.schemaVersion == 2)
            {
                foreach (var node in document.nodes ?? new List<ArchitectureNode>())
                {
                    if (node == null) continue;
                    if (node.interfaceItems == null) node.interfaceItems = new List<ArchitectureInterfaceItem>();
                    if (node.interfaceItems.Count == 0 && !string.IsNullOrWhiteSpace(node.interfaceText))
                        node.interfaceItems.Add(new ArchitectureInterfaceItem { id = NewId(), content = node.interfaceText });
                }
                foreach (var template in document.templates ?? new List<NodeTemplate>())
                {
                    if (template == null) continue;
                    if (template.interfaceItems == null) template.interfaceItems = new List<ArchitectureInterfaceItem>();
                    if (template.interfaceItems.Count == 0 && !string.IsNullOrWhiteSpace(template.interfaceText))
                        template.interfaceItems.Add(new ArchitectureInterfaceItem { id = NewId(), content = template.interfaceText });
                }
                document.schemaVersion = 3;
            }
            if (document.schemaVersion == 3)
            {
                document.interfaceReferences = new List<InterfaceReference>();
                foreach (var node in document.nodes ?? new List<ArchitectureNode>())
                    if (node != null) SetMissingInterfaceTitles(node.interfaceItems);
                foreach (var template in document.templates ?? new List<NodeTemplate>())
                    if (template != null) SetMissingInterfaceTitles(template.interfaceItems);
                document.schemaVersion = 4;
            }
            if (document.schemaVersion == 4)
            {
                foreach (var template in document.templates ?? new List<NodeTemplate>())
                {
                    if (template == null) continue;
                    if (string.IsNullOrEmpty(template.presetText))
                        template.presetText = template.functionText ?? string.Empty;
                    template.functionText = string.Empty;
                }
                document.schemaVersion = CurrentSchemaVersion;
            }
            if (document.schemaVersion != CurrentSchemaVersion) return;
            if (document.interfaceReferences == null) document.interfaceReferences = new List<InterfaceReference>();
            foreach (var node in document.nodes ?? new List<ArchitectureNode>())
                if (node != null) NormalizeInterfaceItems(ref node.interfaceItems);
            foreach (var template in document.templates ?? new List<NodeTemplate>())
                if (template != null) NormalizeInterfaceItems(ref template.interfaceItems);
        }

        private static void NormalizeInterfaceItems(ref List<ArchitectureInterfaceItem> items)
        {
            if (items == null) items = new List<ArchitectureInterfaceItem>();
            foreach (var item in items)
                if (item != null && string.IsNullOrEmpty(item.id)) item.id = NewId();
            SetMissingInterfaceTitles(items);
        }

        private static void SetMissingInterfaceTitles(List<ArchitectureInterfaceItem> items)
        {
            if (items == null) return;
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item == null || !string.IsNullOrWhiteSpace(item.title)) continue;
                var firstLine = (item.content ?? string.Empty).Split(new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())
                    .FirstOrDefault(line => line.Length > 0);
                item.title = string.IsNullOrEmpty(firstLine) ? "接口 " + (index + 1) : firstLine;
            }
        }

        private static string LegacyFunctionText(string purpose, string owns, string excludes)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(purpose)) parts.Add(purpose.Trim());
            if (!string.IsNullOrWhiteSpace(owns)) parts.Add("负责：\n" + owns.Trim());
            if (!string.IsNullOrWhiteSpace(excludes)) parts.Add("不负责：\n" + excludes.Trim());
            return string.Join("\n\n", parts);
        }

        private static List<string> LegacyInterfaceContents(List<ArchitectureInterface> interfaces)
        {
            if (interfaces == null) return new List<string>();
            var entries = new List<string>();
            foreach (var port in interfaces)
            {
                if (port == null) continue;
                if (port.name == "NewInterface" && string.IsNullOrWhiteSpace(port.signature) &&
                    string.IsNullOrWhiteSpace(port.inputs) && string.IsNullOrWhiteSpace(port.outputs) &&
                    string.IsNullOrWhiteSpace(port.notes)) continue;
                var lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(port.name)) lines.Add(port.name.Trim());
                if (!string.IsNullOrWhiteSpace(port.signature)) lines.Add(port.signature.Trim());
                if (!string.IsNullOrWhiteSpace(port.inputs)) lines.Add("输入：" + port.inputs.Trim());
                if (!string.IsNullOrWhiteSpace(port.outputs)) lines.Add("输出：" + port.outputs.Trim());
                if (!string.IsNullOrWhiteSpace(port.notes)) lines.Add("约束：" + port.notes.Trim());
                if (lines.Count > 0) entries.Add(string.Join("\n", lines));
            }
            return entries;
        }

        public static string Purpose(ArchitectureDocument document, ArchitectureNode node)
        {
            var template = FindTemplate(document, node.templateId);
            return template != null ? template.purpose : node.purpose;
        }

        public static string Owns(ArchitectureDocument document, ArchitectureNode node)
        {
            var template = FindTemplate(document, node.templateId);
            return template != null ? template.owns : node.owns;
        }

        public static string Excludes(ArchitectureDocument document, ArchitectureNode node)
        {
            var template = FindTemplate(document, node.templateId);
            return template != null ? template.excludes : node.excludes;
        }

        public static List<ArchitectureInterface> Interfaces(ArchitectureDocument document, ArchitectureNode node)
        {
            var template = FindTemplate(document, node.templateId);
            return template != null
                ? template.interfaces ?? new List<ArchitectureInterface>()
                : node.interfaces ?? new List<ArchitectureInterface>();
        }

        public static NodeTemplate AddTemplate(ArchitectureDocument document, string parentTemplateId = null)
        {
            var template = new NodeTemplate
            {
                id = NewId(),
                parentTemplateId = parentTemplateId ?? string.Empty,
                name = string.IsNullOrEmpty(parentTemplateId) ? "新预制模块" : "新子模块"
            };
            document.templates.Add(template);

            // Existing instances of the parent gain the new fixed child immediately.
            if (!string.IsNullOrEmpty(parentTemplateId))
            {
                foreach (var parent in document.nodes.Where(node => node.templateId == parentTemplateId).ToArray())
                {
                    InstantiateTemplate(document, template.id, parent.id,
                        parent.position + new Vector2(0, 260), parent.templateInstanceId);
                }
            }
            return template;
        }

        public static ArchitectureNode InstantiateTemplate(ArchitectureDocument document, string templateId,
            string parentId, Vector2 position, string instanceId = null)
        {
            var template = FindTemplate(document, templateId);
            if (template == null) throw new ArgumentException("找不到预制模块。", nameof(templateId));

            var node = new ArchitectureNode
            {
                id = NewId(),
                parentId = parentId,
                templateId = templateId,
                position = position
            };
            node.templateInstanceId = string.IsNullOrEmpty(instanceId) ? node.id : instanceId;
            document.nodes.Add(node);

            var children = document.templates.Where(item => item.parentTemplateId == templateId).ToList();
            for (var index = 0; index < children.Count; index++)
            {
                InstantiateTemplate(document, children[index].id, node.id,
                    position + new Vector2((index - (children.Count - 1) / 2f) * 280f, 260f), node.templateInstanceId);
            }
            return node;
        }

        public static ArchitectureNode AddOrdinaryNode(ArchitectureDocument document, string parentId, Vector2 position)
        {
            var node = new ArchitectureNode
            {
                id = NewId(),
                parentId = parentId,
                alias = "新模块",
                position = position
            };
            document.nodes.Add(node);
            return node;
        }

        public static ArchitectureInterface AddInterface(List<ArchitectureInterface> interfaces)
        {
            var port = new ArchitectureInterface { id = NewId(), name = "NewInterface" };
            interfaces.Add(port);
            return port;
        }

        public static bool CanReparent(ArchitectureDocument document, ArchitectureNode node, ArchitectureNode newParent)
        {
            if (node == null || newParent == null || node.id == RootId || node.id == newParent.id) return false;
            if (!string.IsNullOrEmpty(node.templateId) && node.templateInstanceId != node.id) return false;
            var visited = new HashSet<string>();
            for (var cursor = newParent; cursor != null; cursor = FindNode(document, cursor.parentId))
            {
                if (cursor.id == node.id) return false;
                if (!visited.Add(cursor.id)) return false;
            }
            return true;
        }

        public static HashSet<string> SubtreeIds(ArchitectureDocument document, string rootId)
        {
            var result = new HashSet<string>();
            var pending = new Stack<string>();
            pending.Push(rootId);
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (!result.Add(id)) continue;
                foreach (var child in document.nodes.Where(node => node.parentId == id)) pending.Push(child.id);
            }
            return result;
        }

        public static List<string> PasteSubtrees(ArchitectureDocument document,
            List<ArchitectureNode> sourceNodes, List<string> sourceRootIds, Vector2 offset,
            List<InterfaceReference> sourceReferences = null)
        {
            if (document == null || sourceNodes == null || sourceRootIds == null ||
                sourceNodes.Count == 0 || sourceRootIds.Count == 0)
                throw new InvalidOperationException("剪贴板中没有可粘贴的节点。");

            var source = new Dictionary<string, ArchitectureNode>();
            foreach (var node in sourceNodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id) || node.id == RootId ||
                    !source.TryAdd(node.id, node))
                    throw new InvalidOperationException("剪贴板中的节点无效。");
            }
            var rootIds = new HashSet<string>(sourceRootIds);
            if (rootIds.Count != sourceRootIds.Count || rootIds.Any(id => !source.ContainsKey(id)))
                throw new InvalidOperationException("剪贴板中的根节点无效。");
            foreach (var node in sourceNodes)
            {
                var isRoot = rootIds.Contains(node.id);
                if (isRoot == source.ContainsKey(node.parentId ?? string.Empty))
                    throw new InvalidOperationException("剪贴板中的归属关系无效。");
                if (!isRoot) continue;
                var parent = FindNode(document, node.parentId);
                if ((!string.IsNullOrEmpty(node.parentId) && parent == null) ||
                    (!string.IsNullOrEmpty(node.templateId) && node.templateInstanceId != node.id))
                    throw new InvalidOperationException("原归属位置不允许粘贴此节点。");
            }

            var copiedReferences = new List<InterfaceReference>();
            var referenceKeys = new HashSet<string>();
            foreach (var reference in sourceReferences ?? new List<InterfaceReference>())
            {
                if (reference == null || !source.ContainsKey(reference.sourceNodeId ?? string.Empty) ||
                    !source.ContainsKey(reference.consumerNodeId ?? string.Empty)) continue;
                var sourceNode = source[reference.sourceNodeId];
                var key = reference.sourceNodeId + "\n" + reference.interfaceId + "\n" +
                          reference.consumerNodeId;
                if (reference.sourceNodeId == reference.consumerNodeId || !referenceKeys.Add(key) ||
                    InterfaceItems(document, sourceNode)?.All(item => item == null || item.id != reference.interfaceId) != false)
                    throw new InvalidOperationException("剪贴板中的接口引用无效。");
                copiedReferences.Add(reference);
            }

            var idMap = source.Keys.ToDictionary(id => id, id => NewId());
            var interfaceIdMap = new Dictionary<string, string>();
            foreach (var original in sourceNodes)
            {
                var copiedInterfaces = new List<ArchitectureInterfaceItem>();
                foreach (var item in original.interfaceItems ?? new List<ArchitectureInterfaceItem>())
                {
                    if (item == null) continue;
                    var newId = NewId();
                    if (!string.IsNullOrEmpty(item.id)) interfaceIdMap[item.id] = newId;
                    copiedInterfaces.Add(new ArchitectureInterfaceItem
                        { id = newId, title = item.title, content = item.content });
                }
                SetMissingInterfaceTitles(copiedInterfaces);
                var clone = new ArchitectureNode
                {
                    id = idMap[original.id],
                    parentId = idMap.TryGetValue(original.parentId ?? string.Empty, out var mappedParent)
                        ? mappedParent : original.parentId,
                    templateId = original.templateId,
                    templateInstanceId = idMap.TryGetValue(original.templateInstanceId ?? string.Empty, out var mappedInstance)
                        ? mappedInstance : original.templateInstanceId,
                    alias = original.alias,
                    functionText = original.functionText,
                    interfaceItems = copiedInterfaces,
                    interfaceText = original.interfaceText,
                    purpose = original.purpose,
                    owns = original.owns,
                    excludes = original.excludes,
                    codePath = original.codePath,
                    position = original.position + offset,
                    interfaces = original.interfaces == null ? new List<ArchitectureInterface>() :
                        original.interfaces.Where(port => port != null).Select(port => new ArchitectureInterface
                        {
                            id = NewId(),
                            name = port.name,
                            kind = port.kind,
                            signature = port.signature,
                            inputs = port.inputs,
                            outputs = port.outputs,
                            notes = port.notes
                        }).ToList()
                };
                document.nodes.Add(clone);
            }
            foreach (var reference in copiedReferences)
            {
                var sourceId = idMap[reference.sourceNodeId];
                var consumerId = idMap[reference.consumerNodeId];
                var interfaceId = interfaceIdMap.TryGetValue(reference.interfaceId ?? string.Empty, out var mappedInterface)
                    ? mappedInterface : reference.interfaceId;
                AddInterfaceReference(document, sourceId, interfaceId, consumerId);
            }
            return sourceRootIds.Select(id => idMap[id]).ToList();
        }

        public static InterfaceReference AddInterfaceReference(ArchitectureDocument document,
            string sourceNodeId, string interfaceId, string consumerNodeId)
        {
            var source = FindNode(document, sourceNodeId);
            var consumer = FindNode(document, consumerNodeId);
            if (source == null || consumer == null || source.id == consumer.id ||
                InterfaceItems(document, source)?.All(item => item == null || item.id != interfaceId) != false)
                throw new InvalidOperationException("接口引用的来源、接口或使用模块无效。");
            if (document.interfaceReferences.Any(reference => reference.sourceNodeId == sourceNodeId &&
                reference.interfaceId == interfaceId && reference.consumerNodeId == consumerNodeId))
                throw new InvalidOperationException("此接口引用已存在。");
            var created = new InterfaceReference
            {
                id = NewId(), sourceNodeId = sourceNodeId,
                interfaceId = interfaceId, consumerNodeId = consumerNodeId
            };
            document.interfaceReferences.Add(created);
            return created;
        }

        public static void DeleteInterfaceItem(ArchitectureDocument document,
            List<ArchitectureInterfaceItem> items, string interfaceId)
        {
            items.RemoveAll(item => item != null && item.id == interfaceId);
            document.interfaceReferences.RemoveAll(reference => reference.interfaceId == interfaceId);
        }

        public static void DeleteNode(ArchitectureDocument document, string nodeId)
        {
            if (nodeId == RootId) throw new InvalidOperationException("Root 不可删除。");
            var ids = SubtreeIds(document, nodeId);
            document.nodes.RemoveAll(node => ids.Contains(node.id));
            document.calls.RemoveAll(call => ids.Contains(call.callerNodeId) || ids.Contains(call.targetNodeId));
            document.interfaceReferences.RemoveAll(reference =>
                ids.Contains(reference.sourceNodeId) || ids.Contains(reference.consumerNodeId));
        }

        public static HashSet<string> TemplateSubtreeIds(ArchitectureDocument document, string rootId)
        {
            var result = new HashSet<string>();
            var pending = new Stack<string>();
            pending.Push(rootId);
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (!result.Add(id)) continue;
                foreach (var child in document.templates.Where(item => item.parentTemplateId == id)) pending.Push(child.id);
            }
            return result;
        }

        public static void DeleteTemplate(ArchitectureDocument document, string templateId)
        {
            var templateIds = TemplateSubtreeIds(document, templateId);
            var instanceRoots = document.nodes.Where(node => templateIds.Contains(node.templateId)).Select(node => node.id).ToArray();
            var nodeIds = new HashSet<string>();
            foreach (var root in instanceRoots) nodeIds.UnionWith(SubtreeIds(document, root));
            document.nodes.RemoveAll(node => nodeIds.Contains(node.id));
            document.calls.RemoveAll(call => nodeIds.Contains(call.callerNodeId) || nodeIds.Contains(call.targetNodeId));
            document.interfaceReferences.RemoveAll(reference =>
                nodeIds.Contains(reference.sourceNodeId) || nodeIds.Contains(reference.consumerNodeId));
            document.templates.RemoveAll(template => templateIds.Contains(template.id));
        }

        public static List<string> Validate(ArchitectureDocument document)
        {
            var errors = new List<string>();
            if (document == null) { errors.Add("架构文件为空。"); return errors; }
            if (document.schemaVersion != CurrentSchemaVersion) errors.Add("不支持的架构文件版本。");
            if (document.nodes == null || document.templates == null || document.interfaceReferences == null)
            {
                errors.Add("节点、预制模块或接口引用列表缺失。");
                return errors;
            }

            var nodes = new Dictionary<string, ArchitectureNode>();
            foreach (var node in document.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id)) { errors.Add("存在无 ID 的模块。"); continue; }
                if (!nodes.TryAdd(node.id, node)) errors.Add("重复的模块 ID: " + node.id);
            }
            if (!nodes.TryGetValue(RootId, out var root)) errors.Add("缺少 Root 节点。");
            else if (!string.IsNullOrEmpty(root.parentId) || !string.IsNullOrEmpty(root.templateId)) errors.Add("Root 必须是独立的普通节点。");

            var templates = new Dictionary<string, NodeTemplate>();
            foreach (var template in document.templates)
            {
                if (template == null || string.IsNullOrEmpty(template.id)) { errors.Add("存在无 ID 的预制模块。"); continue; }
                if (!templates.TryAdd(template.id, template)) errors.Add("重复的预制模块 ID: " + template.id);
            }

            var interfaceItemIds = new HashSet<string>();
            foreach (var node in document.nodes.Where(item => item != null))
                ValidateInterfaceItems(node.interfaceItems, interfaceItemIds, "模块 " + node.id, errors);
            foreach (var template in document.templates.Where(item => item != null))
                ValidateInterfaceItems(template.interfaceItems, interfaceItemIds, "预制模块 " + template.id, errors);

            foreach (var node in document.nodes.Where(item => item != null))
            {
                if (node.id != RootId && !string.IsNullOrEmpty(node.parentId) &&
                    !nodes.ContainsKey(node.parentId))
                    errors.Add("模块的归属节点不存在: " + node.id);
                if (!string.IsNullOrEmpty(node.templateId) && !templates.ContainsKey(node.templateId))
                    errors.Add("模块引用了不存在的预制定义: " + node.id);
                CheckParentCycle(node.id, nodes, false, errors);
            }

            foreach (var template in document.templates.Where(item => item != null))
            {
                if (!string.IsNullOrEmpty(template.parentTemplateId) && !templates.ContainsKey(template.parentTemplateId))
                    errors.Add("预制模块缺少上级定义: " + template.id);
                CheckParentCycle(template.id, templates, true, errors);
            }

            foreach (var node in document.nodes.Where(item => item != null && !string.IsNullOrEmpty(item.templateId)))
            {
                if (!templates.ContainsKey(node.templateId)) continue;
                foreach (var childTemplate in document.templates.Where(item => item.parentTemplateId == node.templateId))
                {
                    if (document.nodes.Count(child => child.parentId == node.id &&
                        child.templateId == childTemplate.id &&
                        child.templateInstanceId == node.templateInstanceId) != 1)
                        errors.Add("预制结构不一致: " + DisplayName(document, node) + " 缺少或重复了 " + childTemplate.name);
                }
                if (node.templateInstanceId != node.id && !nodes.ContainsKey(node.templateInstanceId ?? string.Empty))
                    errors.Add("预制实例根节点不存在: " + node.id);
            }

            var referenceIds = new HashSet<string>();
            var referenceKeys = new HashSet<string>();
            foreach (var reference in document.interfaceReferences)
            {
                if (reference == null || string.IsNullOrEmpty(reference.id))
                {
                    errors.Add("存在无 ID 的接口引用。");
                    continue;
                }
                if (!referenceIds.Add(reference.id)) errors.Add("重复的接口引用 ID: " + reference.id);
                var key = reference.sourceNodeId + "\n" + reference.interfaceId + "\n" + reference.consumerNodeId;
                if (!referenceKeys.Add(key)) errors.Add("重复的接口引用: " + reference.id);
                if (!nodes.TryGetValue(reference.sourceNodeId ?? string.Empty, out var sourceNode) ||
                    !nodes.ContainsKey(reference.consumerNodeId ?? string.Empty) ||
                    reference.sourceNodeId == reference.consumerNodeId ||
                    InterfaceItems(document, sourceNode)?.All(item => item == null || item.id != reference.interfaceId) != false)
                    errors.Add("接口引用目标无效: " + reference.id);
            }

            return errors.Distinct().ToList();
        }

        private static void ValidateInterfaceItems(List<ArchitectureInterfaceItem> items,
            HashSet<string> ids, string owner, List<string> errors)
        {
            if (items == null) { errors.Add(owner + " 缺少接口列表。"); return; }
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.id))
                    errors.Add(owner + " 存在无 ID 的接口项。");
                else if (!ids.Add(item.id)) errors.Add("重复的接口项 ID: " + item.id);
            }
        }

        private static void CheckParentCycle<T>(string startId, Dictionary<string, T> lookup, bool isTemplate, List<string> errors)
        {
            var visited = new HashSet<string>();
            var id = startId;
            while (!string.IsNullOrEmpty(id) && lookup.TryGetValue(id, out var item))
            {
                if (!visited.Add(id)) { errors.Add((isTemplate ? "预制模块" : "模块") + "归属关系存在环: " + startId); break; }
                id = isTemplate ? ((NodeTemplate)(object)item).parentTemplateId : ((ArchitectureNode)(object)item).parentId;
            }
        }
    }
}
