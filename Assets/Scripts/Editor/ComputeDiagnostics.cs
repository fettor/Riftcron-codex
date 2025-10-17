#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tuntenfisch.Editor
{
    [InitializeOnLoad]
    internal static class ComputeDiagnostics
    {
        static ComputeDiagnostics()
        {
            string[] guids = AssetDatabase.FindAssets("t:ComputeShader");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

                if (shader == null)
                {
                    continue;
                }

                foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader))
                {
                    Debug.LogError($"Compute shader error in '{path}' (line {message.line}): {message.message}", shader);
                }
            }
        }
    }
}
#endif
