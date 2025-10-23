using System.Collections.Generic;
using Tuntenfisch.UI;
using Tuntenfisch.Voxels.CSG;
using Tuntenfisch.Voxels.Materials;
using Tuntenfisch.Voxels.Volume;
using Tuntenfisch.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tuntenfisch.Player
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public class PlayerController : MonoBehaviour
    {
        private enum VoxelToolMode
        {
            Sculpt = 1,
            Selection = 2
        }

        private float Gravity => Physics.gravity.y;

        private const float c_minDownwardVelocity = -2.0f;

        [Header("Movement")]
        [Min(1.0f)]
        [SerializeField]
        private float m_movementSpeed = 5.0f;
        [Min(1.0f)]
        [SerializeField]
        private float m_jumpHeight = 1.5f;

        [Header("Look")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float m_lookSensitivity = 0.05f;
        [SerializeField]
        private Camera m_camera;

        [Header("Flight")]
        [Min(1.0f)]
        [SerializeField]
        private float m_flySpeed = 10.0f;
        [SerializeField]
        [Min(0.1f)]
        private float m_flySpeedStep = 2.0f;
        [SerializeField]
        [Min(0.1f)]
        private float m_minFlySpeed = 1.0f;
        [SerializeField]
        [Min(0.1f)]
        private float m_maxFlySpeed = 100.0f;

        [Header("Voxel Tools")]
        [Min(1)]
        [SerializeField]
        private int m_maxSelectionSize = 16;

        private CharacterController m_controller;
        private int m_playerLayerMask;

        private float2 m_moveDelta;
        private bool m_wantsToJump;
        private float2 m_lookDelta;
        private float2 m_rotation;
        private float3 m_velocity;
        private bool m_primaryDown;
        private bool m_secondaryDown;
        private bool m_flyModeEnabled;
        private bool m_flyUpPressed;
        private bool m_flyDownPressed;
        private bool m_flyUpActionHeld;
        private bool m_flyDownActionHeld;

        private bool m_primaryPressedThisFrame;
        private bool m_primaryReleasedThisFrame;
        private bool m_prevPrimaryDown;
        private bool m_prevSecondaryDown;

        private VoxelToolMode m_activeVoxelMode = VoxelToolMode.Sculpt;
        private VoxelSelectionHologram m_selectionHologram;
        private readonly VoxelClipboard m_clipboard = new VoxelClipboard();
        private bool m_selectionActive;
        private int m_selectionSize = 1;
        private int3 m_selectionMinIndex;
        private bool m_isDraggingSelection;
        private float3 m_selectionOffsetFromPlayer;
        private float m_voxelSpacing = 1.0f;

        private void Start()
        {
            m_controller = GetComponent<CharacterController>();
            m_playerLayerMask = LayerMask.GetMask("Player");
            Cursor.lockState = CursorLockMode.Locked;

            if (WorldManager.VoxelConfig != null)
            {
                m_voxelSpacing = WorldManager.VoxelConfig.VoxelVolumeConfig.VoxelSpacing;
            }

            InitializeSelectionHologram();
            UpdateModeUI();
            SetFlyMode(true);
        }

        private void OnDestroy()
        {
            if (m_selectionHologram != null)
            {
                Destroy(m_selectionHologram.gameObject);
                m_selectionHologram = null;
            }
        }

        private void Update()
        {
            UpdateInputState();
            UpdateFlightControls();
            HandleModeHotkeys();
            ApplyMovement();
            ApplyLook();
            HandleWorldInteraction();
            UpdateSelectionHologram();
        }

        public void OnMove(InputValue value) => m_moveDelta = value.Get<Vector2>();

        public void OnJump() => m_wantsToJump = m_controller.isGrounded;

        public void OnLook(InputValue value) => m_lookDelta = value.Get<Vector2>();

        public void OnPrimary(InputValue value) => m_primaryDown = value.isPressed;

        public void OnSecondary(InputValue value) => m_secondaryDown = value.isPressed;

        public void OnFlyMode(InputValue value)
        {
            if (value.isPressed)
            {
                SetFlyMode(!m_flyModeEnabled);
            }
        }

        public void OnFlyUp(InputValue value) => m_flyUpActionHeld = value.isPressed;

        public void OnFlyDown(InputValue value) => m_flyDownActionHeld = value.isPressed;

        private void SetFlyMode(bool enabled)
        {
            if (m_flyModeEnabled == enabled)
            {
                return;
            }

            m_flyModeEnabled = enabled;
            m_flyUpPressed = false;
            m_flyDownPressed = false;
            m_flyUpActionHeld = false;
            m_flyDownActionHeld = false;
            m_wantsToJump = false;
            m_velocity = 0.0f;
            m_isDraggingSelection = false;
        }

        private void UpdateInputState()
        {
            m_primaryPressedThisFrame = m_primaryDown && !m_prevPrimaryDown;
            m_primaryReleasedThisFrame = !m_primaryDown && m_prevPrimaryDown;
            m_prevPrimaryDown = m_primaryDown;
            m_prevSecondaryDown = m_secondaryDown;
        }

        private void UpdateFlightControls()
        {
            if (!m_flyModeEnabled)
            {
                m_flyUpPressed = false;
                m_flyDownPressed = false;
                return;
            }

            bool flyUp = m_flyUpActionHeld;
            bool flyDown = m_flyDownActionHeld;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                flyUp = keyboard.spaceKey.isPressed;
                flyDown = keyboard.leftShiftKey.isPressed ||
                          keyboard.rightShiftKey.isPressed ||
                          keyboard.leftCtrlKey.isPressed ||
                          keyboard.rightCtrlKey.isPressed;
            }

            m_flyUpPressed = flyUp;
            m_flyDownPressed = flyDown;

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) <= 0.01f)
            {
                return;
            }

            float minSpeed = Mathf.Max(0.1f, m_minFlySpeed);
            float maxSpeed = Mathf.Max(minSpeed, m_maxFlySpeed);
            float speedStep = Mathf.Max(0.1f, m_flySpeedStep);

            m_flySpeed = Mathf.Clamp(m_flySpeed, minSpeed, maxSpeed);

            float speedDelta = scroll > 0.0f ? speedStep : -speedStep;
            m_flySpeed = Mathf.Clamp(m_flySpeed + speedDelta, minSpeed, maxSpeed);
        }

        private void HandleModeHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                SetVoxelMode(VoxelToolMode.Sculpt);
            }
            else if (keyboard.digit2Key.wasPressedThisFrame)
            {
                SetVoxelMode(VoxelToolMode.Selection);
            }

            if (m_activeVoxelMode != VoxelToolMode.Selection)
            {
                return;
            }

            float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0.0f;
            if (Mathf.Abs(scroll) > 0.01f && m_selectionActive)
            {
                int delta = scroll > 0.0f ? 1 : -1;
                AdjustSelectionSize(delta);
            }

            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (!ctrl)
            {
                return;
            }

            if (keyboard.cKey.wasPressedThisFrame)
            {
                CopySelection(false);
            }
            else if (keyboard.xKey.wasPressedThisFrame)
            {
                CopySelection(true);
            }
            else if (keyboard.vKey.wasPressedThisFrame)
            {
                PasteSelection();
            }
        }

        private void ApplyMovement()
        {
            static float3 FlattenHorizontal(Vector3 direction, Vector3 fallback, float3 worldFallback)
            {
                const float epsilon = 1e-5f;

                float3 horizontal = new float3(direction.x, 0.0f, direction.z);
                if (math.lengthsq(horizontal) > epsilon)
                {
                    return math.normalize(horizontal);
                }

                float3 fallbackHorizontal = new float3(fallback.x, 0.0f, fallback.z);
                if (math.lengthsq(fallbackHorizontal) > epsilon)
                {
                    return math.normalize(fallbackHorizontal);
                }

                return worldFallback;
            }

            if (m_flyModeEnabled)
            {
                float3 camRight = FlattenHorizontal(m_camera.transform.right, transform.right, new float3(1.0f, 0.0f, 0.0f));
                float3 camForward = FlattenHorizontal(m_camera.transform.forward, transform.forward, new float3(0.0f, 0.0f, 1.0f));
                float3 moveDirection = camRight * m_moveDelta.x + camForward * m_moveDelta.y;

                if (m_flyUpPressed)
                {
                    moveDirection += new float3(0.0f, 1.0f, 0.0f);
                }

                if (m_flyDownPressed)
                {
                    moveDirection += new float3(0.0f, -1.0f, 0.0f);
                }

                if (math.lengthsq(moveDirection) > 0.0f)
                {
                    moveDirection = math.normalize(moveDirection);
                }

                m_controller.Move((Vector3)(moveDirection * m_flySpeed * Time.deltaTime));
                m_velocity = 0.0f;
                return;
            }

            if (m_controller.isGrounded)
            {
                m_velocity.y = m_wantsToJump ? math.sqrt(-2.0f * Gravity * m_jumpHeight) : c_minDownwardVelocity;
                m_wantsToJump = false;
            }
            else
            {
                m_velocity.y += Gravity * Time.deltaTime;
            }

            m_velocity.xz = (((float3)transform.right).xz * m_moveDelta.x + ((float3)transform.forward).xz * m_moveDelta.y) * m_movementSpeed;
            m_controller.Move(m_velocity * Time.deltaTime);
        }

        private void ApplyLook()
        {
            m_rotation.y += m_lookDelta.x * m_lookSensitivity;
            m_rotation.x -= m_lookDelta.y * m_lookSensitivity;
            m_rotation.x = math.clamp(m_rotation.x, -90.0f, 90.0f);

            m_camera.transform.localRotation = Quaternion.Euler(m_rotation.x, 0.0f, 0.0f);
            transform.localRotation = Quaternion.Euler(0.0f, m_rotation.y, 0.0f);

            m_lookDelta = 0.0f;
        }

        private void HandleWorldInteraction()
        {
            Ray ray = new Ray(m_camera.transform.position, m_camera.transform.forward);

            if (m_activeVoxelMode == VoxelToolMode.Sculpt)
            {
                HandleSculptInteraction(ray);
            }
            else
            {
                HandleSelectionInteraction(ray);
            }
        }

        private void HandleSculptInteraction(Ray ray)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~m_playerLayerMask))
            {
                GPUCSGPrimitive primitive = new GPUCSGPrimitive(CSGPrimitiveType.Sphere);
                float3 scale = 4.0f;

                WorldManager.Instance.DrawCSGPrimitiveHologram(primitive.PrimitiveType, hit.point, scale);

                if (m_primaryDown)
                {
                    WorldManager.Instance.ApplyCSGOperation(new GPUCSGOperator(CSGOperatorIndex.Union), primitive, MaterialIndex.Dirt, hit.point, scale);
                }

                if (m_secondaryDown)
                {
                    WorldManager.Instance.ApplyCSGOperation(new GPUCSGOperator(CSGOperatorIndex.Difference), primitive, default, hit.point, scale);
                }
            }
        }

        private void HandleSelectionInteraction(Ray ray)
        {
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~m_playerLayerMask);

            if (hasHit && m_primaryPressedThisFrame)
            {
                float3 center = QuantizeWorldToVoxelCenter(hit.point);
                PlaceSelectionAtWorld(center);
                m_isDraggingSelection = true;
                m_selectionOffsetFromPlayer = GetSelectionCenterWorld() - (float3)transform.position;
            }

            if (m_primaryReleasedThisFrame)
            {
                m_isDraggingSelection = false;
            }

            if (m_isDraggingSelection && m_selectionActive)
            {
                UpdateDraggedSelection();
            }
        }

        private void SetVoxelMode(VoxelToolMode mode)
        {
            if (m_activeVoxelMode == mode)
            {
                return;
            }

            m_activeVoxelMode = mode;
            if (mode != VoxelToolMode.Selection)
            {
                m_isDraggingSelection = false;
            }

            UpdateModeUI();
        }

        private void UpdateModeUI()
        {
            string label = m_activeVoxelMode switch
            {
                VoxelToolMode.Selection => "2 - Selection",
                _ => "1 - Sculpt"
            };

            VoxelToolHUD hud = VoxelToolHUD.GetOrCreate();
            hud?.SetModeLabel(label);
        }

        private void InitializeSelectionHologram()
        {
            GameObject hologramGO = new GameObject("Voxel Selection Hologram");
            hologramGO.hideFlags = HideFlags.HideInHierarchy;
            m_selectionHologram = hologramGO.AddComponent<VoxelSelectionHologram>();
            m_selectionHologram.SetVisible(false);
        }

        private void UpdateSelectionHologram()
        {
            if (m_selectionHologram == null)
            {
                return;
            }

            if (m_activeVoxelMode != VoxelToolMode.Selection || !m_selectionActive)
            {
                m_selectionHologram.SetVisible(false);
                return;
            }

            Vector3 center = ToVector3(GetSelectionCenterWorld());
            m_selectionHologram.UpdateHologram(center, m_voxelSpacing, m_selectionSize);
            m_selectionHologram.SetVisible(true);
        }

        private void AdjustSelectionSize(int delta)
        {
            int newSize = Mathf.Clamp(m_selectionSize + delta, 1, Mathf.Max(1, m_maxSelectionSize));
            if (newSize == m_selectionSize)
            {
                return;
            }

            float3 center = GetSelectionCenterWorld();
            m_selectionSize = newSize;
            SetSelectionCenterWorld(center);
        }

        private void PlaceSelectionAtWorld(float3 worldCenter)
        {
            m_selectionActive = true;
            SetSelectionCenterWorld(worldCenter);
        }

        private void UpdateDraggedSelection()
        {
            float3 desiredCenter = (float3)transform.position + m_selectionOffsetFromPlayer;
            SetSelectionCenterWorld(desiredCenter);
        }

        private float3 GetSelectionCenterWorld()
        {
            float half = m_selectionSize * 0.5f - 0.5f;
            float3 centerIndex = (float3)m_selectionMinIndex + new float3(half);
            return centerIndex * m_voxelSpacing;
        }

        private void SetSelectionCenterWorld(float3 worldCenter)
        {
            float half = m_selectionSize * 0.5f - 0.5f;
            float3 minIndex = worldCenter / m_voxelSpacing - new float3(half);
            m_selectionMinIndex = (int3)math.round(minIndex);
        }

        private float3 QuantizeWorldToVoxelCenter(float3 worldPosition)
        {
            return math.round(worldPosition / m_voxelSpacing) * m_voxelSpacing;
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private void CopySelection(bool cut)
        {
            if (!m_selectionActive)
            {
                return;
            }

            m_clipboard.BeginRecord(m_selectionSize);
            bool copiedAny = false;

            for (int x = 0; x < m_selectionSize; x++)
            {
                for (int y = 0; y < m_selectionSize; y++)
                {
                    for (int z = 0; z < m_selectionSize; z++)
                    {
                        int3 offset = new int3(x, y, z);
                        int3 globalIndex = m_selectionMinIndex + offset;

                        if (!TryGetChunkForIndex(globalIndex, out Chunk chunk, out int3 localCoordinate))
                        {
                            continue;
                        }

                        if (!chunk.TryGetVoxel(localCoordinate, out PackedVoxel voxel))
                        {
                            continue;
                        }

                        copiedAny = true;
                        m_clipboard.Add(offset, voxel);

                        if (cut)
                        {
                            chunk.SetVoxelToAir(localCoordinate);
                        }
                    }
                }
            }

            if (!copiedAny)
            {
                m_clipboard.Clear();
            }
        }

        private void PasteSelection()
        {
            if (!m_selectionActive || !m_clipboard.HasEntries)
            {
                return;
            }

            foreach (VoxelClipboard.Entry entry in m_clipboard.Entries)
            {
                int3 targetIndex = m_selectionMinIndex + entry.Offset;
                if (!TryGetChunkForIndex(targetIndex, out Chunk chunk, out int3 localCoordinate))
                {
                    continue;
                }

                chunk.TrySetVoxel(localCoordinate, entry.Voxel);
            }
        }

        private bool TryGetChunkForIndex(int3 globalIndex, out Chunk chunk, out int3 localCoordinate)
        {
            float3 worldPosition = (float3)globalIndex * m_voxelSpacing;
            if (!WorldManager.TryGetChunkAtPosition(worldPosition, out chunk))
            {
                localCoordinate = default;
                return false;
            }

            localCoordinate = chunk.WorldToLocalVoxelCoordinate(worldPosition);
            return true;
        }

        private class VoxelClipboard
        {
            public struct Entry
            {
                public int3 Offset;
                public PackedVoxel Voxel;
            }

            private readonly List<Entry> m_entries = new List<Entry>();

            public bool HasEntries => m_entries.Count > 0;
            public IReadOnlyList<Entry> Entries => m_entries;

            public void BeginRecord(int size)
            {
                m_entries.Clear();
            }

            public void Add(int3 offset, PackedVoxel voxel)
            {
                m_entries.Add(new Entry { Offset = offset, Voxel = voxel });
            }

            public void Clear()
            {
                m_entries.Clear();
            }
        }
    }
}
