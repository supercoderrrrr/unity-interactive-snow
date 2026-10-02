using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class InteractiveSnowSetup
{
    private const string TestScenePath = "Assets/Scenes/TestScene.unity";
    private const string MaterialsFolder = "Assets/InteractiveSnow/Materials";
    private const string RenderTexturePath = MaterialsFolder + "/SnowRT.renderTexture";
    private const string SnowMaterialPath = MaterialsFolder + "/InteractiveSnow.mat";
    private const string ParticleMaterialPath = MaterialsFolder + "/TrackParticle.mat";
    private const string TrackLayerName = "Track";

    static InteractiveSnowSetup()
    {
        EditorApplication.delayCall += BuildOnceWhenReady;
    }

    [MenuItem("Tools/Interactive Snow/Rebuild TestScene")]
    private static void RebuildFromMenu()
    {
        BuildTestScene(forceRebuild: true);
    }

    private static void BuildOnceWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += BuildOnceWhenReady;
            return;
        }

        if (SceneManager.GetActiveScene().path == TestScenePath)
        {
            BuildTestScene(forceRebuild: false);
        }
    }

    private static void BuildTestScene(bool forceRebuild)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != TestScenePath)
        {
            scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        }

        if (!forceRebuild && HasValidSetup(scene))
        {
            if (EnsureSpherePhysics(scene))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, TestScenePath);
            }

            return;
        }

        Shader snowShader = Shader.Find("Custom/Snow Interactive");
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (snowShader == null || particleShader == null)
        {
            Debug.LogError("Interactive Snow setup stopped: one or more tutorial shaders are not imported yet.");
            return;
        }

        int trackLayer = LayerMask.NameToLayer(TrackLayerName);
        if (trackLayer < 0)
        {
            Debug.LogError("Interactive Snow setup stopped: the existing 'Track' layer could not be found.");
            return;
        }

        if (forceRebuild)
        {
            RemoveOldTestObjects(scene);
        }

        RenderTexture renderTexture = CreateOrUpdateRenderTexture();
        Material snowMaterial = CreateOrUpdateSnowMaterial(snowShader);
        Material particleMaterial = CreateOrUpdateParticleMaterial(particleShader);

        GameObject interactiveSnowRoot = new GameObject("Interactive Snow");
        GameObject ground = CreateGround(interactiveSnowRoot.transform, snowMaterial);
        GameObject interactor = CreateInteractor(interactiveSnowRoot.transform, trackLayer, particleMaterial);
        CreateTrackingCamera(interactiveSnowRoot.transform, interactor.transform, trackLayer, renderTexture);

        Camera mainCamera = FindRootComponent<Camera>(scene, "Main Camera");
        if (mainCamera != null)
        {
            mainCamera.cullingMask &= ~(1 << trackLayer);
        }

        Selection.activeGameObject = interactor;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TestScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"Interactive Snow setup complete. Ground: {ground.name}, " +
            $"Interactor: {interactor.name}, RenderTexture: {RenderTexturePath}");
    }

    private static bool HasValidSetup(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            SetInteractiveShaderEffects effects = root.GetComponentInChildren<SetInteractiveShaderEffects>(true);
            if (effects != null &&
                effects.GetComponent<Camera>() != null &&
                effects.GetComponent<UniversalAdditionalCameraData>() != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool EnsureSpherePhysics(Scene scene)
    {
        PlayerMovementController movementController = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (PlayerMovementController candidate in
                     root.GetComponentsInChildren<PlayerMovementController>(true))
            {
                if (candidate.gameObject.activeInHierarchy)
                {
                    movementController = candidate;
                    break;
                }
            }

            if (movementController != null)
            {
                break;
            }
        }

        if (movementController == null)
        {
            return false;
        }

        Transform target = movementController.transform;
        bool changed = false;

        CharacterController oldController = target.GetComponent<CharacterController>();
        if (oldController != null)
        {
            Object.DestroyImmediate(oldController);
            changed = true;
        }

        CapsuleCollider oldCapsule = target.GetComponent<CapsuleCollider>();
        if (oldCapsule != null)
        {
            Object.DestroyImmediate(oldCapsule);
            changed = true;
        }

        SphereCollider sphereCollider = target.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = target.gameObject.AddComponent<SphereCollider>();
            sphereCollider.center = Vector3.zero;
            sphereCollider.radius = 0.5f;
            changed = true;
        }

        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = target.gameObject.AddComponent<Rigidbody>();
            changed = true;
        }

        changed |= ConfigureSphereRigidbody(body);

        Camera mainCamera = FindRootComponent<Camera>(scene, "Main Camera");
        if (mainCamera != null)
        {
            SerializedObject serializedMovement = new SerializedObject(movementController);
            SerializedProperty cameraProperty = serializedMovement.FindProperty("cameraTransform");
            if (cameraProperty.objectReferenceValue != mainCamera.transform)
            {
                cameraProperty.objectReferenceValue = mainCamera.transform;
                serializedMovement.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            SetInteractiveShaderEffects effects = root.GetComponentInChildren<SetInteractiveShaderEffects>(true);
            if (effects != null)
            {
                SerializedObject serializedEffects = new SerializedObject(effects);
                SerializedProperty targetProperty = serializedEffects.FindProperty("target");
                if (targetProperty.objectReferenceValue != target)
                {
                    targetProperty.objectReferenceValue = target;
                    serializedEffects.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }
        }

        ParticleSystem trail = target.GetComponentInChildren<ParticleSystem>(true);
        if (trail != null && !Mathf.Approximately(trail.transform.localPosition.y, -0.49f))
        {
            Vector3 localPosition = trail.transform.localPosition;
            localPosition.y = -0.49f;
            trail.transform.localPosition = localPosition;
            changed = true;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == target.gameObject || root.activeInHierarchy)
            {
                continue;
            }

            PlayerMovementController obsoleteController =
                root.GetComponentInChildren<PlayerMovementController>(true);
            if (obsoleteController != null)
            {
                Object.DestroyImmediate(root);
                changed = true;
            }
        }

        return changed;
    }

    private static bool ConfigureSphereRigidbody(Rigidbody body)
    {
        bool changed =
            !Mathf.Approximately(body.mass, 1f) ||
            !Mathf.Approximately(body.drag, 0.35f) ||
            !Mathf.Approximately(body.angularDrag, 0.05f) ||
            !body.useGravity ||
            body.isKinematic ||
            body.interpolation != RigidbodyInterpolation.Interpolate ||
            body.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic ||
            body.constraints != RigidbodyConstraints.None ||
            !Mathf.Approximately(body.maxAngularVelocity, 30f);

        body.mass = 1f;
        body.drag = 0.35f;
        body.angularDrag = 0.05f;
        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.None;
        body.maxAngularVelocity = 30f;
        return changed;
    }

    private static void RemoveOldTestObjects(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Main Camera" || root.name == "Directional Light")
            {
                root.SetActive(true);
                continue;
            }

            Object.DestroyImmediate(root);
        }
    }

    private static RenderTexture CreateOrUpdateRenderTexture()
    {
        RenderTexture renderTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32)
            {
                name = "SnowRT"
            };
            AssetDatabase.CreateAsset(renderTexture, RenderTexturePath);
        }

        renderTexture.Release();
        renderTexture.width = 1024;
        renderTexture.height = 1024;
        renderTexture.depth = 0;
        renderTexture.antiAliasing = 1;
        renderTexture.filterMode = FilterMode.Bilinear;
        renderTexture.wrapMode = TextureWrapMode.Clamp;
        renderTexture.useMipMap = false;
        renderTexture.autoGenerateMips = false;
        renderTexture.format = RenderTextureFormat.ARGB32;
        EditorUtility.SetDirty(renderTexture);
        return renderTexture;
    }

    private static Material CreateOrUpdateSnowMaterial(Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SnowMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "InteractiveSnow" };
            AssetDatabase.CreateAsset(material, SnowMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        // Tutorial shader defaults with texture slots left empty
        material.SetTexture("_Noise", null);
        material.SetFloat("_NoiseScale", 0.1f);
        material.SetFloat("_NoiseWeight", 0.1f);
        material.SetColor("_ShadowColor", new Color(0.5f, 0.5f, 0.5f, 1f));
        material.SetFloat("_MaxTessDistance", 50f);
        material.SetFloat("_Tess", 20f);
        material.SetColor("_Color", new Color(0.5f, 0.5f, 0.5f, 1f));
        material.SetColor("_PathColorIn", new Color(0.5f, 0.5f, 0.7f, 1f));
        material.SetColor("_PathColorOut", new Color(0.5f, 0.5f, 0.7f, 1f));
        material.SetFloat("_PathBlending", 0.3f);
        material.SetTexture("_MainTex", null);
        material.SetFloat("_SnowHeight", 0.3f);
        material.SetFloat("_SnowDepth", 0.3f);
        material.SetFloat("_SnowTextureOpacity", 0.3f);
        material.SetFloat("_SnowTextureScale", 0.3f);
        material.SetFloat("_SparkleScale", 10f);
        material.SetFloat("_SparkCutoff", 0.8f);
        material.SetTexture("_SparkleNoise", null);
        material.SetFloat("_RimPower", 20f);
        material.SetColor("_RimColor", new Color(0.5f, 0.5f, 0.5f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateOrUpdateParticleMaterial(Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "TrackParticle" };
            AssetDatabase.CreateAsset(material, ParticleMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        Texture2D softCircle = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/glow.png");
        material.SetTexture("_BaseMap", softCircle);
        material.SetTexture("_MainTex", softCircle);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 1f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject CreateGround(Transform parent, Material material)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Snow Ground";
        ground.transform.SetParent(parent);
        ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = material;
        return ground;
    }

    private static GameObject CreateInteractor(Transform parent, int trackLayer, Material particleMaterial)
    {
        GameObject interactor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        interactor.name = "Sphere";
        interactor.transform.SetParent(parent);
        interactor.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);
        interactor.transform.localScale = Vector3.one * 2f;

        SphereCollider sphereCollider = interactor.GetComponent<SphereCollider>();
        sphereCollider.center = Vector3.zero;
        sphereCollider.radius = 0.5f;
        Rigidbody body = interactor.AddComponent<Rigidbody>();
        ConfigureSphereRigidbody(body);
        PlayerMovementController movementController = interactor.AddComponent<PlayerMovementController>();
        if (Camera.main != null)
        {
            SerializedObject serializedMovement = new SerializedObject(movementController);
            serializedMovement.FindProperty("cameraTransform").objectReferenceValue = Camera.main.transform;
            serializedMovement.ApplyModifiedPropertiesWithoutUndo();
        }

        GameObject trail = new GameObject("Track Particles");
        trail.layer = trackLayer;
        trail.transform.SetParent(interactor.transform);
        trail.transform.localPosition = new Vector3(0f, -0.49f, 0f);
        trail.transform.localRotation = Quaternion.identity;

        ParticleSystem particles = trail.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 15f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.1f);
        main.startColor = Color.green;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 10000;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 1.5f;
        emission.rateOverDistance = 10f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
        shape.scale = new Vector3(0.1f, 0.1f, 0.1f);

        Gradient trailGradient = new Gradient();
        trailGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.green, 0f),
                new GradientColorKey(Color.green, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = trailGradient;

        ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        particleRenderer.sharedMaterial = particleMaterial;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        return interactor;
    }

    private static void CreateTrackingCamera(
        Transform parent,
        Transform target,
        int trackLayer,
        RenderTexture renderTexture)
    {
        GameObject cameraObject = new GameObject("Track Camera");
        cameraObject.transform.SetParent(parent);
        cameraObject.transform.SetPositionAndRotation(
            target.position + Vector3.up * 20f,
            Quaternion.Euler(90f, 0f, 0f));

        Camera trackingCamera = cameraObject.AddComponent<Camera>();
        trackingCamera.orthographic = true;
        trackingCamera.orthographicSize = 15f;
        trackingCamera.nearClipPlane = 0.1f;
        trackingCamera.farClipPlane = 50f;
        trackingCamera.clearFlags = CameraClearFlags.SolidColor;
        trackingCamera.backgroundColor = Color.black;
        trackingCamera.cullingMask = 1 << trackLayer;
        trackingCamera.targetTexture = renderTexture;
        trackingCamera.allowHDR = false;
        trackingCamera.allowMSAA = false;
        trackingCamera.depth = -10f;

        UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.requiresDepthTexture = false;
        cameraData.requiresColorTexture = false;

        SetInteractiveShaderEffects shaderEffects = cameraObject.AddComponent<SetInteractiveShaderEffects>();
        SerializedObject serializedEffects = new SerializedObject(shaderEffects);
        serializedEffects.FindProperty("rt").objectReferenceValue = renderTexture;
        serializedEffects.FindProperty("target").objectReferenceValue = target;
        serializedEffects.FindProperty("yOffset").floatValue = 20f;
        serializedEffects.FindProperty("trackingCamera").objectReferenceValue = trackingCamera;
        serializedEffects.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T FindRootComponent<T>(Scene scene, string rootName) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == rootName)
            {
                return root.GetComponent<T>();
            }
        }

        return null;
    }
}
