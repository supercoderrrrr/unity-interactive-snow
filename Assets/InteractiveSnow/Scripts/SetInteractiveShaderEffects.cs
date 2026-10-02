using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class SetInteractiveShaderEffects : MonoBehaviour
{
    [SerializeField] private RenderTexture rt;
    [SerializeField] private Transform target;
    [SerializeField] private float yOffset = 20f;
    [SerializeField] private Camera trackingCamera;

    private static readonly int GlobalEffectRt = Shader.PropertyToID("_GlobalEffectRT");
    private static readonly int OrthographicCamSize = Shader.PropertyToID("_OrthographicCamSize");
    private static readonly int Position = Shader.PropertyToID("_Position");

    private void Awake()
    {
        if (trackingCamera == null)
        {
            trackingCamera = GetComponent<Camera>();
        }

        ApplyShaderGlobals();
    }

    private void OnEnable()
    {
        ApplyShaderGlobals();
    }

    private void Update()
    {
        if (trackingCamera == null || target == null || rt == null || rt.height <= 0)
        {
            return;
        }

        // Snap the tracking camera to render texture pixels for a stable trail
        float pixelSize = 2f * trackingCamera.orthographicSize / rt.height;
        Vector3 targetPosition = new Vector3(
            Mathf.Round(target.position.x / pixelSize) * pixelSize,
            Mathf.Round((target.position.y + yOffset) / pixelSize) * pixelSize,
            Mathf.Round(target.position.z / pixelSize) * pixelSize);

        transform.position = targetPosition;
        Shader.SetGlobalVector(Position, transform.position);
    }

    private void ApplyShaderGlobals()
    {
        if (trackingCamera == null)
        {
            trackingCamera = GetComponent<Camera>();
        }

        if (trackingCamera == null)
        {
            return;
        }

        Shader.SetGlobalTexture(GlobalEffectRt, rt);
        Shader.SetGlobalFloat(OrthographicCamSize, trackingCamera.orthographicSize);
        Shader.SetGlobalVector(Position, transform.position);
    }
}
