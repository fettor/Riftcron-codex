#if UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tuntenfisch.World
{
    [RequireComponent(typeof(WorldManager))]
    public sealed class WorldChunkRegenerator : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Keyboard binding that triggers a full chunk regeneration while in Play Mode.")]
        [SerializeField]
        private Key m_regenerateKey = Key.F9;

        [Tooltip("Optional UI overlay shown briefly when regeneration is triggered.")]
        [SerializeField]
        private bool m_showOverlay = true;

        [Tooltip("Overlay text displayed in the top-right corner while regeneration is in progress.")]
        [SerializeField]
        private string m_overlayText = "Regenerating chunks...";

        [Tooltip("Seconds to display the overlay after regeneration finishes.")]
        [SerializeField, Min(0.0f)]
        private float m_overlayHoldDuration = 1.5f;

        private WorldManager m_worldManager;
        private bool m_isRegenerating;
        private float m_overlayTimer;

        private void Awake()
        {
            m_worldManager = GetComponent<WorldManager>();
        }

        private void Update()
        {
            if (!Application.isPlaying || m_worldManager == null)
            {
                return;
            }

            if (!m_isRegenerating && Keyboard.current != null && Keyboard.current[m_regenerateKey].wasPressedThisFrame)
            {
                StartCoroutine(RegenerateRoutine());
            }

            if (m_overlayTimer > 0.0f)
            {
                m_overlayTimer -= Time.deltaTime;
            }
        }

        private IEnumerator RegenerateRoutine()
        {
            m_isRegenerating = true;

            // Force a current frame yield to avoid input jitter before heavy work.
            yield return null;

            m_worldManager.RegenerateAllChunks();

            if (m_showOverlay)
            {
                m_overlayTimer = Mathf.Max(m_overlayHoldDuration, 0.1f);
            }

            m_isRegenerating = false;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !m_showOverlay || m_overlayTimer <= 0.0f)
            {
                return;
            }

            const float margin = 24.0f;
            const float width = 260.0f;
            const float height = 36.0f;
            Rect rect = new Rect(Screen.width - width - margin, margin, width, height);

            GUILayout.BeginArea(rect, GUIContent.none, GUI.skin.box);
            GUILayout.Label(m_overlayText, LocalGuiStyles.LabelAlignedRight);
            GUILayout.EndArea();
        }
    }

    internal static class LocalGuiStyles
    {
        private static GUIStyle s_labelRight;

        public static GUIStyle LabelAlignedRight
        {
            get
            {
                if (s_labelRight == null)
                {
                    s_labelRight = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleRight,
                        fontStyle = FontStyle.Bold
                    };
                }
                return s_labelRight;
            }
        }
    }
}
#endif
