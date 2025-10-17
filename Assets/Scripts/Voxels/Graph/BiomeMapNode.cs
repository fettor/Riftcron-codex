using Tuntenfisch.World.Planning;
using UnityEngine;
using XNode;

namespace Tuntenfisch.Voxels.Procedural
{
    [CreateNodeMenu("Generation Nodes/Biomes/Biome Map", order = (int)NodeType.BiomeMap)]
    [NodeWidth(240)]
    [NodeTint(c_internalNodeColor)]
    public sealed class BiomeMapNode : GenerationGraphNode
    {
        public BiomeLibrary BiomeLibrary => m_biomeLibrary;

        [Input(backingValue = ShowBackingValue.Never, connectionType = ConnectionType.Override, typeConstraint = TypeConstraint.Strict)]
        [SerializeField]
        private float m_position;

        [Output(backingValue = ShowBackingValue.Never, connectionType = ConnectionType.Override, typeConstraint = TypeConstraint.Strict)]
        [SerializeField]
        private float m_output;

        [SerializeField]
        private BiomeLibrary m_biomeLibrary;

        [SerializeField, Range(0.0f, 8.0f)]
        private float m_weightGain = 1.0f;

        public float WeightGain => m_weightGain;

        public override NodeType GetNodeType() => NodeType.BiomeMap;
    }
}
