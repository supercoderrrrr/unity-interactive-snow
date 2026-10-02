using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public sealed class PlayerControl : MonoBehaviour
{
    private static readonly int VerticalParameter =
        Animator.StringToHash("Vertical");

    private static readonly int HorizontalParameter =
        Animator.StringToHash("Horizontal");

    private static readonly int WalkSpeedParameter =
        Animator.StringToHash("WalkSpeed");

    [Header("Player Movement")]
    [SerializeField, Min(0f)]
    private float walkSpeed = 3f;

    [SerializeField, Min(0f)]
    private float runSpeed = 5f;

    [Tooltip("Mouse movement multiplier used by the orbit camera.")]
    [SerializeField, Range(0.1f, 2f)]
    private float rotateScale = 0.5f;

    [Tooltip("How quickly the character turns toward its movement direction.")]
    [SerializeField, Min(0f)]
    private float turnSpeed = 12f;

    [SerializeField]
    private float gravity = -20f;

    [Header("Orbit Camera")]
    [Tooltip("The gameplay camera. If empty, a child Camera or Camera.main is used.")]
    [SerializeField]
    private Transform cameraTransform;

    [Tooltip("World-space height of the camera pivot above the player root.")]
    [SerializeField]
    private float cameraPivotHeight = 2f;

    [SerializeField]
    private float minimumCameraPitch = -20f;

    [SerializeField]
    private float maximumCameraPitch = 70f;

    [SerializeField, Min(0.1f)]
    private float minimumCameraDistance = 3f;

    [SerializeField, Min(0.1f)]
    private float maximumCameraDistance = 25f;

    [SerializeField, Min(0f)]
    private float cameraZoomSpeed = 2f;

    [Tooltip("Higher values make the camera follow the player more quickly.")]
    [SerializeField, Min(0f)]
    private float cameraFollowSharpness = 20f;


    [Header("Animation")]
    [SerializeField, Min(0f)]
    private float walkAnimationSpeed = 2.2f;

    [SerializeField, Min(0f)]
    private float runAnimationSpeed = 1f;

    [SerializeField, Min(0f)]
    private float animationDampTime = 0.1f;

    [Header("Snow Field")]
    [Tooltip("The snow object must have a Renderer and a MeshCollider.")]
    [SerializeField]
    private Transform snowField;

    [Header("Track Texture")]
    [Tooltip("The circular brush texture drawn underneath both feet.")]
    [SerializeField]
    private Texture2D stampTexture;

    [Tooltip("The texture property used by the snow shader.")]
    [SerializeField]
    private string trackTextureProperty = "_TrackTexture";

    [Tooltip("The width and height of the runtime track Render Texture.")]
    [SerializeField, Min(32)]
    private int rtWidthAndHeight = 1024;

    [Tooltip("Scale multiplier applied to the circular stamp texture.")]
    [SerializeField, Range(0.01f, 10f)]
    private float sizeScale = 0.1f;

    [Header("Continuous Track")]
    [Tooltip("Optional track center. Leave empty to use the player root position.")]
    [SerializeField]
    private Transform trackOrigin;

    [Tooltip("Maximum world-space distance between two consecutive stamps. Smaller values create a smoother track.")]
    [SerializeField, Min(0.001f)]
    private float trackInterpolation = 0.1f;

    [Tooltip("Movement shorter than this distance will not produce a new track stamp.")]
    [SerializeField, Min(0f)]
    private float minimumTrackMovement = 0.01f;

    [Tooltip("Safety limit for stamps generated during one frame, for example after teleporting.")]
    [SerializeField, Min(1)]
    private int maxStampsPerFrame = 128;

    [Tooltip("Maximum distance used when detecting the snow surface.")]
    [SerializeField, Min(0.1f)]
    private float raycastDistance = 10f;

    [Tooltip("Height added above the track center before casting the ray downward.")]
    [SerializeField, Min(0f)]
    private float rayOriginHeight = 0.5f;

    [Header("Debug Preview")]
    [Tooltip("Optional RawImage used to display the runtime track texture.")]
    [SerializeField]
    private RawImage trackTexturePreview;

    private CharacterController characterController;
    private Animator animator;

    private Renderer snowRenderer;
    private MeshCollider snowCollider;
    private MaterialPropertyBlock propertyBlock;
    private RenderTexture trackRenderTexture;

    private float verticalVelocity;
    private Vector3 lastTrackPosition;
    private bool hasLastTrackPosition;

    private float cameraYaw;
    private float cameraPitch;
    private float cameraDistance;
    private Vector3 smoothedCameraPivot;
    private bool cameraSystemReady;

    private int trackTexturePropertyId;
    private bool snowSystemReady;

    private void Awake()
    {
        characterController =
            GetComponent<CharacterController>();

        animator =
            GetComponent<Animator>();

        // CharacterController owns movement so root motion stays disabled
        animator.applyRootMotion = false;

        if (cameraTransform == null)
        {
            Camera childCamera =
                GetComponentInChildren<Camera>(true);

            if (childCamera != null)
            {
                cameraTransform =
                    childCamera.transform;
            }
            else if (Camera.main != null)
            {
                cameraTransform =
                    Camera.main.transform;
            }
        }

        if (cameraTransform != null)
        {
            // Detach the camera so it can orbit independently
            cameraTransform.SetParent(null, true);
            cameraTransform.localScale = Vector3.one;
        }
    }

    private void Start()
    {
        cameraSystemReady =
            InitializeOrbitCamera();

        snowSystemReady =
            InitializeTrackSystem();

        if (snowSystemReady)
        {
            lastTrackPosition =
                GetTrackWorldPosition();

            hasLastTrackPosition = true;
            DrawAtWorldPosition(lastTrackPosition);
        }
    }

    private void Update()
    {
        UpdateOrbitCameraInput();
        MovePlayer();
        DrawContinuousTrack();
    }

    private void LateUpdate()
    {
        UpdateOrbitCameraPosition();
    }

    private bool InitializeOrbitCamera()
    {
        if (cameraTransform == null)
        {
            Debug.LogWarning(
                "PlayerControl: No gameplay camera was found. " +
                "Movement will use the player's orientation.",
                this);

            return false;
        }

        cameraYaw =
            cameraTransform.eulerAngles.y;

        cameraPitch =
            NormalizeAngle(
                cameraTransform.eulerAngles.x);

        cameraPitch =
            Mathf.Clamp(
                cameraPitch,
                minimumCameraPitch,
                maximumCameraPitch);

        Vector3 pivot =
            GetCameraPivot();

        cameraDistance =
            Vector3.Distance(
                cameraTransform.position,
                pivot);

        cameraDistance =
            Mathf.Clamp(
                cameraDistance,
                minimumCameraDistance,
                Mathf.Max(
                    minimumCameraDistance,
                    maximumCameraDistance));

        smoothedCameraPivot = pivot;
        UpdateOrbitCameraPosition(true);

        return true;
    }

    private void UpdateOrbitCameraInput()
    {
        if (!cameraSystemReady)
        {
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState =
                CursorLockMode.Locked;

            Cursor.visible = false;
        }

        if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState =
                CursorLockMode.None;

            Cursor.visible = true;
        }

        if (Input.GetMouseButton(1))
        {
            cameraYaw +=
                Input.GetAxis("Mouse X") *
                rotateScale *
                10f;

            cameraPitch -=
                Input.GetAxis("Mouse Y") *
                rotateScale *
                10f;

            cameraPitch =
                Mathf.Clamp(
                    cameraPitch,
                    minimumCameraPitch,
                    maximumCameraPitch);
        }

        float scrollInput =
            Input.mouseScrollDelta.y;

        if (Mathf.Abs(scrollInput) > 0.001f)
        {
            cameraDistance -=
                scrollInput * cameraZoomSpeed;

            cameraDistance =
                Mathf.Clamp(
                    cameraDistance,
                    minimumCameraDistance,
                    Mathf.Max(
                        minimumCameraDistance,
                        maximumCameraDistance));
        }
    }

    private void UpdateOrbitCameraPosition(
        bool snapImmediately = false)
    {
        if (cameraTransform == null)
        {
            return;
        }

        Vector3 targetPivot =
            GetCameraPivot();

        if (snapImmediately)
        {
            smoothedCameraPivot =
                targetPivot;
        }
        else
        {
            float followAmount =
                1f - Mathf.Exp(
                    -cameraFollowSharpness *
                    Time.deltaTime);

            smoothedCameraPivot =
                Vector3.Lerp(
                    smoothedCameraPivot,
                    targetPivot,
                    followAmount);
        }

        Quaternion orbitRotation =
            Quaternion.Euler(
                cameraPitch,
                cameraYaw,
                0f);

        cameraTransform.position =
            smoothedCameraPivot +
            orbitRotation *
            (Vector3.back * cameraDistance);

        cameraTransform.rotation =
            orbitRotation;
    }

    private Vector3 GetCameraPivot()
    {
        return transform.position +
               Vector3.up * cameraPivotHeight;
    }

    private static float NormalizeAngle(
        float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }

    private void OnDisable()
    {
        Cursor.lockState =
            CursorLockMode.None;

        Cursor.visible = true;
    }

    private void MovePlayer()
    {
        float horizontalInput =
            Input.GetAxisRaw("Horizontal");

        float verticalInput =
            Input.GetAxisRaw("Vertical");

        Vector2 input = Vector2.ClampMagnitude(
            new Vector2(
                horizontalInput,
                verticalInput),
            1f);

        bool hasMovement =
            input.sqrMagnitude > 0.0001f;

        bool isRunning =
            hasMovement &&
            Input.GetKey(KeyCode.LeftShift);

        float currentSpeed =
            isRunning
                ? runSpeed
                : walkSpeed;

        Vector3 referenceForward =
            transform.forward;

        Vector3 referenceRight =
            transform.right;

        if (cameraSystemReady &&
            cameraTransform != null)
        {
            referenceForward =
                Vector3.ProjectOnPlane(
                    cameraTransform.forward,
                    Vector3.up).normalized;

            referenceRight =
                Vector3.ProjectOnPlane(
                    cameraTransform.right,
                    Vector3.up).normalized;
        }

        Vector3 moveDirection =
            referenceForward * input.y +
            referenceRight * input.x;

        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        if (hasMovement &&
            moveDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    moveDirection,
                    Vector3.up);

            float rotationAmount =
                1f - Mathf.Exp(
                    -turnSpeed *
                    Time.deltaTime);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    rotationAmount);
        }

        if (characterController.isGrounded &&
            verticalVelocity < 0f)
        {
            // Keep the controller attached to slopes and uneven surfaces
            verticalVelocity = -2f;
        }

        verticalVelocity +=
            gravity * Time.deltaTime;

        Vector3 movement =
            moveDirection * currentSpeed;

        movement.y = verticalVelocity;

        characterController.Move(
            movement * Time.deltaTime);

        UpdateAnimator(
            input.magnitude,
            isRunning);
    }

    private void UpdateAnimator(
        float movementAmount,
        bool isRunning)
    {
        float animationVertical =
            movementAmount;

        if (isRunning)
        {
            // The character blend tree uses 2 for forward running
            animationVertical *= 2f;
        }

        float animationSpeed =
            isRunning
                ? runAnimationSpeed
                : walkAnimationSpeed;

        animator.SetFloat(
            VerticalParameter,
            animationVertical,
            animationDampTime,
            Time.deltaTime);

        animator.SetFloat(
            HorizontalParameter,
            0f,
            animationDampTime,
            Time.deltaTime);

        animator.SetFloat(
            WalkSpeedParameter,
            animationSpeed);
    }

    private void DrawContinuousTrack()
    {
        if (!snowSystemReady)
        {
            return;
        }

        Vector3 currentTrackPosition =
            GetTrackWorldPosition();

        if (!hasLastTrackPosition)
        {
            lastTrackPosition = currentTrackPosition;
            hasLastTrackPosition = true;
            DrawAtWorldPosition(currentTrackPosition);
            return;
        }

        Vector2 previousHorizontalPosition =
            new Vector2(
                lastTrackPosition.x,
                lastTrackPosition.z);

        Vector2 currentHorizontalPosition =
            new Vector2(
                currentTrackPosition.x,
                currentTrackPosition.z);

        float movementDistance = Vector2.Distance(
                previousHorizontalPosition,
                currentHorizontalPosition);

        if (movementDistance < minimumTrackMovement)
        {
            return;
        }

        float interpolationDistance =
            Mathf.Max(0.001f, trackInterpolation);

        int stampCount = Mathf.CeilToInt(
                movementDistance/
                interpolationDistance);

        stampCount = Mathf.Clamp(
                stampCount,
                1,
                Mathf.Max(1, maxStampsPerFrame));

        for (int index = 1; index <= stampCount; index++)
        {
            float interpolation = index/(float)stampCount;

            Vector3 interpolatedPosition = Vector3.Lerp(
                    lastTrackPosition,
                    currentTrackPosition,
                    interpolation);

            DrawAtWorldPosition(interpolatedPosition);
        }

        lastTrackPosition =
            currentTrackPosition;
    }

    private Vector3 GetTrackWorldPosition()
    {
        return trackOrigin != null
            ? trackOrigin.position
            : transform.position;
    }

    private bool InitializeTrackSystem()
    {
        if (snowField == null)
        {
            Debug.LogError(
                "PlayerControl: Snow Field has not been assigned.",
                this);

            return false;
        }

        if (stampTexture == null)
        {
            Debug.LogError(
                "PlayerControl: Stamp Texture has not been assigned.",
                this);

            return false;
        }

        snowRenderer =
            snowField.GetComponentInChildren<Renderer>();

        snowCollider =
            snowField.GetComponentInChildren<MeshCollider>();

        if (snowRenderer == null)
        {
            Debug.LogError(
                "PlayerControl: No Renderer was found on the Snow Field.",
                snowField);

            return false;
        }

        if (snowRenderer.sharedMaterial == null)
        {
            Debug.LogError(
                "PlayerControl: The Snow Field Renderer has no material.",
                snowRenderer);

            return false;
        }

        if (snowCollider == null)
        {
            Debug.LogError(
                "PlayerControl: No MeshCollider was found on the Snow Field. " +
                "A MeshCollider is required to obtain texture coordinates.",
                snowField);

            return false;
        }

        if (string.IsNullOrWhiteSpace(
                trackTextureProperty))
        {
            Debug.LogError(
                "PlayerControl: Track Texture Property cannot be empty.",
                this);

            return false;
        }

        if (!snowRenderer.sharedMaterial.HasProperty(
                trackTextureProperty))
        {
            Debug.LogError(
                $"PlayerControl: The snow material does not contain " +
                $"the shader property '{trackTextureProperty}'.",
                snowRenderer);

            return false;
        }

        int textureSize =
            Mathf.Max(
                32,
                rtWidthAndHeight);

        trackRenderTexture =
            new RenderTexture(
                textureSize,
                textureSize,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Runtime Foot Track Render Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };

        trackRenderTexture.Create();

        ClearRenderTexture(
            trackRenderTexture,
            Color.black);

        trackTexturePropertyId =
            Shader.PropertyToID(
                trackTextureProperty);

        propertyBlock =
            new MaterialPropertyBlock();

        snowRenderer.GetPropertyBlock(
            propertyBlock);

        propertyBlock.SetTexture(
            trackTexturePropertyId,
            trackRenderTexture);

        snowRenderer.SetPropertyBlock(
            propertyBlock);

        if (trackTexturePreview != null)
        {
            trackTexturePreview.texture =
                trackRenderTexture;
        }

        return true;
    }

    private bool DrawAtWorldPosition(
        Vector3 worldPosition)
    {
        if (trackRenderTexture == null ||
            snowCollider == null)
        {
            return false;
        }

        Vector3 rayOrigin =
            worldPosition +
            Vector3.up * rayOriginHeight;

        Ray ray =
            new Ray(
                rayOrigin,
                Vector3.down);

        float totalRayDistance =
            raycastDistance +
            rayOriginHeight;

        if (!snowCollider.Raycast(ray,out RaycastHit hit,totalRayDistance))
        {
            return false;
        }

        Vector2 uv = hit.textureCoord;

        int centerX = Mathf.RoundToInt(
                uv.x * (trackRenderTexture.width - 1));

        int centerY = Mathf.RoundToInt(
                (1f - uv.y) * (trackRenderTexture.height - 1));

        DrawTextureToRenderTexture(
            stampTexture,
            sizeScale,
            centerX,
            centerY,
            trackRenderTexture);

        return true;
    }

    private static void DrawTextureToRenderTexture(
        Texture2D source,
        float sourceScale,
        int centerXOnDestination,
        int centerYOnDestination,
        RenderTexture destination)
    {
        if (source == null ||
            destination == null ||
            sourceScale <= 0f)
        {
            return;
        }

        int scaledWidth =
            Mathf.Max(
                1,
                Mathf.RoundToInt(
                    source.width *
                    sourceScale));

        int scaledHeight =
            Mathf.Max(
                1,
                Mathf.RoundToInt(
                    source.height *
                    sourceScale));

        float destinationX =
            centerXOnDestination -
            scaledWidth * 0.5f;

        float destinationY =
            centerYOnDestination -
            scaledHeight * 0.5f;

        Rect destinationRect =
            new Rect(
                destinationX,
                destinationY,
                scaledWidth,
                scaledHeight);

        RenderTexture previousRenderTexture =
            RenderTexture.active;

        RenderTexture.active =
            destination;

        GL.PushMatrix();

        GL.LoadPixelMatrix(
            0f,
            destination.width,
            destination.height,
            0f);

        // The GPU clips regions outside the render texture
        Graphics.DrawTexture(
            destinationRect,
            source);

        GL.PopMatrix();

        RenderTexture.active =
            previousRenderTexture;
    }

    private static void ClearRenderTexture(
        RenderTexture renderTexture,
        Color clearColor)
    {
        RenderTexture previousRenderTexture =
            RenderTexture.active;

        RenderTexture.active =
            renderTexture;

        GL.Clear(
            false,
            true,
            clearColor);

        RenderTexture.active =
            previousRenderTexture;
    }

    private void OnDestroy()
    {
        if (trackTexturePreview != null &&
            trackTexturePreview.texture ==
            trackRenderTexture)
        {
            trackTexturePreview.texture = null;
        }

        if (trackRenderTexture == null)
        {
            return;
        }

        trackRenderTexture.Release();
        Destroy(trackRenderTexture);
        trackRenderTexture = null;
    }
}
