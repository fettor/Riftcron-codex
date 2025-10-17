using Tuntenfisch.World.Planning;
using UnityEngine;
using XNode;

namespace Tuntenfisch.Voxels.Procedural
{
    [CreateNodeMenu("Generation Nodes/Biomes/Biome Mixer", order = (int)NodeType.BiomeMixer)]
    [NodeWidth(240)]
    [NodeTint(c_internalNodeColor)]
    public sealed class BiomeMixerNode : GenerationGraphNode
    {
        public BiomeLibrary BiomeLibrary => m_biomeLibrary;
        public float MixStrength => m_mixStrength;

        [Input(backingValue = ShowBackingValue.Never, connectionType = ConnectionType.Override, typeConstraint = TypeConstraint.Strict)]
        [SerializeField]
        private float m_position;

        [Output(backingValue = ShowBackingValue.Never, connectionType = ConnectionType.Multiple, typeConstraint = TypeConstraint.Strict)]
        [SerializeField]
        private float m_output;

        [SerializeField]
        private BiomeLibrary m_biomeLibrary;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_mixStrength = 1.0f;

        public override NodeType GetNodeType() => NodeType.BiomeMixer;
    }
}
