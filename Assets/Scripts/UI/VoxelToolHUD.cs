using UnityEngine;
using UnityEngine.UI;

#if TMP_PRESENT
using TMPro;
#endif

namespace Tuntenfisch.UI
{
    [ExecuteAlways]
    public class VoxelToolHUD : MonoBehaviour
    {
        private const string c_defaultLabel = "Voxel Mode: {0}";

        public static VoxelToolHUD Instance { get; private set; }

        [SerializeField]
        private Canvas m_canvas;
        [SerializeField]
        private RectTransform m_textRoot;
#if TMP_PRESENT
        [SerializeField]
        private TMP_Text m_text;
#else
        [SerializeField]
        private Text m_text;
#endif
        [SerializeField]
        private string m_format = c_defaultLabel;
        [SerializeField]
        private Vector2 m_anchor = new Vector2(0.5f, 0f);
        [SerializeField]
        private Vector2 m_position = new Vector2(0f, 40f);
        [SerializeField]
        private int m_fontSize = 20;
        [SerializeField]
        private Color m_textColor = Color.white;

        private void Awake()
        {
            Register();
            EnsureUI();
        }

        private void OnEnable()
        {
            Register();
            EnsureUI();
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static VoxelToolHUD GetOrCreate()
        {
            if (Instance != null)
            {
                return Instance;
            }

            GameObject go = new GameObject("VoxelToolHUD");
            Instance = go.AddComponent<VoxelToolHUD>();
            Instance.EnsureUI();
            return Instance;
        }

        public void SetModeLabel(string modeName)
        {
            EnsureUI();
            if (m_text != null)
            {
                m_text.text = string.Format(string.IsNullOrEmpty(m_format) ? c_defaultLabel : m_format, modeName);
            }
        }

        private void EnsureUI()
        {
            if (m_canvas == null)
            {
                GameObject canvasGO = new GameObject("VoxelToolCanvas");
                canvasGO.transform.SetParent(transform, false);

                m_canvas = canvasGO.AddComponent<Canvas>();
                m_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
                m_canvas.sortingOrder = 32766;
            }

            if (m_textRoot == null)
            {
                GameObject textRoot = new GameObject("VoxelToolTextRoot");
                m_textRoot = textRoot.AddComponent<RectTransform>();
                m_textRoot.SetParent(m_canvas.transform, false);
                m_textRoot.anchorMin = m_anchor;
                m_textRoot.anchorMax = m_anchor;
                m_textRoot.pivot = m_anchor;
                m_textRoot.anchoredPosition = m_position;
                m_textRoot.sizeDelta = new Vector2(400, 50);
            }

            if (m_text == null)
            {
                GameObject textGO = new GameObject("VoxelToolText");
                RectTransform rect = textGO.AddComponent<RectTransform>();
                rect.SetParent(m_textRoot, false);
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
#if TMP_PRESENT
                TMP_Text tmp = textGO.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = m_fontSize;
                tmp.alignment = TextAlignmentOptions.MiddleCenter;
                tmp.raycastTarget = false;
                tmp.color = m_textColor;
                tmp.enableWordWrapping = false;
                m_text = tmp;
#else
                Text textComponent = textGO.AddComponent<Text>();
                textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                textComponent.fontSize = m_fontSize;
                textComponent.alignment = TextAnchor.MiddleCenter;
                textComponent.raycastTarget = false;
                textComponent.color = m_textColor;
                m_text = textComponent;
#endif
            }
        }

        private void Register()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
                    DestroyImmediate(gameObject);
                }
            }
        }
    }
}
