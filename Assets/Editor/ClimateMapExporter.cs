using System;
using System.IO;
using Tuntenfisch.World;
using Tuntenfisch.World.Planning;
using UnityEditor;
using UnityEngine;

namespace Tuntenfisch.World.Editor
{
    public sealed class ClimateMapExporterWindow : EditorWindow
    {
        private const string k_defaultFilename = "ClimateMap.png";
        private const float k_minPreviewSize = 128.0f;
        private const float k_maxPreviewSize = 4096.0f;

        [MenuItem("Tools/World/Climate Map Exporter")]
        private static void OpenWindow()
        {
            ClimateMapExporterWindow window = GetWindow<ClimateMapExporterWindow>("Climate Map Exporter");
            window.minSize = new Vector2(520.0f, 520.0f);
        }

        private RegionPlanner m_regionPlanner;
        private BiomeLibrary m_biomeLibrary;
        private Vector2Int m_regionGrid = new Vector2Int(6, 6);
        private bool m_centerOnOrigin = true;
        private bool m_previewInWindow = true;
        private string m_saveDirectory = "Assets";
        private string m_fileName = k_defaultFilename;
        private Texture2D m_previewTexture;
        private bool m_includeWeights = true;
        private float m_previewScale = 1.0f;

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                m_regionPlanner = (RegionPlanner)EditorGUILayout.ObjectField("Region Planner", m_regionPlanner, typeof(RegionPlanner), true);
                if (GUILayout.Button("Pick", GUILayout.Width(48.0f)))
                {
                    m_regionPlanner = FindObjectOfType<RegionPlanner>();
                    GUI.FocusControl(null);
                }
            }

            m_biomeLibrary = (BiomeLibrary)EditorGUILayout.ObjectField("Biome Library", m_biomeLibrary, typeof(BiomeLibrary), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Region Grid", EditorStyles.boldLabel);
            m_regionGrid = Vector2Int.Max(Vector2Int.one, EditorGUILayout.Vector2IntField("Regions (X,Z)", m_regionGrid));
            m_centerOnOrigin = EditorGUILayout.Toggle("Center Around Origin", m_centerOnOrigin);
            m_includeWeights = EditorGUILayout.Toggle("Use Biome Weights", m_includeWeights);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            m_previewInWindow = EditorGUILayout.Toggle("Preview In Window", m_previewInWindow);
            if (m_previewInWindow)
            {
                EditorGUI.indentLevel++;
                m_previewScale = EditorGUILayout.Slider("Preview Scale", m_previewScale, 0.25f, 4.0f);
                EditorGUI.indentLevel--;
            }
            using (new EditorGUI.DisabledGroupScope(!m_previewInWindow))
            {
                if (m_previewTexture != null)
                {
                    EditorGUILayout.Space();
                    float baseSize = Mathf.Min(m_previewTexture.width, m_previewTexture.height);
                    float targetSize = Mathf.Clamp(baseSize * m_previewScale, k_minPreviewSize, k_maxPreviewSize);
                    float availableWidth = Mathf.Max(k_minPreviewSize, position.width - 40.0f);
                    float availableHeight = Mathf.Max(k_minPreviewSize, position.height - 260.0f);
                    targetSize = Mathf.Min(targetSize, availableWidth, availableHeight);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        Rect previewRect = GUILayoutUtility.GetRect(targetSize, targetSize, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                        previewRect.width = previewRect.height = targetSize;
                        DrawPreview(previewRect);
                        GUILayout.FlexibleSpace();
                    }
                }
                else if (m_previewInWindow)
                {
                    EditorGUILayout.HelpBox("Generate a map to preview it here.", MessageType.Info);
                }
            }

            using (new EditorGUI.DisabledGroupScope(m_previewInWindow))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Save Directory");
                    m_saveDirectory = EditorGUILayout.TextField(m_saveDirectory);
                    if (GUILayout.Button("…", GUILayout.Width(28.0f)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Choose Folder", m_saveDirectory, string.Empty);
                        if (!string.IsNullOrEmpty(picked))
                        {
                            m_saveDirectory = picked;
                            GUI.FocusControl(null);
                        }
                    }
                }

                m_fileName = EditorGUILayout.TextField("File Name", m_fileName);
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Generate", GUILayout.Width(120.0f)))
                {
                    GenerateClimateMap();
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawPreview(Rect rect)
        {
            if (m_previewTexture == null)
            {
                EditorGUI.DropShadowLabel(rect, "No preview");
                return;
            }

            GUI.Box(rect, GUIContent.none);
            Rect inner = new Rect(rect.x + 4.0f, rect.y + 4.0f, rect.width - 8.0f, rect.height - 8.0f);
            GUI.DrawTexture(inner, m_previewTexture, ScaleMode.ScaleToFit, false);
        }

        private void GenerateClimateMap()
        {
            if (m_regionPlanner == null)
            {
                Debug.LogError("Climate map exporter requires a RegionPlanner reference.");
                return;
            }

            BiomeLibrary library = m_biomeLibrary != null ? m_biomeLibrary : m_regionPlanner.ClimateProvider?.BiomeLibrary;
            if (library == null)
            {
                Debug.LogError("No BiomeLibrary provided or resolvable from RegionPlanner.");
                return;
            }

            Vector2Int grid = Vector2Int.Max(Vector2Int.one, m_regionGrid);
            int tileResolution = m_regionPlanner.ClimateProvider != null ? m_regionPlanner.ClimateProvider.TileResolution : 128;
            int texWidth = grid.x * tileResolution;
            int texHeight = grid.y * tileResolution;

            Texture2D output = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            Vector2Int minCorner = m_centerOnOrigin ? new Vector2Int(-grid.x / 2, -grid.y / 2) : Vector2Int.zero;

            for (int z = 0; z < grid.y; ++z)
            {
                for (int x = 0; x < grid.x; ++x)
                {
                    Vector2Int regionCoord = minCorner + new Vector2Int(x, z);
                    RegionKey regionKey = new RegionKey(regionCoord.x, regionCoord.y);
                    RegionPlan plan = m_regionPlanner.RebuildPlan(regionKey);
                    Color[] colors = ExtractColors(plan, library, tileResolution);
                    output.SetPixels(x * tileResolution, z * tileResolution, tileResolution, tileResolution, colors);
                }
            }

            output.Apply(false, false);

            if (m_previewInWindow)
            {
                if (m_previewTexture != null)
                {
                    DestroyImmediate(m_previewTexture);
                }
                m_previewTexture = output;
            }
            else
            {
                string path = Path.Combine(m_saveDirectory, string.IsNullOrEmpty(m_fileName) ? k_defaultFilename : m_fileName);
                path = Path.ChangeExtension(path, ".png");
                byte[] data = output.EncodeToPNG();
                File.WriteAllBytes(path, data);
                DestroyImmediate(output);
                Debug.Log($"Climate map saved to {path}");
            }
        }

        private Color[] ExtractColors(RegionPlan plan, BiomeLibrary library, int tileResolution)
        {
            Color[] colors = new Color[tileResolution * tileResolution];

            if (m_includeWeights && plan.BiomeWeightTiles.Count > 0)
            {
                int biomeCount = Mathf.Min(library.BiomeCount, plan.BiomeWeightTiles.Count);
                Color[] accum = new Color[colors.Length];

                for (int b = 0; b < biomeCount; ++b)
                {
                    Texture2D weightTile = plan.BiomeWeightTiles[b];
                    Color[] pixels = GetPixelsSafe(weightTile);
                    if (pixels == null)
                    {
                        continue;
                    }

                    Color biomeColor = library.GetBiome(b).DebugColor;
                    for (int i = 0; i < colors.Length; ++i)
                    {
                        float weight = pixels[i].r;
                        accum[i] += biomeColor * weight;
                    }
                }

                for (int i = 0; i < colors.Length; ++i)
                {
                    colors[i] = accum[i];
                }
            }
            else
            {
                Texture2D temperature = plan.ClimateTiles.Count > 0 ? plan.ClimateTiles[0] : null;
                Texture2D moisture = plan.ClimateTiles.Count > 1 ? plan.ClimateTiles[1] : null;
                Color[] temps = GetPixelsSafe(temperature);
                Color[] moist = GetPixelsSafe(moisture);

                for (int i = 0; i < colors.Length; ++i)
                {
                    float t = temps != null ? temps[i].r : 0.5f;
                    float m = moist != null ? moist[i].r : 0.5f;
                    colors[i] = new Color(t, m, 1.0f - t, 1.0f);
                }
            }

            return colors;
        }

        private static Color[] GetPixelsSafe(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }

            try
            {
                return source.GetPixels();
            }
            catch (Exception e) when (e is UnityException || e is ArgumentException)
            {
                RenderTexture temp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, temp);

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = temp;
                Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply(false, true);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temp);

                Color[] pixels = readable.GetPixels();
                UnityEngine.Object.DestroyImmediate(readable);
                return pixels;
            }
        }

        private static string tr(string text) => text;

        private void OnDestroy()
        {
            if (m_previewTexture != null)
            {
                DestroyImmediate(m_previewTexture);
                m_previewTexture = null;
            }
        }
    }
}
