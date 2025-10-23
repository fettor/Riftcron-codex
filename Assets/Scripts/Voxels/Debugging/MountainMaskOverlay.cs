using Tuntenfisch.Voxels.Procedural;
using Tuntenfisch.Voxels.Volume;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using XNode;

namespace Tuntenfisch.Voxels.Debugging
{
    [AddComponentMenu("Voxels/Debug/Mountain Mask Overlay")]
    [RequireComponent(typeof(VoxelVolume), typeof(VoxelConfig))]
    public sealed class MountainMaskOverlay : MonoBehaviour
    {
        [SerializeField]
        private bool m_enableOverlay = false;
        [SerializeField, Range(64, 512)]
        private int m_overlaySize = 256;

        private VoxelConfig m_voxelConfig;
        private RenderTexture m_maskTexture;
        private bool m_writeRequested;

        private void Awake()
        {
            m_voxelConfig = GetComponent<VoxelConfig>();

            if (m_voxelConfig != null)
            {
                m_voxelConfig.GenerationGraph.OnLateDirtied += RefreshNodeState;
                m_voxelConfig.VoxelVolumeConfig.OnLateDirtied += HandleVoxelVolumeConfigChanged;
            }

            RefreshNodeState();
            EnsureTexture(true);
        }

        private void OnEnable()
        {
            RefreshNodeState();
            EnsureTexture();
        }

        private void OnDisable()
        {
            ReleaseTexture();
        }

        private void OnDestroy()
        {
            if (m_voxelConfig != null)
            {
                m_voxelConfig.GenerationGraph.OnLateDirtied -= RefreshNodeState;
                m_voxelConfig.VoxelVolumeConfig.OnLateDirtied -= HandleVoxelVolumeConfigChanged;
            }

            ReleaseTexture();
        }

        private void OnValidate()
        {
            m_overlaySize = math.clamp(m_overlaySize, 64, 512);
            if (!isActiveAndEnabled)
            {
                return;
            }

            EnsureTexture(true);
        }

        private void HandleVoxelVolumeConfigChanged()
        {
            EnsureTexture(true);
        }

        private void RefreshNodeState()
        {
            m_writeRequested = false;

            if (m_voxelConfig == null || m_voxelConfig.GenerationGraph == null)
            {
                return;
            }

            foreach (Node node in m_voxelConfig.GenerationGraph.nodes)
            {
                if (node is MountainNode mountainNode && mountainNode.WriteMountainMask)
                {
                    m_writeRequested = true;
                    break;
                }
            }
        }

        private void EnsureTexture(bool forceRecreate = false)
        {
            if (!m_enableOverlay)
            {
                ReleaseTexture();
                return;
            }

            if (m_voxelConfig == null || m_voxelConfig.VoxelVolumeConfig == null)
            {
                return;
            }

            int size = m_voxelConfig.VoxelVolumeConfig.NumberOfVoxelsAlongAxis;
            if (size <= 0)
            {
                ReleaseTexture();
                return;
            }

            if (!forceRecreate && m_maskTexture != null && m_maskTexture.width == size)
            {
                return;
            }

            ReleaseTexture();

            m_maskTexture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = true,
                dimension = TextureDimension.Tex2D,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MountainMaskOverlay"
            };
            m_maskTexture.Create();
        }

        private void ReleaseTexture()
        {
            if (m_maskTexture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(m_maskTexture);
            }
            else
            {
                DestroyImmediate(m_maskTexture);
            }
            m_maskTexture = null;
        }

        private void ClearTexture()
        {
            if (m_maskTexture == null)
            {
                return;
            }

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = m_maskTexture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;
        }

        internal bool TryAcquireMask(out RenderTexture texture)
        {
            EnsureTexture(false);

            bool shouldCapture = m_enableOverlay && m_writeRequested && m_maskTexture != null;
            if (!shouldCapture)
            {
                texture = null;
                return false;
            }

            ClearTexture();
            texture = m_maskTexture;
            return true;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !m_enableOverlay || m_maskTexture == null)
            {
                return;
            }

            Rect rect = new Rect(16.0f, 16.0f, m_overlaySize, m_overlaySize);
            GUI.DrawTexture(rect, m_maskTexture, ScaleMode.StretchToFill, false);
            GUI.Box(rect, GUIContent.none);

            Rect legendRect = new Rect(rect.x, rect.y + rect.height + 4.0f, rect.width, 38.0f);
            GUI.Box(legendRect, GUIContent.none);
            Rect textRect = new Rect(legendRect.x + 6.0f, legendRect.y + 6.0f, legendRect.width - 12.0f, 26.0f);
            string status = m_writeRequested ? "Active" : "Awaiting MountainNode";
            GUI.Label(textRect, $"Mountain Mask (R) / Plateau (G)\nStatus: {status}");
        }
    }
}
