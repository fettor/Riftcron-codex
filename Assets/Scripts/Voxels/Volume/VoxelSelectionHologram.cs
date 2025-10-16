using UnityEngine;

namespace Tuntenfisch.Voxels.Volume
{
    public class VoxelSelectionHologram : MonoBehaviour
    {
        [SerializeField]
        private Color m_outlineColor = new Color(0.0f, 0.75f, 1.0f, 1.0f);
        [SerializeField]
        private Color m_gridColor = new Color(0.0f, 0.75f, 1.0f, 0.35f);

        private static Material s_lineMaterial;
        private bool m_visible;
        private Vector3 m_center;
        private float m_voxelSpacing = 1.0f;
        private int m_size = 1;

        public void SetVisible(bool visible)
        {
            m_visible = visible;
        }

        public void UpdateHologram(Vector3 center, float voxelSpacing, int size)
        {
            m_center = center;
            m_voxelSpacing = Mathf.Max(0.0001f, voxelSpacing);
            m_size = Mathf.Max(1, size);
        }

        private void OnDisable()
        {
            m_visible = false;
        }

        private void OnRenderObject()
        {
            if (!m_visible || m_size <= 0)
            {
                return;
            }

            EnsureMaterial();
            if (s_lineMaterial == null)
            {
                return;
            }

            Vector3 extent = 0.5f * m_voxelSpacing * new Vector3(m_size, m_size, m_size);
            Vector3 min = m_center - extent;
            Vector3 max = m_center + extent;

            s_lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            GL.Begin(GL.LINES);
            GL.Color(m_gridColor);
            DrawGridLines(min, max, m_size, m_voxelSpacing);

            GL.Color(m_outlineColor);
            DrawOutline(min, max);
            GL.End();

            GL.PopMatrix();
        }

        private static void EnsureMaterial()
        {
            if (s_lineMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return;
            }

            s_lineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            s_lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            s_lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            s_lineMaterial.SetInt("_ZWrite", 0);
        }

        private static void DrawOutline(Vector3 min, Vector3 max)
        {
            Vector3 a = new Vector3(min.x, min.y, min.z);
            Vector3 b = new Vector3(max.x, min.y, min.z);
            Vector3 c = new Vector3(max.x, min.y, max.z);
            Vector3 d = new Vector3(min.x, min.y, max.z);

            Vector3 e = new Vector3(min.x, max.y, min.z);
            Vector3 f = new Vector3(max.x, max.y, min.z);
            Vector3 g = new Vector3(max.x, max.y, max.z);
            Vector3 h = new Vector3(min.x, max.y, max.z);

            DrawLine(a, b);
            DrawLine(b, c);
            DrawLine(c, d);
            DrawLine(d, a);

            DrawLine(e, f);
            DrawLine(f, g);
            DrawLine(g, h);
            DrawLine(h, e);

            DrawLine(a, e);
            DrawLine(b, f);
            DrawLine(c, g);
            DrawLine(d, h);
        }

        private static void DrawGridLines(Vector3 min, Vector3 max, int size, float spacing)
        {
            int segments = Mathf.Max(1, size);
            float length = segments * spacing;

            for (int x = 0; x <= segments; x++)
            {
                float offsetX = x * spacing;

                for (int z = 0; z <= segments; z++)
                {
                    float offsetZ = z * spacing;
                    Vector3 start = new Vector3(min.x + offsetX, min.y, min.z + offsetZ);
                    Vector3 end = start + new Vector3(0.0f, length, 0.0f);
                    DrawLine(start, end);
                }

                for (int y = 0; y <= segments; y++)
                {
                    float offsetY = y * spacing;
                    Vector3 start = new Vector3(min.x + offsetX, min.y + offsetY, min.z);
                    Vector3 end = start + new Vector3(0.0f, 0.0f, length);
                    DrawLine(start, end);
                }
            }

            for (int z = 0; z <= segments; z++)
            {
                float offsetZ = z * spacing;

                for (int y = 0; y <= segments; y++)
                {
                    float offsetY = y * spacing;
                    Vector3 start = new Vector3(min.x, min.y + offsetY, min.z + offsetZ);
                    Vector3 end = start + new Vector3(length, 0.0f, 0.0f);
                    DrawLine(start, end);
                }
            }
        }

        private static void DrawLine(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }
    }
}
