using Tuntenfisch.Voxels.CSG;
using Tuntenfisch.Voxels.Materials;
using Tuntenfisch.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tuntenfisch.Player
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public class PlayerController : MonoBehaviour
    {
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

        private void Start()
        {
            m_controller = GetComponent<CharacterController>();
            m_playerLayerMask = LayerMask.GetMask("Player");
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update()
        {
            UpdateFlightControls();
            ApplyMovement();
            ApplyLook();
            HandleWorldInteraction();
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
                m_flyModeEnabled = !m_flyModeEnabled;
                m_flyUpPressed = false;
                m_flyDownPressed = false;
                m_flyUpActionHeld = false;
                m_flyDownActionHeld = false;
                m_wantsToJump = false;
                m_velocity = 0.0f;
            }
        }

        public void OnFlyUp(InputValue value) => m_flyUpActionHeld = value.isPressed;

        public void OnFlyDown(InputValue value) => m_flyDownActionHeld = value.isPressed;

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
    }
}
