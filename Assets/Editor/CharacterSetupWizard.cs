using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Cinemachine;
using UnityEditor.Animations;
using UnityEngine.AI;

namespace BlackylesMixamoTools
{
    /// <summary>
    /// Character Setup Wizard - Part of BKT Productions Suite (Alpha v1.0.1)
    /// A tool for quickly setting up character controllers, input systems, and cameras.
    /// </summary>
    public class CharacterSetupWizard : EditorWindow
    {
        private const string VERSION = "Alpha v1.0.1";
        private const string SUITE_NAME = "BKT Productions Suite";
        private GameObject characterModel;
        private bool createAnimatorController = true;
        private bool setupMaterials = true;
        private bool alignRootBone = true;
        private bool createPrefab = true;
        private bool setupCamera = true;
        private bool setupCharacterController = true;
        private bool setupInput = true;
        private string prefabPath = "Assets/Prefabs/Characters";
        private Vector2 scrollPosition;
        private CharacterType characterType = CharacterType.Player;
        private bool setupNavMeshAgent = false;
        private bool setupAI = false;
        private bool isSettingUp = false;

        private enum CharacterType
        {
            Player,
            NPC
        }

        [MenuItem("BKT Suite/Character Setup Wizard")]
        public static void ShowWindow()
        {
            var window = GetWindow<CharacterSetupWizard>("Character Setup Wizard");
            window.minSize = new Vector2(400, 500);
        }

        private void OnGUI()
        {
            if (isSettingUp)
            {
                EditorGUILayout.HelpBox("Setting up character...", MessageType.Info);
                return;
            }

            try
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Character Setup Wizard - {VERSION}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Part of {SUITE_NAME}", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox("This is an alpha version. Please report any issues.", MessageType.Warning);
                EditorGUILayout.Space();

                // Character Type Selection
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                characterType = (CharacterType)EditorGUILayout.EnumPopup("Character Type", characterType);
                if (EditorGUI.EndChangeCheck())
                {
                    GUI.FocusControl(null);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                // Model Selection
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                characterModel = EditorGUILayout.ObjectField("Character Model", characterModel, typeof(GameObject), false) as GameObject;
                if (EditorGUI.EndChangeCheck())
                {
                    GUI.FocusControl(null);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                // Setup Options
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Setup Options", EditorStyles.boldLabel);
                
                EditorGUI.BeginChangeCheck();
                createAnimatorController = EditorGUILayout.Toggle("Create Animator Controller", createAnimatorController);
                setupMaterials = EditorGUILayout.Toggle("Setup Materials", setupMaterials);
                alignRootBone = EditorGUILayout.Toggle("Align Root Bone", alignRootBone);

                if (characterType == CharacterType.Player)
                {
                    setupCamera = EditorGUILayout.Toggle("Setup Camera", setupCamera);
                    setupCharacterController = EditorGUILayout.Toggle("Setup Character Controller", setupCharacterController);
                    setupInput = EditorGUILayout.Toggle("Setup Input", setupInput);
                }
                else
                {
                    setupNavMeshAgent = EditorGUILayout.Toggle("Setup NavMesh Agent", setupNavMeshAgent);
                    setupAI = EditorGUILayout.Toggle("Setup AI Components", setupAI);
                }

                createPrefab = EditorGUILayout.Toggle("Create Prefab", createPrefab);
                
                if (createPrefab)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Prefab Path:", GUILayout.Width(100));
                    prefabPath = EditorGUILayout.TextField(prefabPath);
                    if (GUILayout.Button("Browse", GUILayout.Width(60)))
                    {
                        string path = EditorUtility.SaveFolderPanel("Select Prefab Folder", prefabPath, "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            prefabPath = "Assets" + path.Substring(Application.dataPath.Length);
                            GUI.FocusControl(null);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space();

                // Setup Button
                EditorGUI.BeginDisabledGroup(characterModel == null || isSettingUp);
                if (GUILayout.Button("Setup Character", GUILayout.Height(30)))
                {
                    isSettingUp = true;
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            SetupCharacter();
                        }
                        finally
                        {
                            isSettingUp = false;
                            Repaint();
                        }
                    };
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndScrollView();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in CharacterSetupWizard OnGUI: {e.Message}\n{e.StackTrace}");
                EditorGUILayout.HelpBox($"An error occurred: {e.Message}", MessageType.Error);
            }
        }

        private void SetupCharacter()
        {
            if (characterModel == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a character model first!", "OK");
                return;
            }

            try
            {
                GameObject characterSetup;
                if (characterType == CharacterType.Player)
                {
                    characterSetup = SetupPlayerCharacter();
                }
                else
                {
                    characterSetup = SetupNPCCharacter();
                }

                // Select the new setup in the hierarchy
                Selection.activeGameObject = characterSetup;

                EditorUtility.DisplayDialog("Success", "Character setup completed successfully!", "OK");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"An error occurred during setup: {e.Message}", "OK");
                Debug.LogError($"Character Setup Error: {e.Message}\n{e.StackTrace}");
            }
        }

        private void CreateDefaultInputActions()
        {
            try
            {
                // Create the input actions directory if it doesn't exist
                string inputActionsPath = "Assets/Input";
                if (!Directory.Exists(inputActionsPath))
                {
                    Directory.CreateDirectory(inputActionsPath);
                }

                // Check if the input actions file already exists
                string assetPath = Path.Combine(inputActionsPath, "PlayerInputActions.inputactions");
                if (File.Exists(assetPath))
                {
                    // Try to load existing asset
                    var existingAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                    if (existingAsset != null)
                    {
                        return; // Asset exists and is valid, no need to create new one
                    }
                    else
                    {
                        // If asset exists but is invalid, delete it
                        File.Delete(assetPath);
                        AssetDatabase.Refresh();
                    }
                }

                // Create a new input action asset
                var inputActions = ScriptableObject.CreateInstance<InputActionAsset>();
                inputActions.name = "PlayerInputActions";

                // Create the action map
                var actionMap = inputActions.AddActionMap("Player");

                // Add movement action
                var moveAction = actionMap.AddAction("Move");
                moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");

                // Add look action
                var lookAction = actionMap.AddAction("Look");
                lookAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Mouse>/delta/y")
                    .With("Down", "<Mouse>/delta/y")
                    .With("Left", "<Mouse>/delta/x")
                    .With("Right", "<Mouse>/delta/x");

                // Add jump action
                var jumpAction = actionMap.AddAction("Jump");
                jumpAction.AddBinding("<Keyboard>/space");

                // Add sprint action
                var sprintAction = actionMap.AddAction("Sprint");
                sprintAction.AddBinding("<Keyboard>/leftShift");

                // Create control schemes
                var keyboardMouseScheme = new InputControlScheme("Keyboard&Mouse",
                    new InputControlScheme.DeviceRequirement[]
                    {
                        new InputControlScheme.DeviceRequirement { controlPath = "<Keyboard>" },
                        new InputControlScheme.DeviceRequirement { controlPath = "<Mouse>" }
                    });

                // Save the asset
                string json = inputActions.ToJson();
                File.WriteAllText(assetPath, json);
                AssetDatabase.ImportAsset(assetPath);

                // Wait for the asset to be imported
                AssetDatabase.Refresh();
                while (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == null)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error creating input actions: {e.Message}\n{e.StackTrace}");
                throw;
            }
        }

        private GameObject CreateDefaultCharacterStructure()
        {
            // Create the root object
            GameObject characterRoot = new GameObject("CharacterRoot");
            
            // Add Character Controller
            var characterController = characterRoot.AddComponent<CharacterController>();
            characterController.center = new Vector3(0, 1f, 0);
            characterController.radius = 0.3f;
            characterController.height = 1.8f;
            characterController.slopeLimit = 45f;
            characterController.stepOffset = 0.3f;
            characterController.skinWidth = 0.08f;
            characterController.minMoveDistance = 0.001f;

            // Create and setup input actions
            CreateDefaultInputActions();

            // Add Player Input
            var playerInput = characterRoot.AddComponent<PlayerInput>();
            playerInput.notificationBehavior = PlayerNotifications.InvokeUnityEvents;
            playerInput.defaultActionMap = "Player";
            playerInput.defaultControlScheme = "Keyboard&Mouse";

            // Load input actions with error handling
            string inputActionsPath = "Assets/Input/PlayerInputActions.inputactions";
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(inputActionsPath);
            if (inputActions == null)
            {
                Debug.LogError($"Failed to load input actions from {inputActionsPath}");
                // Create a default input actions asset in memory
                inputActions = ScriptableObject.CreateInstance<InputActionAsset>();
                inputActions.name = "PlayerInputActions";
                var actionMap = inputActions.AddActionMap("Player");
                actionMap.AddAction("Move");
                actionMap.AddAction("Look");
                actionMap.AddAction("Jump");
                actionMap.AddAction("Sprint");
            }
            playerInput.actions = inputActions;

            // Create Camera Target
            GameObject cameraTarget = new GameObject("CameraTarget");
            cameraTarget.transform.SetParent(characterRoot.transform);
            cameraTarget.transform.localPosition = new Vector3(0, 1.5f, 0);
            cameraTarget.transform.localRotation = Quaternion.identity;

            // Create Camera Root
            GameObject cameraRoot = new GameObject("CameraRoot");
            cameraRoot.transform.SetParent(characterRoot.transform);
            cameraRoot.transform.localPosition = new Vector3(0, 1.5f, 0);
            cameraRoot.transform.localRotation = Quaternion.identity;

            // Create Virtual Camera
            GameObject vcam = new GameObject("PlayerFollowCamera");
            vcam.transform.SetParent(cameraRoot.transform);
            vcam.transform.localPosition = new Vector3(0, 0, -4);
            vcam.transform.localRotation = Quaternion.identity;

            // Add Cinemachine Components
            var virtualCamera = vcam.AddComponent<CinemachineVirtualCamera>();
            virtualCamera.Follow = cameraTarget.transform;
            virtualCamera.LookAt = cameraTarget.transform;
            virtualCamera.m_Lens.FieldOfView = 40f;
            virtualCamera.m_Lens.NearClipPlane = 0.1f;
            virtualCamera.m_Lens.FarClipPlane = 1000f;

            // Add Transposer
            var transposer = virtualCamera.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_FollowOffset = new Vector3(0, 1.5f, -4);
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTarget;
            transposer.m_XDamping = 1f;
            transposer.m_YDamping = 1f;
            transposer.m_ZDamping = 1f;

            // Add Composer
            var composer = virtualCamera.AddCinemachineComponent<CinemachineComposer>();
            composer.m_TrackedObjectOffset = new Vector3(0, 0.5f, 0);
            composer.m_LookaheadTime = 0.1f;
            composer.m_LookaheadSmoothing = 5f;
            composer.m_HorizontalDamping = 0.5f;
            composer.m_VerticalDamping = 0.5f;

            return characterRoot;
        }

        private GameObject SetupPlayerCharacter()
        {
            // Create the default character structure
            GameObject characterSetup = CreateDefaultCharacterStructure();
            characterSetup.name = characterModel.name + "_Controller";
            Undo.RegisterCreatedObjectUndo(characterSetup, "Create Character Setup");

            // Instantiate the new model
            GameObject modelInstance = Instantiate(characterModel, characterSetup.transform);
            modelInstance.name = "Model";

            // Setup Animator
            if (createAnimatorController)
            {
                SetupAnimator(modelInstance);
            }

            // Setup Materials
            if (setupMaterials)
            {
                SetupMaterials(modelInstance);
            }

            // Align Root Bone
            if (alignRootBone)
            {
                AlignRootBone(modelInstance);
            }

            // Create Prefab
            if (createPrefab)
            {
                CreatePrefab(characterSetup);
            }

            return characterSetup;
        }

        private GameObject SetupNPCCharacter()
        {
            // Create a new GameObject to hold the character setup
            GameObject characterSetup = new GameObject(characterModel.name + "_NPC");
            Undo.RegisterCreatedObjectUndo(characterSetup, "Create NPC Setup");

            // Add required components to the container
            if (setupNavMeshAgent)
            {
                var navMeshAgent = characterSetup.AddComponent<NavMeshAgent>();
                navMeshAgent.height = 1.8f;
                navMeshAgent.radius = 0.3f;
                navMeshAgent.baseOffset = 1f;
            }

            if (setupAI)
            {
                // Try to add AIController if it exists
                try
                {
                    var aiControllerType = System.Type.GetType("AIController");
                    if (aiControllerType != null)
                    {
                        characterSetup.AddComponent(aiControllerType);
                    }
                    else
                    {
                        Debug.LogWarning("AIController script not found. Skipping component addition.");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error adding AIController: {e.Message}");
                }
            }

            // Instantiate the model as a child
            GameObject modelInstance = Instantiate(characterModel, characterSetup.transform);
            modelInstance.name = characterModel.name;

            // Setup Animator
            if (createAnimatorController)
            {
                SetupNPCAnimator(modelInstance);
            }

            // Setup Materials
            if (setupMaterials)
            {
                SetupMaterials(modelInstance);
            }

            // Align Root Bone
            if (alignRootBone)
            {
                AlignRootBone(modelInstance);
            }

            // Create Prefab
            if (createPrefab)
            {
                CreatePrefab(characterSetup);
            }

            return characterSetup;
        }

        private void SetupNPCAnimator(GameObject model)
        {
            // Add Animator component if it doesn't exist
            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }

            // Create and assign a new Animator Controller
            string controllerPath = $"Assets/Animators/{model.name}_NPC_Controller.controller";
            Directory.CreateDirectory(Path.GetDirectoryName(controllerPath));
            
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            animator.runtimeAnimatorController = controller;

            // Setup default layers and parameters
            var rootStateMachine = controller.layers[0].stateMachine;
            var idleState = rootStateMachine.AddState("Idle");
            rootStateMachine.defaultState = idleState;

            // Add common parameters for NPCs
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsInteracting", AnimatorControllerParameterType.Bool);
        }

        private void SetupAnimator(GameObject model)
        {
            // Add Animator component if it doesn't exist
            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }

            // Create and assign a new Animator Controller
            string controllerPath = $"Assets/Animators/{model.name}_Controller.controller";
            Directory.CreateDirectory(Path.GetDirectoryName(controllerPath));
            
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            animator.runtimeAnimatorController = controller;

            // Setup default layers and parameters
            var rootStateMachine = controller.layers[0].stateMachine;
            var idleState = rootStateMachine.AddState("Idle");
            rootStateMachine.defaultState = idleState;

            // Add common parameters
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("FreeFall", AnimatorControllerParameterType.Bool);
            controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        }

        private void SetupMaterials(GameObject model)
        {
            // Get all renderers in the model
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                // Create a new material for each renderer
                Material newMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                newMaterial.name = $"{model.name}_{renderer.name}_Material";
                
                // Copy main texture if it exists
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null)
                {
                    newMaterial.mainTexture = renderer.sharedMaterial.mainTexture;
                }

                // Apply the new material
                renderer.sharedMaterial = newMaterial;
            }
        }

        private void AlignRootBone(GameObject model)
        {
            // Find the root bone (usually Hips or Root)
            Transform rootBone = null;
            var animator = model.GetComponent<Animator>();
            
            if (animator != null && animator.isHuman)
            {
                rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            else
            {
                // Try to find a bone named "Hips" or "Root"
                rootBone = model.transform.Find("Hips") ?? model.transform.Find("Root");
            }

            if (rootBone != null)
            {
                // Store original position and rotation
                Vector3 originalPosition = rootBone.position;
                Quaternion originalRotation = rootBone.rotation;

                // Align the root bone
                rootBone.position = Vector3.zero;
                rootBone.rotation = Quaternion.identity;

                // Adjust the model's position to compensate
                model.transform.position = originalPosition;
                model.transform.rotation = originalRotation;
            }
        }

        private void CreatePrefab(GameObject characterSetup)
        {
            // Ensure the prefab directory exists
            Directory.CreateDirectory(prefabPath);

            // Create the prefab
            string prefabName = $"{characterSetup.name}.prefab";
            string fullPath = Path.Combine(prefabPath, prefabName);
            
            PrefabUtility.SaveAsPrefabAsset(characterSetup, fullPath);
            AssetDatabase.Refresh();
        }
    }
} 