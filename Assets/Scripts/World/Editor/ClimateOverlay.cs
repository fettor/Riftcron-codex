#if UNITY_EDITOR
using System.Collections.Generic;
using Tuntenfisch.World.Planning;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;

namespace Tuntenfisch.World.Editor
{
    [InitializeOnLoad]
    internal static class ClimateOverlay
    {
        private const float k_windowWidth = 240.0f;
        private const float k_previewSize = 200.0f;

        private static readonly List<GUIContent> s_options = new List<GUIContent>();
        private static BiomeLibrary s_cachedLibrary;
        private static int s_cachedBiomeCount;
        private static int s_selectedIndex;

        static ClimateOverlay()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += _ => RefreshOptions();
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            RegionPlanner planner = GetPlanner();

            if (planner == null || planner.Settings == null)
            {
                return;
            }

            RefreshOptions(planner);

            Transform viewer = WorldManager.ViewerTransform;

            if (viewer == null && sceneView != null && sceneView.camera != null)
            {
                viewer = sceneView.camera.transform;
            }

            if (viewer == null)
            {
                return;
            }

            RegionKey regionKey = planner.Settings.GetRegionKeyFromWorldPosition((float3)viewer.position);

            if (!planner.TryGetPlan(regionKey, out RegionPlan plan) || plan == null)
            {
                return;
            }

            Texture preview = ResolveTextureForSelection(plan, out Color tint);

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(16.0f, 16.0f, k_windowWidth, k_previewSize + 64.0f), GUI.skin.window);
            GUILayout.Label($"Climate Overlay - Region {regionKey}", EditorStyles.boldLabel);

            if (s_options.Count == 0)
            {
                GUILayout.Label("No climate maps available.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                int clampedIndex = Mathf.Clamp(s_selectedIndex, 0, s_options.Count - 1);
                GUIContent[] optionArray = s_options.ToArray();
                s_selectedIndex = EditorGUILayout.Popup(new GUIContent("Layer"), clampedIndex, optionArray);

                Rect previewRect = GUILayoutUtility.GetRect(k_previewSize, k_previewSize, GUILayout.ExpandWidth(false));

                if (preview != null)
                {
                    Color previousColor = GUI.color;
                    GUI.color = tint;
                    GUI.DrawTexture(previewRect, preview, ScaleMode.StretchToFill, tint.a < 0.99f);
                    GUI.color = previousColor;
                }
                else
                {
                    EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f));
                    GUI.Label(previewRect, "No texture", EditorStyles.centeredGreyMiniLabel);
                }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static Texture ResolveTextureForSelection(RegionPlan plan, out Color tint)
        {
            tint = Color.white;

            if (plan == null)
            {
                return null;
            }

            if (s_selectedIndex == 0 && plan.ClimateTiles.Count > 0)
            {
                return plan.ClimateTiles[0];
            }

            if (s_selectedIndex == 1 && plan.ClimateTiles.Count > 1)
            {
                return plan.ClimateTiles[1];
            }

            int biomeTextureIndex = s_selectedIndex - 2;

            if (biomeTextureIndex >= 0 && biomeTextureIndex < plan.BiomeWeightTiles.Count && s_cachedLibrary != null && biomeTextureIndex < s_cachedLibrary.BiomeCount)
            {
                BiomeDefinition biome = s_cachedLibrary.GetBiome(biomeTextureIndex);
                tint = new Color(biome.DebugColor.r, biome.DebugColor.g, biome.DebugColor.b, 0.9f);
                return plan.BiomeWeightTiles[biomeTextureIndex];
            }

            return null;
        }

        private static RegionPlanner GetPlanner()
        {
            if (WorldManager.Instance != null)
            {
                return WorldManager.Instance.GetComponent<RegionPlanner>();
            }

#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<RegionPlanner>();
#else
            return Object.FindObjectOfType<RegionPlanner>();
#endif
        }

        private static void RefreshOptions(RegionPlanner planner = null)
        {
            planner ??= GetPlanner();
            ClimateProvider provider = planner != null ? planner.ClimateProvider : null;
            BiomeLibrary library = provider != null ? provider.BiomeLibrary : null;
            int currentCount = library != null ? library.BiomeCount : 0;

            if (library == s_cachedLibrary && s_cachedBiomeCount == currentCount && s_options.Count > 0)
            {
                return;
            }

            s_cachedLibrary = library;
            s_cachedBiomeCount = currentCount;
            s_options.Clear();
            s_options.Add(new GUIContent("Temperature"));
            s_options.Add(new GUIContent("Moisture"));

            if (library != null)
            {
                for (int i = 0; i < library.BiomeCount; ++i)
                {
                    BiomeDefinition biome = library.GetBiome(i);
                    string name = string.IsNullOrEmpty(biome.Id) ? $"Biome {i}" : biome.Id;
                    s_options.Add(new GUIContent($"Weight - {name}"));
                }
            }

            s_selectedIndex = math.min(s_selectedIndex, math.max(0, s_options.Count - 1));
        }
    }
}
#endif
