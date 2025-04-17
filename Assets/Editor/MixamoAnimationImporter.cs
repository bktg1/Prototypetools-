using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

[System.Serializable]
public class AnimationPack : ScriptableObject
{
    public string packName;
    public List<AnimationClip> animations = new List<AnimationClip>();
    public string description;
}

[System.Serializable]
public class PrefabTemplate : ScriptableObject
{
    public string templateName;
    public List<Component> requiredComponents = new List<Component>();
    public List<MonoScript> scripts = new List<MonoScript>();
    public bool includeCollider = true;
    public bool includeRigidbody = false;
    public bool isTrigger = false;
}

public class MixamoAnimationImporter : EditorWindow
{
    private const string VERSION = "1.0.0";
    private const string TOOL_NAME = "Mixamo Animation Importer";
    
    private GameObject targetModel;
    private Vector2 scrollPosition;
    private List<AnimationClip> animationClips = new List<AnimationClip>();
    private bool createNewAnimator = true;
    private string animatorControllerName = "NewAnimatorController";
    private bool convertToURP = true;
    private bool createAnimationPack = false;
    private string packName = "New Animation Pack";
    private string packDescription = "";
    private bool setHumanoid = true;
    private bool autoRename = true;
    private Vector2 mainScrollPosition;

    // Animation Settings
    private bool autoDetectLoops = true;
    private bool enableRootMotion = false;
    private float rootMotionThreshold = 0.1f;
    private bool addFootstepEvents = false;
    private bool optimizeForMobile = false;
    private bool createTimeline = false;
    private string timelineName = "New Timeline";

    // Preview fields
    private GameObject previewModel;
    private Animator previewAnimator;
    private AnimationClip currentPreviewClip;
    private float previewTime = 0f;
    private bool isPlaying = false;
    private bool isLooping = true;
    private float playbackSpeed = 1f;
    private Editor previewEditor;
    private float lastUpdateTime;
    private Vector2 previewScrollPosition;
    private bool showPreview = false;

    // Prefab Generator Settings
    private bool generatePrefab = false;
    private string prefabName = "AnimatedPrefab";
    private bool includeCollider = true;
    private bool includeRigidbody = false;
    private bool isTrigger = false;
    private List<MonoScript> selectedScripts = new List<MonoScript>();
    private Vector2 scriptScrollPosition;
    private List<PrefabTemplate> savedTemplates = new List<PrefabTemplate>();
    private int selectedTemplateIndex = -1;

    // Preview Window
    private EditorWindow previewWindow;
    private Vector2 previewWindowSize = new Vector2(400, 400);
    private bool usePopOutPreview = false;

    // Model Animation Copy
    private GameObject sourceModel;
    private List<AnimationClip> sourceAnimations = new List<AnimationClip>();
    private Vector2 sourceAnimationsScrollPosition;

    [MenuItem("BKT Suite/Mixamo Animation Importer")]
    public static void ShowWindow()
    {
        GetWindow<MixamoAnimationImporter>("Mixamo Animation Importer");
    }

    private void OnEnable()
    {
        CreateDefaultPreviewModel();
    }

    private void CreateDefaultPreviewModel()
    {
        previewModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        previewModel.name = "Animation Preview Model";
        previewModel.transform.position = Vector3.zero;
        previewAnimator = previewModel.AddComponent<Animator>();
        previewModel.hideFlags = HideFlags.HideAndDontSave;
    }

    private void OnGUI()
    {
        mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label($"{TOOL_NAME} v{VERSION}", EditorStyles.boldLabel);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();

        // Target Model Selection
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Model Settings", EditorStyles.boldLabel);
        targetModel = (GameObject)EditorGUILayout.ObjectField("Target Model", targetModel, typeof(GameObject), true);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        // Import Settings
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
        setHumanoid = EditorGUILayout.Toggle("Set Humanoid Avatar", setHumanoid);
        autoRename = EditorGUILayout.Toggle("Auto-Rename Animations", autoRename);
        convertToURP = EditorGUILayout.Toggle("Convert to URP Shaders", convertToURP);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();

        // Animation Settings
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Animation Settings", EditorStyles.boldLabel);
        autoDetectLoops = EditorGUILayout.Toggle("Auto-detect Loopable Animations", autoDetectLoops);
        enableRootMotion = EditorGUILayout.Toggle("Enable Root Motion", enableRootMotion);
        if (enableRootMotion)
        {
            rootMotionThreshold = EditorGUILayout.Slider("Root Motion Threshold", rootMotionThreshold, 0.01f, 1f);
        }
        addFootstepEvents = EditorGUILayout.Toggle("Add Footstep Events", addFootstepEvents);
        optimizeForMobile = EditorGUILayout.Toggle("Optimize for Mobile", optimizeForMobile);
        createTimeline = EditorGUILayout.Toggle("Create Timeline", createTimeline);
        if (createTimeline)
        {
            timelineName = EditorGUILayout.TextField("Timeline Name", timelineName);
        }
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        // Animation List with Preview
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Animations", EditorStyles.boldLabel);
        
        // Drag and Drop Area
        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop Animation Files Here", EditorStyles.helpBox);
        
        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform)
        {
            if (!dropArea.Contains(currentEvent.mousePosition))
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                ProcessDroppedFiles(DragAndDrop.paths);
            }
            currentEvent.Use();
        }

        previewScrollPosition = EditorGUILayout.BeginScrollView(previewScrollPosition);
        for (int i = 0; i < animationClips.Count; i++)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            animationClips[i] = (AnimationClip)EditorGUILayout.ObjectField(animationClips[i], typeof(AnimationClip), false);
            
            if (GUILayout.Button("Preview", GUILayout.Width(60)))
            {
                currentPreviewClip = animationClips[i];
                previewTime = 0f;
                isPlaying = true;
                showPreview = true;
                UpdatePreview();
            }
            
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                animationClips.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        // Model Animation Copy Section
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Copy Animations from Model", EditorStyles.boldLabel);
        
        sourceModel = (GameObject)EditorGUILayout.ObjectField("Source Model", sourceModel, typeof(GameObject), true);
        
        if (sourceModel != null)
        {
            if (GUILayout.Button("Load Animations from Model"))
            {
                LoadAnimationsFromModel();
            }
            
            if (sourceAnimations.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Available Animations", EditorStyles.boldLabel);
                
                sourceAnimationsScrollPosition = EditorGUILayout.BeginScrollView(sourceAnimationsScrollPosition, GUILayout.Height(100));
                for (int i = 0; i < sourceAnimations.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(sourceAnimations[i], typeof(AnimationClip), false);
                    if (GUILayout.Button("Copy", GUILayout.Width(60)))
                    {
                        CopyAnimationFromModel(sourceAnimations[i]);
                    }
                    if (GUILayout.Button("Preview", GUILayout.Width(60)))
                    {
                        PreviewAnimation(sourceAnimations[i]);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        EditorGUILayout.EndVertical();

        // Preview Section
        if (showPreview && currentPreviewClip != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            // Preview Window Toggle
            usePopOutPreview = EditorGUILayout.Toggle("Use Pop-out Preview", usePopOutPreview);

            if (!usePopOutPreview)
            {
                // Inline Preview
                if (previewModel != null)
                {
                    if (previewEditor == null)
                    {
                        previewEditor = Editor.CreateEditor(previewModel);
                    }
                    Rect previewRect = GUILayoutUtility.GetRect(200, 200);
                    previewEditor.OnPreviewGUI(previewRect, EditorStyles.whiteLabel);
                }
            }
            else
            {
                // Pop-out Preview Button
                if (GUILayout.Button("Open Preview Window"))
                {
                    ShowPreviewWindow();
                }
            }

            // Playback Controls
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "Pause" : "Play"))
            {
                isPlaying = !isPlaying;
            }
            isLooping = GUILayout.Toggle(isLooping, "Loop");
            playbackSpeed = EditorGUILayout.Slider(playbackSpeed, 0.1f, 2f);
            if (GUILayout.Button("Close Preview"))
            {
                showPreview = false;
                isPlaying = false;
                currentPreviewClip = null;
            }
            EditorGUILayout.EndHorizontal();

            // Timeline
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Time: " + previewTime.ToString("F2") + " / " + currentPreviewClip.length.ToString("F2"));
            float newTime = EditorGUILayout.Slider(previewTime, 0f, currentPreviewClip.length);
            if (newTime != previewTime)
            {
                previewTime = newTime;
                UpdatePreview();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        // Animation Pack Settings
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Animation Pack Settings", EditorStyles.boldLabel);
        createAnimationPack = EditorGUILayout.Toggle("Create Animation Pack", createAnimationPack);
        
        if (createAnimationPack)
        {
            packName = EditorGUILayout.TextField("Pack Name", packName);
            EditorGUILayout.LabelField("Pack Description");
            packDescription = EditorGUILayout.TextArea(packDescription, GUILayout.Height(60));
        }
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        // Animator Settings
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Animator Settings", EditorStyles.boldLabel);
        createNewAnimator = EditorGUILayout.Toggle("Create New Animator Controller", createNewAnimator);
        if (createNewAnimator)
        {
            animatorControllerName = EditorGUILayout.TextField("Controller Name", animatorControllerName);
        }
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space();
        
        // Prefab Generator Settings
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Prefab Generator", EditorStyles.boldLabel);
        
        generatePrefab = EditorGUILayout.Toggle("Generate Prefab", generatePrefab);
        
        if (generatePrefab)
        {
            prefabName = EditorGUILayout.TextField("Prefab Name", prefabName);
            
            // Template Selection
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Template Settings", EditorStyles.boldLabel);
            
            // Load saved templates
            if (savedTemplates.Count == 0)
            {
                string[] templateGuids = AssetDatabase.FindAssets("t:PrefabTemplate");
                savedTemplates.Clear();
                foreach (string guid in templateGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    PrefabTemplate template = AssetDatabase.LoadAssetAtPath<PrefabTemplate>(path);
                    if (template != null)
                    {
                        savedTemplates.Add(template);
                    }
                }
            }
            
            // Template dropdown
            string[] templateNames = savedTemplates.Select(t => t.templateName).ToArray();
            selectedTemplateIndex = EditorGUILayout.Popup("Load Template", selectedTemplateIndex, templateNames);
            
            if (selectedTemplateIndex >= 0 && selectedTemplateIndex < savedTemplates.Count)
            {
                PrefabTemplate template = savedTemplates[selectedTemplateIndex];
                includeCollider = template.includeCollider;
                includeRigidbody = template.includeRigidbody;
                isTrigger = template.isTrigger;
                selectedScripts = new List<MonoScript>(template.scripts);
            }
            
            // Component Settings
            EditorGUILayout.Space();
            includeCollider = EditorGUILayout.Toggle("Include Collider", includeCollider);
            if (includeCollider)
            {
                isTrigger = EditorGUILayout.Toggle("Is Trigger", isTrigger);
            }
            includeRigidbody = EditorGUILayout.Toggle("Include Rigidbody", includeRigidbody);
            
            // Script Selection
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scripts to Add", EditorStyles.boldLabel);
            
            scriptScrollPosition = EditorGUILayout.BeginScrollView(scriptScrollPosition, GUILayout.Height(100));
            for (int i = 0; i < selectedScripts.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                selectedScripts[i] = (MonoScript)EditorGUILayout.ObjectField(selectedScripts[i], typeof(MonoScript), false);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    selectedScripts.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            
            if (GUILayout.Button("Add Script"))
            {
                selectedScripts.Add(null);
            }
            
            // Template Management
            EditorGUILayout.Space();
            if (GUILayout.Button("Save as Template"))
            {
                SaveAsTemplate();
            }
        }
        
        EditorGUILayout.EndVertical();
        
        // Apply Button
        if (GUILayout.Button("Apply Animations", GUILayout.Height(30)))
        {
            ApplyAnimations();
        }

        EditorGUILayout.EndScrollView();
    }

    private void Update()
    {
        if (isPlaying && currentPreviewClip != null)
        {
            float deltaTime = Time.realtimeSinceStartup - lastUpdateTime;
            previewTime += deltaTime * playbackSpeed;
            
            if (isLooping)
            {
                previewTime %= currentPreviewClip.length;
            }
            else if (previewTime >= currentPreviewClip.length)
            {
                previewTime = currentPreviewClip.length;
                isPlaying = false;
            }
            
            UpdatePreview();
            lastUpdateTime = Time.realtimeSinceStartup;
            Repaint();
        }
    }

    private void UpdatePreview()
    {
        if (currentPreviewClip != null && previewModel != null)
        {
            currentPreviewClip.SampleAnimation(previewModel, previewTime);
        }
    }

    private void OnDestroy()
    {
        if (previewModel != null)
        {
            DestroyImmediate(previewModel);
        }
        
        if (previewWindow != null)
        {
            previewWindow.Close();
        }
    }

    private void ProcessDroppedFiles(string[] paths)
    {
        foreach (string path in paths)
        {
            if (Path.GetExtension(path).ToLower() == ".fbx")
            {
                // Set import settings
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null)
                {
                    if (setHumanoid)
                    {
                        importer.animationType = ModelImporterAnimationType.Generic;
                        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        importer.optimizeGameObjects = true;
                    }
                    importer.SaveAndReimport();
                }

                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !animationClips.Contains(clip))
                {
                    if (autoRename)
                    {
                        string newName = GetSmartName(clip.name);
                        clip.name = newName;
                    }

                    // Apply animation settings
                    if (autoDetectLoops)
                    {
                        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                        settings.loopTime = MixamoAnimationUtils.IsLoopable(clip);
                        AnimationUtility.SetAnimationClipSettings(clip, settings);
                    }

                    if (addFootstepEvents)
                    {
                        MixamoAnimationUtils.AddAnimationEvents(clip, true, false);
                    }

                    if (optimizeForMobile)
                    {
                        MixamoAnimationUtils.OptimizeForMobile(clip, true, 0.8f);
                    }

                    if (generatePrefab)
                    {
                        GeneratePrefab(clip);
                    }

                    animationClips.Add(clip);
                }
            }
        }
    }

    private string GetSmartName(string originalName)
    {
        // Remove common Mixamo prefixes
        string name = originalName.Replace("mixamo.com@", "");
        
        // Convert to proper case
        name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
        
        // Replace underscores with spaces
        name = name.Replace("_", " ");
        
        return name;
    }

    private void ConvertToURPShaders(GameObject model)
    {
        if (!convertToURP) return;

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    // Check if the material is using a legacy shader
                    if (materials[i].shader.name.Contains("Legacy"))
                    {
                        // Create a new URP-compatible material
                        Material urpMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        
                        // Copy properties from the original material
                        urpMaterial.CopyPropertiesFromMaterial(materials[i]);
                        
                        // Set the new material
                        materials[i] = urpMaterial;
                    }
                }
            }
            renderer.sharedMaterials = materials;
        }
    }

    private void CreateAnimationPack()
    {
        if (!createAnimationPack || animationClips.Count == 0) return;

        // Create the animation pack asset
        AnimationPack pack = ScriptableObject.CreateInstance<AnimationPack>();
        pack.packName = packName;
        pack.description = packDescription;
        pack.animations = new List<AnimationClip>(animationClips);

        // Create the directory if it doesn't exist
        string packPath = "Assets/AnimationPacks";
        if (!Directory.Exists(packPath))
        {
            Directory.CreateDirectory(packPath);
        }

        // Save the animation pack
        string assetPath = $"{packPath}/{packName}.asset";
        AssetDatabase.CreateAsset(pack, assetPath);
        AssetDatabase.SaveAssets();
    }

    private void ApplyAnimations()
    {
        if (targetModel == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a target model first!", "OK");
            return;
        }

        if (animationClips.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No animations to apply!", "OK");
            return;
        }

        // Convert shaders to URP if needed
        ConvertToURPShaders(targetModel);

        // Get or create Animator component
        Animator animator = targetModel.GetComponent<Animator>();
        if (animator == null)
        {
            animator = targetModel.AddComponent<Animator>();
        }

        // Create or get Animator Controller
        RuntimeAnimatorController controller = null;
        if (createNewAnimator)
        {
            string controllerPath = "Assets/AnimatorControllers/" + animatorControllerName + ".controller";
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }
        else
        {
            controller = animator.runtimeAnimatorController;
        }

        if (controller == null)
        {
            EditorUtility.DisplayDialog("Error", "Failed to create/get Animator Controller!", "OK");
            return;
        }

        // Apply animations to the controller
        AnimatorController animatorController = controller as AnimatorController;
        if (animatorController != null)
        {
            foreach (AnimationClip clip in animationClips)
            {
                // Create animation state
                AnimatorState state = animatorController.layers[0].stateMachine.AddState(clip.name);
                state.motion = clip;

                // Set up transitions
                if (animatorController.layers[0].stateMachine.states.Length > 1)
                {
                    AnimatorStateTransition transition = animatorController.layers[0].stateMachine.AddAnyStateTransition(state);
                    transition.duration = 0.25f;
                    transition.hasExitTime = false;
                }

                // Create timeline if requested
                if (createTimeline)
                {
                    MixamoAnimationUtils.CreateTimelineForAnimations(new List<AnimationClip> { clip }, $"{timelineName}_{clip.name}");
                }
            }
        }

        // Apply the controller to the animator
        animator.runtimeAnimatorController = controller;

        // Create animation pack if requested
        CreateAnimationPack();

        // Save changes
        EditorUtility.SetDirty(targetModel);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", "Animations applied successfully!", "OK");
    }

    private void SaveAsTemplate()
    {
        PrefabTemplate template = ScriptableObject.CreateInstance<PrefabTemplate>();
        template.templateName = prefabName + "_Template";
        template.includeCollider = includeCollider;
        template.includeRigidbody = includeRigidbody;
        template.isTrigger = isTrigger;
        template.scripts = new List<MonoScript>(selectedScripts);
        
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Template",
            template.templateName,
            "asset",
            "Save the template as an asset"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(template, path);
            AssetDatabase.SaveAssets();
            savedTemplates.Add(template);
        }
    }

    private void GeneratePrefab(AnimationClip clip)
    {
        // Create the prefab
        GameObject prefab = new GameObject(prefabName);
        
        // Add required components
        if (includeCollider)
        {
            CapsuleCollider collider = prefab.AddComponent<CapsuleCollider>();
            collider.isTrigger = isTrigger;
        }
        
        if (includeRigidbody)
        {
            Rigidbody rb = prefab.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.isKinematic = false;
        }
        
        // Add selected scripts
        foreach (MonoScript script in selectedScripts)
        {
            if (script != null)
            {
                System.Type type = script.GetClass();
                if (type != null)
                {
                    prefab.AddComponent(type);
                }
            }
        }
        
        // Add animator and set up animation
        Animator animator = prefab.AddComponent<Animator>();
        RuntimeAnimatorController controller = CreateAnimatorController(clip);
        animator.runtimeAnimatorController = controller;
        
        // Save as prefab
        string prefabPath = "Assets/Prefabs/" + prefabName + ".prefab";
        string directory = Path.GetDirectoryName(prefabPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        DestroyImmediate(prefab);
    }

    private RuntimeAnimatorController CreateAnimatorController(AnimationClip clip)
    {
        // Create controller
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(
            "Assets/AnimatorControllers/" + prefabName + "_Controller.controller"
        );
        
        // Add animation state
        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
        AnimatorState state = rootStateMachine.AddState(clip.name);
        state.motion = clip;
        
        // Set up transitions
        AnimatorStateTransition transition = rootStateMachine.AddAnyStateTransition(state);
        transition.duration = 0.25f;
        transition.hasExitTime = false;
        
        return controller;
    }

    private void LoadAnimationsFromModel()
    {
        sourceAnimations.Clear();
        
        // Get animations from Animator
        Animator animator = sourceModel.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller != null)
            {
                foreach (AnimatorControllerLayer layer in controller.layers)
                {
                    foreach (ChildAnimatorState state in layer.stateMachine.states)
                    {
                        if (state.state.motion is AnimationClip clip)
                        {
                            sourceAnimations.Add(clip);
                        }
                    }
                }
            }
        }
        
        // Get animations from Animation component (legacy)
        Animation legacyAnimation = sourceModel.GetComponent<Animation>();
        if (legacyAnimation != null)
        {
            foreach (AnimationState state in legacyAnimation)
            {
                sourceAnimations.Add(state.clip);
            }
        }
    }

    private void CopyAnimationFromModel(AnimationClip clip)
    {
        if (clip == null) return;

        // Create a copy of the animation
        AnimationClip newClip = new AnimationClip();
        EditorUtility.CopySerialized(clip, newClip);
        
        // Add to animation list
        if (!animationClips.Contains(newClip))
        {
            animationClips.Add(newClip);
        }
    }

    private void ShowPreviewWindow()
    {
        if (previewWindow == null)
        {
            previewWindow = GetWindow<EditorWindow>("Animation Preview", typeof(SceneView));
            previewWindow.minSize = previewWindowSize;
            previewWindow.position = new Rect(position.x + position.width, position.y, previewWindowSize.x, previewWindowSize.y);
        }
        
        previewWindow.Show();
        UpdatePreviewWindow();
    }

    private void UpdatePreviewWindow()
    {
        if (previewWindow != null && currentPreviewClip != null && previewModel != null)
        {
            previewWindow.Repaint();
        }
    }

    private void PreviewAnimation(AnimationClip clip)
    {
        currentPreviewClip = clip;
        previewTime = 0f;
        isPlaying = true;
        showPreview = true;
        
        if (usePopOutPreview)
        {
            ShowPreviewWindow();
        }
        
        UpdatePreview();
    }
} 