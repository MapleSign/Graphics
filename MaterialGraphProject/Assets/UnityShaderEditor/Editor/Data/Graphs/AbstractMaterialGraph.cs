using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.Graphing.Util;

namespace UnityEditor.ShaderGraph
{
    [Serializable]
    public abstract class AbstractMaterialGraph : IGraph, ISerializationCallbackReceiver, IGenerateProperties
    {
        public IGraphObject owner { get; set; }

        #region Property data

        [NonSerialized]
        List<IShaderProperty> m_Properties = new List<IShaderProperty>();

        public IEnumerable<IShaderProperty> properties
        {
            get { return m_Properties; }
        }

        [SerializeField]
        List<SerializationHelper.JSONSerializedElement> m_SerializedProperties = new List<SerializationHelper.JSONSerializedElement>();

        [NonSerialized]
        List<IShaderProperty> m_AddedProperties = new List<IShaderProperty>();

        public IEnumerable<IShaderProperty> addedProperties
        {
            get { return m_AddedProperties; }
        }

        [NonSerialized]
        List<Guid> m_RemovedProperties = new List<Guid>();

        public IEnumerable<Guid> removedProperties
        {
            get { return m_RemovedProperties; }
        }

        #endregion

        #region Node data

        [NonSerialized]
        Dictionary<Guid, INode> m_Nodes = new Dictionary<Guid, INode>();

        public IEnumerable<T> GetNodes<T>() where T : INode
        {
            return m_Nodes.Values.OfType<T>();
        }

        [SerializeField]
        List<SerializationHelper.JSONSerializedElement> m_SerializableNodes = new List<SerializationHelper.JSONSerializedElement>();

        [NonSerialized]
        List<INode> m_AddedNodes = new List<INode>();

        public IEnumerable<INode> addedNodes
        {
            get { return m_AddedNodes; }
        }

        [NonSerialized]
        List<INode> m_RemovedNodes = new List<INode>();

        public IEnumerable<INode> removedNodes
        {
            get { return m_RemovedNodes; }
        }

        #endregion

        #region Edge data

        [NonSerialized]
        List<IEdge> m_Edges = new List<IEdge>();

        public IEnumerable<IEdge> edges
        {
            get { return m_Edges; }
        }

        [SerializeField]
        List<SerializationHelper.JSONSerializedElement> m_SerializableEdges = new List<SerializationHelper.JSONSerializedElement>();

        [NonSerialized]
        Dictionary<Guid, List<IEdge>> m_NodeEdges = new Dictionary<Guid, List<IEdge>>();

        [NonSerialized]
        List<IEdge> m_AddedEdges = new List<IEdge>();

        public IEnumerable<IEdge> addedEdges
        {
            get { return m_AddedEdges; }
        }

        [NonSerialized]
        List<IEdge> m_RemovedEdges = new List<IEdge>();

        public IEnumerable<IEdge> removedEdges
        {
            get { return m_RemovedEdges; }
        }

        #endregion

        [SerializeField]
        InspectorPreviewData m_PreviewData = new InspectorPreviewData();

        public InspectorPreviewData previewData
        {
            get { return m_PreviewData; }
            set { m_PreviewData = value; }
        }

        public void ClearChanges()
        {
            m_AddedNodes.Clear();
            m_RemovedNodes.Clear();
            m_AddedEdges.Clear();
            m_RemovedEdges.Clear();
            m_AddedProperties.Clear();
            m_RemovedProperties.Clear();
        }

        public virtual void AddNode(INode node)
        {
            if (node is AbstractMaterialNode)
            {
                AddNodeNoValidate(node);
                ValidateGraph();
            }
            else
            {
                Debug.LogWarningFormat("Trying to add node {0} to Material graph, but it is not a {1}", node, typeof(AbstractMaterialNode));
            }
        }

        void AddNodeNoValidate(INode node)
        {
            m_Nodes.Add(node.guid, node);
            node.owner = this;
            m_AddedNodes.Add(node);
        }

        public void RemoveNode(INode node)
        {
            if (!node.canDeleteNode)
                return;

            m_Nodes.Remove(node.guid);
            m_RemovedNodes.Add(node);
            ValidateGraph();
        }

        void RemoveNodeNoValidate(INode node)
        {
            if (!node.canDeleteNode)
                return;

            m_Nodes.Remove(node.guid);
            m_RemovedNodes.Add(node);
        }

        void AddEdgeToNodeEdges(IEdge edge)
        {
            List<IEdge> inputEdges;
            if (!m_NodeEdges.TryGetValue(edge.inputSlot.nodeGuid, out inputEdges))
                m_NodeEdges[edge.inputSlot.nodeGuid] = inputEdges = new List<IEdge>();
            inputEdges.Add(edge);

            List<IEdge> outputEdges;
            if (!m_NodeEdges.TryGetValue(edge.outputSlot.nodeGuid, out outputEdges))
                m_NodeEdges[edge.outputSlot.nodeGuid] = outputEdges = new List<IEdge>();
            outputEdges.Add(edge);
        }

        IEdge ConnectNoValidate(SlotReference fromSlotRef, SlotReference toSlotRef)
        {
            var fromNode = GetNodeFromGuid(fromSlotRef.nodeGuid);
            var toNode = GetNodeFromGuid(toSlotRef.nodeGuid);

            if (fromNode == null || toNode == null)
                return null;

            // if fromNode is already connected to toNode
            // do now allow a connection as toNode will then
            // have an edge to fromNode creating a cycle.
            // if this is parsed it will lead to an infinite loop.
            var dependentNodes = new List<INode>();
            NodeUtils.CollectNodesNodeFeedsInto(dependentNodes, toNode);
            if (dependentNodes.Contains(fromNode))
                return null;

            var fromSlot = fromNode.FindSlot<ISlot>(fromSlotRef.slotId);
            var toSlot = toNode.FindSlot<ISlot>(toSlotRef.slotId);

            if (fromSlot.isOutputSlot == toSlot.isOutputSlot)
                return null;

            var outputSlot = fromSlot.isOutputSlot ? fromSlotRef : toSlotRef;
            var inputSlot = fromSlot.isInputSlot ? fromSlotRef : toSlotRef;

            s_TempEdges.Clear();
            GetEdges(inputSlot, s_TempEdges);

            // remove any inputs that exits before adding
            foreach (var edge in s_TempEdges)
            {
                RemoveEdgeNoValidate(edge);
            }

            var newEdge = new Edge(outputSlot, inputSlot);
            m_Edges.Add(newEdge);
            m_AddedEdges.Add(newEdge);
            AddEdgeToNodeEdges(newEdge);

            //Debug.LogFormat("Connected edge: {0} -> {1} ({2} -> {3})\n{4}", newEdge.outputSlot.nodeGuid, newEdge.inputSlot.nodeGuid, fromNode.name, toNode.name, Environment.StackTrace);
            return newEdge;
        }

        public virtual IEdge Connect(SlotReference fromSlotRef, SlotReference toSlotRef)
        {
            var newEdge = ConnectNoValidate(fromSlotRef, toSlotRef);
            ValidateGraph();
            return newEdge;
        }

        public virtual void RemoveEdge(IEdge e)
        {
            RemoveEdgeNoValidate(e);
            ValidateGraph();
        }

        public void RemoveElements(IEnumerable<INode> nodes, IEnumerable<IEdge> edges)
        {
            foreach (var edge in edges.ToArray())
                RemoveEdgeNoValidate(edge);

            foreach (var serializableNode in nodes.ToArray())
                RemoveNodeNoValidate(serializableNode);

            ValidateGraph();
        }

        protected void RemoveEdgeNoValidate(IEdge e)
        {
            e = m_Edges.FirstOrDefault(x => x.Equals(e));
            if (e == null)
                throw new ArgumentException("Trying to remove an edge that does not exist.", "e");
            m_Edges.Remove(e);

            List<IEdge> inputNodeEdges;
            if (m_NodeEdges.TryGetValue(e.inputSlot.nodeGuid, out inputNodeEdges))
                inputNodeEdges.Remove(e);

            List<IEdge> outputNodeEdges;
            if (m_NodeEdges.TryGetValue(e.outputSlot.nodeGuid, out outputNodeEdges))
                outputNodeEdges.Remove(e);

            m_RemovedEdges.Add(e);
        }

        public INode GetNodeFromGuid(Guid guid)
        {
            INode node;
            m_Nodes.TryGetValue(guid, out node);
            return node;
        }

        public bool ContainsNodeGuid(Guid guid)
        {
            return m_Nodes.ContainsKey(guid);
        }

        public T GetNodeFromGuid<T>(Guid guid) where T : INode
        {
            var node = GetNodeFromGuid(guid);
            if (node is T)
                return (T)node;
            return default(T);
        }

        public void GetEdges(SlotReference s, List<IEdge> foundEdges)
        {
            var node = GetNodeFromGuid(s.nodeGuid);
            if (node == null)
            {
                Debug.LogWarning("Node does not exist");
                return;
            }
            ISlot slot = node.FindSlot<ISlot>(s.slotId);

            List<IEdge> candidateEdges;
            if (!m_NodeEdges.TryGetValue(s.nodeGuid, out candidateEdges))
                return;

            foreach (var edge in candidateEdges)
            {
                var cs = slot.isInputSlot ? edge.inputSlot : edge.outputSlot;
                if (cs.nodeGuid == s.nodeGuid && cs.slotId == s.slotId)
                    foundEdges.Add(edge);
            }
        }

        public virtual void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            foreach (var prop in properties)
                collector.AddShaderProperty(prop);
        }

        public void AddShaderProperty(IShaderProperty property)
        {
            if (property == null)
                return;

            if (m_Properties.Contains(property))
                return;

            property.displayName = property.displayName.Trim();
            if (m_Properties.Any(p => p.displayName == property.displayName))
            {
                var regex = new Regex(@"^" + Regex.Escape(property.displayName) + @" \((\d+)\)$");
                var existingDuplicateNumbers = m_Properties.Select(p => regex.Match(p.displayName)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Where(n => n > 0).ToList();

                var duplicateNumber = 1;
                existingDuplicateNumbers.Sort();
                if (existingDuplicateNumbers.Any() && existingDuplicateNumbers.First() == 1)
                {
                    duplicateNumber = existingDuplicateNumbers.Last() + 1;
                    for (var i = 1; i < existingDuplicateNumbers.Count; i++)
                    {
                        if (existingDuplicateNumbers[i - 1] != existingDuplicateNumbers[i] - 1)
                        {
                            duplicateNumber = existingDuplicateNumbers[i - 1] + 1;
                            break;
                        }
                    }
                }
                property.displayName = string.Format("{0} ({1})", property.displayName, duplicateNumber);
            }

            m_Properties.Add(property);
            m_AddedProperties.Add(property);
        }

        public void RemoveShaderProperty(Guid guid)
        {
            var propertyNodes = GetNodes<PropertyNode>().Where(x => x.propertyGuid == guid).ToList();
            foreach (var propNode in propertyNodes)
                ReplacePropertyNodeWithConcreteNodeNoValidate(propNode);

            RemoveShaderPropertyNoValidate(guid);

            ValidateGraph();
        }

        void RemoveShaderPropertyNoValidate(Guid guid)
        {
            if (m_Properties.RemoveAll(x => x.guid == guid) > 0)
                m_RemovedProperties.Add(guid);
        }

        static List<IEdge> s_TempEdges = new List<IEdge>();

        public void ReplacePropertyNodeWithConcreteNode(PropertyNode propertyNode)
        {
            ReplacePropertyNodeWithConcreteNodeNoValidate(propertyNode);
            ValidateGraph();
        }

        void ReplacePropertyNodeWithConcreteNodeNoValidate(PropertyNode propertyNode)
        {
            var property = properties.FirstOrDefault(x => x.guid == propertyNode.propertyGuid);
            if (property == null)
                return;

            var node = property.ToConcreteNode();
            if (!(node is AbstractMaterialNode))
                return;

            var slot = propertyNode.FindOutputSlot<MaterialSlot>(PropertyNode.OutputSlotId);
            var newSlot = node.GetOutputSlots<MaterialSlot>().FirstOrDefault(s => s.valueType == slot.valueType);
            if (newSlot == null)
                return;

            node.drawState = propertyNode.drawState;
            AddNodeNoValidate(node);

            foreach (var edge in this.GetEdges(slot.slotReference))
                ConnectNoValidate(newSlot.slotReference, edge.inputSlot);

            RemoveNodeNoValidate(propertyNode);
        }

        public void ValidateGraph()
        {
            var propertyNodes = GetNodes<PropertyNode>().Where(n => !m_Properties.Any(p => p.guid == n.propertyGuid)).ToArray();
            foreach (var pNode in propertyNodes)
                ReplacePropertyNodeWithConcreteNodeNoValidate(pNode);

            //First validate edges, remove any
            //orphans. This can happen if a user
            //manually modifies serialized data
            //of if they delete a node in the inspector
            //debug view.
            foreach (var edge in edges.ToArray())
            {
                var outputNode = GetNodeFromGuid(edge.outputSlot.nodeGuid);
                var inputNode = GetNodeFromGuid(edge.inputSlot.nodeGuid);

                if (outputNode == null
                    || inputNode == null
                    || outputNode.FindOutputSlot<ISlot>(edge.outputSlot.slotId) == null
                    || inputNode.FindInputSlot<ISlot>(edge.inputSlot.slotId) == null)
                {
                    //orphaned edge
                    RemoveEdgeNoValidate(edge);
                }
            }

            foreach (var node in GetNodes<INode>())
                node.ValidateNode();

            foreach (var edge in m_AddedEdges.ToList())
            {
                if (!ContainsNodeGuid(edge.outputSlot.nodeGuid) || !ContainsNodeGuid(edge.inputSlot.nodeGuid))
                {
                    Debug.LogWarningFormat("Added edge is invalid: {0} -> {1}\n{2}", edge.outputSlot.nodeGuid, edge.inputSlot.nodeGuid, Environment.StackTrace);
                    m_AddedEdges.Remove(edge);
                }
            }
        }

        public void ReplaceWith(IGraph other)
        {
            var otherMg = other as AbstractMaterialGraph;
            if (otherMg == null)
                throw new ArgumentException("Can only replace with another AbstractMaterialGraph", "other");

            using (var removedPropertiesPooledObject = ListPool<Guid>.GetDisposable())
            {
                var removedPropertyGuids = removedPropertiesPooledObject.value;
                foreach (var property in m_Properties)
                    removedPropertyGuids.Add(property.guid);
                foreach (var propertyGuid in removedPropertyGuids)
                    RemoveShaderPropertyNoValidate(propertyGuid);
            }
            foreach (var otherProperty in otherMg.properties)
            {
                if (!properties.Any(p => p.guid == otherProperty.guid))
                    AddShaderProperty(otherProperty);
            }

            other.ValidateGraph();
            ValidateGraph();

            // Current tactic is to remove all nodes and edges and then re-add them, such that depending systems
            // will re-initialize with new references.
            using (var pooledList = ListPool<IEdge>.GetDisposable())
            {
                var removedNodeEdges = pooledList.value;
                removedNodeEdges.AddRange(m_Edges);
                foreach (var edge in removedNodeEdges)
                    RemoveEdgeNoValidate(edge);
            }

            using (var removedNodesPooledObject = ListPool<Guid>.GetDisposable())
            {
                var removedNodeGuids = removedNodesPooledObject.value;
                removedNodeGuids.AddRange(m_Nodes.Keys);
                foreach (var nodeGuid in removedNodeGuids)
                    RemoveNodeNoValidate(m_Nodes[nodeGuid]);
            }

            ValidateGraph();

            foreach (var node in other.GetNodes<INode>())
                AddNodeNoValidate(node);

            foreach (var edge in other.edges)
                ConnectNoValidate(edge.outputSlot, edge.inputSlot);

            ValidateGraph();
        }

        public void OnBeforeSerialize()
        {
            m_SerializableNodes = SerializationHelper.Serialize<INode>(m_Nodes.Values);
            m_SerializableEdges = SerializationHelper.Serialize<IEdge>(m_Edges);
            m_SerializedProperties = SerializationHelper.Serialize<IShaderProperty>(m_Properties);
        }

        public virtual void OnAfterDeserialize()
        {
            // have to deserialize 'globals' before nodes
            m_Properties = SerializationHelper.Deserialize<IShaderProperty>(m_SerializedProperties, null);
            var nodes = SerializationHelper.Deserialize<INode>(m_SerializableNodes, GraphUtil.GetLegacyTypeRemapping());
            m_Nodes = new Dictionary<Guid, INode>(nodes.Count);
            foreach (var node in nodes)
            {
                node.owner = this;
                node.UpdateNodeAfterDeserialization();
                m_Nodes.Add(node.guid, node);
            }

            m_SerializableNodes = null;

            m_Edges = SerializationHelper.Deserialize<IEdge>(m_SerializableEdges, null);
            m_SerializableEdges = null;
            foreach (var edge in m_Edges)
                AddEdgeToNodeEdges(edge);
        }

        public void OnEnable()
        {
            foreach (var node in GetNodes<INode>().OfType<IOnAssetEnabled>())
            {
                node.OnEnable();
            }
        }
    }

    [Serializable]
    public class InspectorPreviewData
    {
        public SerializableMesh serializedMesh = new SerializableMesh();

        [NonSerialized]
        public Quaternion rotation = Quaternion.identity;
    }
}
