using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class PlayerMovementController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.75f;
    [SerializeField, Min(0f)] private float acceleration = 30f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.25f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
    [SerializeField, Range(0f, 89f)] private float maximumGroundAngle = 55f;
    [SerializeField, Min(0f)] private float groundedGraceTime = 0.12f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform trackEmitter;

    private Rigidbody body;
    private SphereCollider sphereCollider;
    private Vector2 moveInput;
    private bool sprintHeld;
    private bool jumpQueued;
    private float lastGroundedTime = float.NegativeInfinity;

    private bool IsGrounded => Time.time - lastGroundedTime <= groundedGraceTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();
        body.maxAngularVelocity = 30f;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (trackEmitter == null)
        {
            ParticleSystem particles = GetComponentInChildren<ParticleSystem>(true);
            trackEmitter = particles != null ? particles.transform : null;
        }
    }

    private void Update()
    {
        moveInput = Vector2.ClampMagnitude(
            new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
            1f);
        sprintHeld = Input.GetKey(KeyCode.LeftShift);
        jumpQueued |= Input.GetButtonDown("Jump");
    }

    private void FixedUpdate()
    {
        Vector3 moveDirection = GetCameraRelativeDirection(moveInput);
        float targetSpeed = moveSpeed * (sprintHeld ? sprintMultiplier : 1f);
        Vector3 targetPlanarVelocity = moveDirection * targetSpeed;
        Vector3 currentPlanarVelocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        Vector3 requiredAcceleration =
            (targetPlanarVelocity - currentPlanarVelocity) / Time.fixedDeltaTime;

        float control = IsGrounded ? 1f : airControl;
        requiredAcceleration = Vector3.ClampMagnitude(requiredAcceleration, acceleration * control);
        body.AddForce(requiredAcceleration, ForceMode.Acceleration);

        if (jumpQueued && IsGrounded)
        {
            float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
            float verticalVelocityChange = Mathf.Max(0f, jumpVelocity - body.velocity.y);
            body.AddForce(Vector3.up * verticalVelocityChange, ForceMode.VelocityChange);
            lastGroundedTime = float.NegativeInfinity;
        }

        jumpQueued = false;
    }

    private void LateUpdate()
    {
        if (trackEmitter == null)
        {
            return;
        }

        float worldRadius = sphereCollider.bounds.extents.y;
        Vector3 emitterPosition = transform.position + Vector3.down * (worldRadius - 0.02f);
        trackEmitter.SetPositionAndRotation(emitterPosition, Quaternion.identity);
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        if (cameraTransform == null)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        RecordGroundContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        RecordGroundContact(collision);
    }

    private void RecordGroundContact(Collision collision)
    {
        float minimumGroundDot = Mathf.Cos(maximumGroundAngle * Mathf.Deg2Rad);
        for (int index = 0; index < collision.contactCount; index++)
        {
            if (Vector3.Dot(collision.GetContact(index).normal, Vector3.up) >= minimumGroundDot)
            {
                lastGroundedTime = Time.time;
                return;
            }
        }
    }
}
