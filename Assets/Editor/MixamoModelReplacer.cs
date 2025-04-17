using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using UnityEditor.Animations;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Cinemachine;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine.AI;  // Add this for NavMeshLink

namespace BlackylesMixamoTools
{
    /// <summary>
    /// A professional tool for replacing Mixamo models while preserving animations and script references.
    /// Compatible with Unity 2022.3.61f1 and later.
    /// </summary>
    public class MixamoModelReplacer : EditorWindow
    {
        private const string VERSION = "1.0.5";
        private const string TOOL_NAME = "Mixamo Model Replacer";
        private const string COMPANY_NAME = "Blackyles";
        private const string MIN_UNITY_VERSION = "2022.3.61f1";

        private GameObject targetCharacter;
        private GameObject newMixamoModel;
        private Animator targetAnimator;
        private bool preserveScale = true;
        private Vector3 originalScale;
        private string backupPath = "Assets/CharacterBackups";
        private string lastBackupPath = "";
        private List<string> requiredAnimatorParameters = new List<string>
        {
            "Speed",
            "Jump",
            "Grounded",
            "FreeFall",
            "MotionSpeed"
        };
        private GameObject lastReplacedCharacter;
        private GameObject lastOriginalCharacter;
        private RuntimeAnimatorController lastOriginalController;
        private List<MonoBehaviour> lastUpdatedScripts = new List<MonoBehaviour>();
        private GameObject lastModel;  // Store the actual model reference instead of just the path

        [MenuItem("BKT Suite/Mixamo Model Replacer")]
        public static void ShowWindow()
        {
            GetWindow<MixamoModelReplacer>(TOOL_NAME);
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawMainContent();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{TOOL_NAME} v{VERSION}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"by {COMPANY_NAME}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawMainContent()
        {
            EditorGUILayout.HelpBox("This professional tool helps you replace or create a new Third Person Controller with a Mixamo humanoid model while preserving all animations and script references.", MessageType.Info);
            EditorGUILayout.Space();

            targetCharacter = EditorGUILayout.ObjectField("Current Character", targetCharacter, typeof(GameObject), true) as GameObject;
            newMixamoModel = EditorGUILayout.ObjectField("Mixamo Model", newMixamoModel, typeof(GameObject), true) as GameObject;
            preserveScale = EditorGUILayout.Toggle("Preserve Original Scale", preserveScale);

            EditorGUILayout.Space();

            GUI.enabled = newMixamoModel != null && targetCharacter != null;
            if (GUILayout.Button("Replace Model"))
            {
                if (ValidateInputs())
                {
                    CreateBackup();
                    ReplaceModel();
                }
            }
            GUI.enabled = true;

            EditorGUILayout.Space();

            if (GUILayout.Button("Select from Project Models"))
            {
                ShowModelSelectionWindow();
            }
        }

        private void ShowModelSelectionWindow()
        {
            // Find all possible model types in the project, excluding package assets
            string[] guids = AssetDatabase.FindAssets("t:GameObject t:Prefab t:Model");
            List<GameObject> validModels = new List<GameObject>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                
                // Skip package assets
                if (path.StartsWith("Packages/"))
                {
                    continue;
                }

                try
                {
                    GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    
                    // Skip if model is null or destroyed
                    if (model == null || model.Equals(null))
                    {
                        continue;
                    }

                    // Check if the model has an Animator component and is humanoid
                    var animator = model.GetComponent<Animator>();
                    if (animator != null && !animator.Equals(null) && animator.isHuman)
                    {
                        validModels.Add(model);
                    }
                    else
                    {
                        // Check children for animator components
                        var childAnimators = model.GetComponentsInChildren<Animator>();
                        foreach (var childAnimator in childAnimators)
                        {
                            if (childAnimator != null && !childAnimator.Equals(null) && childAnimator.isHuman)
                            {
                                validModels.Add(childAnimator.gameObject);
                                break;
                            }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    // Skip any assets that cause errors during loading
                    Debug.LogWarning($"Skipping asset {path} due to error: {e.Message}");
                    continue;
                }
            }

            // Remove any null or destroyed models from the list
            validModels.RemoveAll(model => model == null || model.Equals(null));

            if (validModels.Count == 0)
            {
                EditorUtility.DisplayDialog("No Models Found", "No valid humanoid models found in the project. Please ensure you have imported your models into the project first.", "OK");
                return;
            }

            // Create and show the selection window
            ModelSelectionWindow window = ScriptableObject.CreateInstance<ModelSelectionWindow>();
            window.Initialize(validModels, this);
            window.ShowUtility();
        }

        public void OnModelSelected(GameObject selectedModel)
        {
            if (selectedModel == null || targetCharacter == null)
            {
                EditorUtility.DisplayDialog("Error", "Selected model or target character is null!", "OK");
                return;
            }

            try
            {
                // Store camera references before replacement
                var virtualCamera = FindFirstObjectByType<CinemachineVirtualCamera>();
                var originalFollow = virtualCamera?.Follow;
                var originalLookAt = virtualCamera?.LookAt;
                
                // Get the actual model from the scene if it exists
                GameObject modelToUse = selectedModel;
                
                // If the selected model is a prefab, find its instance in the scene
                if (PrefabUtility.GetPrefabAssetType(selectedModel) != PrefabAssetType.NotAPrefab)
                {
                    // Find the first instance of this prefab in the scene
                    var instances = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                    foreach (var instance in instances)
                    {
                        if (instance != null && !instance.Equals(null) && 
                            PrefabUtility.GetCorrespondingObjectFromSource(instance) == selectedModel)
                        {
                            modelToUse = instance;
                            break;
                        }
                    }
                }
                
                // Set the new model and use the exact same replacement method
                newMixamoModel = modelToUse;
                
                // Use the exact same replacement process as the main button
                if (ValidateInputs())
                {
                    CreateBackup();
                    
                    // Store original transform values
                    Vector3 originalPosition = targetCharacter.transform.position;
                    Quaternion originalRotation = targetCharacter.transform.rotation;
                    Vector3 originalScale = targetCharacter.transform.localScale;
                    
                    // Find the old model to replace
                    var oldModel = targetCharacter.GetComponentInChildren<Animator>()?.gameObject;
                    if (oldModel != null)
                    {
                        // Store the old model's parent and sibling index
                        Transform oldParent = oldModel.transform.parent;
                        int oldSiblingIndex = oldModel.transform.GetSiblingIndex();
                        
                        // Remove the old model from hierarchy but don't destroy it yet
                        oldModel.transform.SetParent(null);
                        
                        // Create new model as a child of the original parent
                        GameObject newModel = Instantiate(modelToUse, oldParent);
                        if (newModel != null)
                        {
                            newModel.transform.SetSiblingIndex(oldSiblingIndex);
                            newModel.name = modelToUse.name;
                            
                            // Set the new model as the target for replacement
                            newMixamoModel = newModel;
                            
                            // Perform the replacement
                            ReplaceModel();
                            
                            // Now we can safely destroy the old model instance
                            if (oldModel != null && !oldModel.Equals(null))
                            {
                                DestroyImmediate(oldModel);
                            }
                            
                            // Restore transform values
                            if (targetCharacter != null && !targetCharacter.Equals(null))
                            {
                                targetCharacter.transform.position = originalPosition;
                                targetCharacter.transform.rotation = originalRotation;
                                targetCharacter.transform.localScale = originalScale;
                            }
                            
                            // Restore camera references
                            if (virtualCamera != null && !virtualCamera.Equals(null))
                            {
                                var newCameraTarget = lastReplacedCharacter?.transform.Find("PlayerCameraRoot");
                                if (newCameraTarget != null)
                                {
                                    virtualCamera.Follow = newCameraTarget;
                                    virtualCamera.LookAt = newCameraTarget;
                                }
                                else if (lastReplacedCharacter != null && !lastReplacedCharacter.Equals(null))
                                {
                                    virtualCamera.Follow = lastReplacedCharacter.transform;
                                    virtualCamera.LookAt = lastReplacedCharacter.transform;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Restore the original model if validation fails
                    newMixamoModel = null;
                    
                    // Restore original camera references
                    if (virtualCamera != null && !virtualCamera.Equals(null))
                    {
                        virtualCamera.Follow = originalFollow;
                        virtualCamera.LookAt = originalLookAt;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in OnModelSelected: {e.Message}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
            }
        }

        private void CreateBackup()
        {
            try
            {
                // Ensure the backup directory exists
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                    AssetDatabase.Refresh();
                }

                // Clean up old backups, keeping only the last two
                var backupFolders = Directory.GetDirectories(backupPath)
                    .OrderByDescending(d => d)
                    .ToList();
                
                for (int i = 2; i < backupFolders.Count; i++)
                {
                    string metaPath = backupFolders[i] + ".meta";
                    if (File.Exists(metaPath))
                    {
                        File.Delete(metaPath);
                    }
                    Directory.Delete(backupFolders[i], true);
                }

                string backupName = $"{targetCharacter.name}_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                string backupFolder = Path.Combine(backupPath, backupName);
                
                // Ensure the backup folder exists
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                    AssetDatabase.Refresh();
                }
                
                GameObject backupCharacter = Object.Instantiate(targetCharacter);
                backupCharacter.name = $"{targetCharacter.name}_Backup";
                
                if (PrefabUtility.IsPartOfAnyPrefab(backupCharacter))
                {
                    PrefabUtility.UnpackPrefabInstance(backupCharacter, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                
                string prefabPath = Path.Combine(backupFolder, $"{backupName}.prefab");
                PrefabUtility.SaveAsPrefabAsset(backupCharacter, prefabPath);
                
                lastBackupPath = prefabPath;
                
                Object.DestroyImmediate(backupCharacter);
                AssetDatabase.Refresh();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error creating backup: {e.Message}\n{e.StackTrace}");
            }
        }

        private void CreateNewCharacter()
        {
            if (newMixamoModel == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a Mixamo model.", "OK");
                return;
            }

            // Create the main character container
            GameObject characterContainer = new GameObject(newMixamoModel.name + "_Controller");
            Undo.RegisterCreatedObjectUndo(characterContainer, "Create Character Container");

            // Add required components to the container
            var characterController = characterContainer.AddComponent<CharacterController>();
            characterController.center = new Vector3(0, 1f, 0);
            characterController.radius = 0.3f;
            characterController.height = 1.8f;

            var playerInput = characterContainer.AddComponent<PlayerInput>();
            var animator = characterContainer.AddComponent<Animator>();

            // Instantiate the Mixamo model as a child
            GameObject modelInstance = Instantiate(newMixamoModel, characterContainer.transform);
            modelInstance.name = newMixamoModel.name;
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;

            // Copy the animator avatar from the model
            Animator modelAnimator = modelInstance.GetComponent<Animator>();
            if (modelAnimator != null)
            {
                animator.avatar = modelAnimator.avatar;
                DestroyImmediate(modelAnimator);
            }

            // Try to find and assign the default animator controller
            string[] guids = AssetDatabase.FindAssets("t:AnimatorController ThirdPerson");
            if (guids.Length > 0)
            {
                string controllerPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            }

            // Add camera setup
            CreateCameraSetup(characterContainer);

            // Add scripts
            string[] scriptGuids = AssetDatabase.FindAssets("ThirdPersonController t:Script");
            if (scriptGuids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuids[0]);
                MonoScript controllerScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (controllerScript != null)
                {
                    characterContainer.AddComponent(controllerScript.GetClass());
                }
            }

            // Position the character properly in the scene
            characterContainer.transform.position = new Vector3(0, 0, 0);

            // Select the new character in the hierarchy
            Selection.activeGameObject = characterContainer;

            EditorUtility.DisplayDialog("Success", "New character created successfully!", "OK");
        }

        private void CreateCameraSetup(GameObject character)
        {
            // Create camera target
            GameObject cameraTarget = new GameObject("PlayerCameraRoot");
            cameraTarget.transform.SetParent(character.transform);
            cameraTarget.transform.localPosition = new Vector3(0, 1.375f, 0);
            cameraTarget.transform.localRotation = Quaternion.identity;

            // Create camera setup if Cinemachine is available
            GameObject vcam = new GameObject("PlayerFollowCamera");
            var virtualCamera = vcam.AddComponent<CinemachineVirtualCamera>();
            virtualCamera.Follow = cameraTarget.transform;
            virtualCamera.LookAt = cameraTarget.transform;

            // Set up Cinemachine components
            var composer = virtualCamera.GetCinemachineComponent<CinemachineComposer>();
            if (composer == null)
                composer = virtualCamera.AddCinemachineComponent<CinemachineComposer>();

            var transposer = virtualCamera.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer == null)
                transposer = virtualCamera.AddCinemachineComponent<CinemachineTransposer>();

            // Configure camera position and settings
            transposer.m_FollowOffset = new Vector3(0, 1.375f, -4);
            composer.m_TrackedObjectOffset = new Vector3(0, 0.5f, 0);
        }

        private bool ValidateInputs()
        {
            if (targetCharacter == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select the current character object.", "OK");
                return false;
            }

            if (newMixamoModel == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a Mixamo model to replace with.", "OK");
                return false;
            }

            // Check if the new model has a valid humanoid avatar
            var newModelAnimator = newMixamoModel.GetComponent<Animator>();
            if (newModelAnimator == null || !newModelAnimator.isHuman)
            {
                EditorUtility.DisplayDialog("Error", "New model must be a humanoid model with an Animator component.", "OK");
                return false;
            }

            // Validate the new model's avatar configuration
            if (!ValidateAvatarConfiguration(newModelAnimator))
            {
                EditorUtility.DisplayDialog("Error", "New model's avatar configuration is invalid. Please ensure it has all required humanoid bones.", "OK");
                return false;
            }

            targetAnimator = targetCharacter.GetComponent<Animator>();
            if (targetAnimator == null)
            {
                EditorUtility.DisplayDialog("Error", "Target character must have an Animator component.", "OK");
                return false;
            }

            return true;
        }

        private bool ValidateAvatarConfiguration(Animator animator)
        {
            if (animator == null || !animator.isHuman || animator.avatar == null)
                return false;

            var avatar = animator.avatar;
            if (!avatar.isValid || !avatar.isHuman)
                return false;

            // Check for required humanoid bones
            var humanBones = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
                HumanBodyBones.UpperChest,
                HumanBodyBones.Neck,
                HumanBodyBones.Head,
                HumanBodyBones.LeftShoulder,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                HumanBodyBones.RightShoulder,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot
            };

            foreach (var bone in humanBones)
            {
                if (animator.GetBoneTransform(bone) == null)
                {
                    Debug.LogError($"Missing required bone: {bone}");
                    return false;
                }
            }

            return true;
        }

        private void UndoLastReplacement()
        {
            if (lastReplacedCharacter == null || lastOriginalCharacter == null)
            {
                EditorUtility.DisplayDialog("Error", "No previous replacement to undo!", "OK");
                return;
            }

            try
            {
                // Store the current character's transform values
                Vector3 currentPosition = lastReplacedCharacter.transform.position;
                Quaternion currentRotation = lastReplacedCharacter.transform.rotation;
                Vector3 currentScale = lastReplacedCharacter.transform.localScale;

                // Destroy the replaced character
                DestroyImmediate(lastReplacedCharacter);

                // Load the backup prefab
                GameObject backupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(lastBackupPath);
                if (backupPrefab == null)
                {
                    EditorUtility.DisplayDialog("Error", "Could not load the backup prefab!", "OK");
                    return;
                }

                // Create a new instance of the backup
                GameObject restoredCharacter = PrefabUtility.InstantiatePrefab(backupPrefab) as GameObject;
                if (restoredCharacter == null)
                {
                    EditorUtility.DisplayDialog("Error", "Could not instantiate the backup prefab!", "OK");
                    return;
                }

                // Position the restored character where the current one was
                restoredCharacter.transform.position = currentPosition;
                restoredCharacter.transform.rotation = currentRotation;
                restoredCharacter.transform.localScale = currentScale;

                // Restore the original animator controller
                var restoredAnimator = restoredCharacter.GetComponent<Animator>();
                if (restoredAnimator != null && lastOriginalController != null)
                {
                    restoredAnimator.runtimeAnimatorController = lastOriginalController;
                }

                // Restore script references
                foreach (var script in lastUpdatedScripts)
                {
                    if (script != null)
                    {
                        var fields = script.GetType().GetFields(System.Reflection.BindingFlags.Instance | 
                                                              System.Reflection.BindingFlags.Public | 
                                                              System.Reflection.BindingFlags.NonPublic);
                        
                        foreach (var field in fields)
                        {
                            if (field.FieldType == typeof(GameObject) || 
                                field.FieldType == typeof(Transform) || 
                                field.FieldType == typeof(Animator))
                            {
                                var value = field.GetValue(script);
                                if (value != null && value.ToString().Contains(lastReplacedCharacter.name))
                                {
                                    if (field.FieldType == typeof(GameObject))
                                    {
                                        field.SetValue(script, restoredCharacter);
                                    }
                                    else if (field.FieldType == typeof(Transform))
                                    {
                                        field.SetValue(script, restoredCharacter.transform);
                                    }
                                    else if (field.FieldType == typeof(Animator))
                                    {
                                        field.SetValue(script, restoredCharacter.GetComponent<Animator>());
                                    }
                                }
                            }
                        }
                    }
                }

                // Select the restored character in the hierarchy
                Selection.activeGameObject = restoredCharacter;

                // Clear the undo history
                lastReplacedCharacter = null;
                lastOriginalCharacter = null;
                lastOriginalController = null;
                lastUpdatedScripts.Clear();

                EditorUtility.DisplayDialog("Success", "Last replacement has been undone and original character restored!", "OK");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"An error occurred while undoing: {e.Message}\n\nPlease check the console for more details.", "OK");
                Debug.LogError($"Error in MixamoModelReplacer Undo: {e.Message}\n{e.StackTrace}");
            }
        }

        private void UpdateScriptReferences(GameObject newCharacter)
        {
            lastUpdatedScripts.Clear();
            
            // Find all scripts in the scene
            var allScripts = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            foreach (var script in allScripts)
            {
                if (script == null) continue;

                bool wasUpdated = false;
                var fields = script.GetType().GetFields(System.Reflection.BindingFlags.Instance | 
                                                      System.Reflection.BindingFlags.Public | 
                                                      System.Reflection.BindingFlags.NonPublic);
                
                foreach (var field in fields)
                {
                    if (field.FieldType == typeof(GameObject) || 
                        field.FieldType == typeof(Transform) || 
                        field.FieldType == typeof(Animator))
                    {
                        var value = field.GetValue(script);
                        if (value != null && value.ToString().Contains(targetCharacter.name))
                        {
                            // Update the reference to the new character
                            if (field.FieldType == typeof(GameObject))
                            {
                                field.SetValue(script, newCharacter);
                            }
                            else if (field.FieldType == typeof(Transform))
                            {
                                field.SetValue(script, newCharacter.transform);
                            }
                            else if (field.FieldType == typeof(Animator))
                            {
                                field.SetValue(script, newCharacter.GetComponent<Animator>());
                            }
                            wasUpdated = true;
                        }
                    }
                }

                if (wasUpdated)
                {
                    lastUpdatedScripts.Add(script);
                }
            }
        }

        private void ReplaceModel()
        {
            try
            {
                // Clean up any existing NavMeshLinks first
                CleanupNavMeshLinks();

                // Check if we're trying to modify a package asset
                if (AssetDatabase.GetAssetPath(targetCharacter).StartsWith("Packages/"))
                {
                    EditorUtility.DisplayDialog("Error", "Cannot modify package assets directly. Please create a copy of the prefab in your project first.", "OK");
                    return;
                }

                // Store the original character and controller for undo
                lastOriginalCharacter = targetCharacter;
                lastOriginalController = targetCharacter.GetComponent<Animator>()?.runtimeAnimatorController;

                // Create a backup before making any changes
                CreateBackup();

                // Store original transform values
                Vector3 originalPosition = targetCharacter.transform.position;
                Quaternion originalRotation = targetCharacter.transform.rotation;
                originalScale = targetCharacter.transform.localScale;

                // Store NavMeshAgent settings if they exist
                var originalNavMeshAgent = targetCharacter.GetComponent<UnityEngine.AI.NavMeshAgent>();
                float originalSpeed = 0f;
                float originalAngularSpeed = 0f;
                float originalAcceleration = 0f;
                float originalStoppingDistance = 0f;
                bool originalAutoBraking = false;
                bool originalAutoRepath = false;
                UnityEngine.AI.NavMeshAgent originalAgent = null;

                if (originalNavMeshAgent != null)
                {
                    originalSpeed = originalNavMeshAgent.speed;
                    originalAngularSpeed = originalNavMeshAgent.angularSpeed;
                    originalAcceleration = originalNavMeshAgent.acceleration;
                    originalStoppingDistance = originalNavMeshAgent.stoppingDistance;
                    originalAutoBraking = originalNavMeshAgent.autoBraking;
                    originalAutoRepath = originalNavMeshAgent.autoRepath;
                    originalAgent = originalNavMeshAgent;
                }

                // Check if AI Navigation package is available and not in a package
                bool hasAINavigation = false;
                try
                {
                    var navMeshLinkType = System.Type.GetType("UnityEngine.AI.NavMeshLink, UnityEngine.AIModule");
                    hasAINavigation = navMeshLinkType != null;
                }
                catch
                {
                    hasAINavigation = false;
                }

                // Store NavMeshLink settings if they exist and package is available
                if (hasAINavigation)
                {
                    try
                    {
                        var navMeshLinkType = System.Type.GetType("UnityEngine.AI.NavMeshLink, UnityEngine.AIModule");
                        var originalNavMeshLinks = targetCharacter.GetComponentsInChildren(navMeshLinkType, true);
                        
                        // Clear any existing links
                        originalLinks.Clear();
                        
                        foreach (var link in originalNavMeshLinks)
                        {
                            // Skip if the link is in a package
                            if (AssetDatabase.GetAssetPath(link.gameObject).StartsWith("Packages/"))
                            {
                                continue;
                            }

                            var startPoint = (Vector3)link.GetType().GetProperty("startPoint").GetValue(link);
                            var endPoint = (Vector3)link.GetType().GetProperty("endPoint").GetValue(link);
                            var width = (float)link.GetType().GetProperty("width").GetValue(link);
                            var costModifier = (float)link.GetType().GetProperty("costModifier").GetValue(link);
                            var bidirectional = (bool)link.GetType().GetProperty("bidirectional").GetValue(link);
                            var autoUpdatePosition = (bool)link.GetType().GetProperty("autoUpdatePosition").GetValue(link);
                            var area = (int)link.GetType().GetProperty("area").GetValue(link);

                            // Store the link data
                            originalLinks.Add(new NavMeshLinkData
                            {
                                startPoint = startPoint,
                                endPoint = endPoint,
                                width = width,
                                costModifier = costModifier,
                                bidirectional = bidirectional,
                                autoUpdatePosition = autoUpdatePosition,
                                area = area
                            });
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Could not process NavMeshLink components: {e.Message}");
                    }
                }

                // Unpack the root prefab instance if it is one
                if (PrefabUtility.IsPartOfAnyPrefab(targetCharacter))
                {
                    PrefabUtility.UnpackPrefabInstance(targetCharacter, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                // Find the model to replace
                GameObject oldModel = null;
                var skinnedMeshes = targetCharacter.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skinnedMeshes.Length > 0)
                {
                    oldModel = skinnedMeshes[0].gameObject;
                }

                if (oldModel == null)
                {
                    EditorUtility.DisplayDialog("Error", "Could not find the original model to replace!", "OK");
                    return;
                }

                // Store the original animator controller and animations
                var originalAnimator = targetCharacter.GetComponent<Animator>();
                if (originalAnimator == null)
                {
                    EditorUtility.DisplayDialog("Error", "Original character must have an Animator component!", "OK");
                    return;
                }

                RuntimeAnimatorController originalController = originalAnimator.runtimeAnimatorController;
                Avatar originalAvatar = originalAnimator.avatar;
                bool originalApplyRootMotion = originalAnimator.applyRootMotion;
                bool originalUpdateMode = originalAnimator.updateMode == AnimatorUpdateMode.Fixed;
                float originalAnimatorSpeed = originalAnimator.speed;

                // Create new model instance
                GameObject newModel = Instantiate(newMixamoModel, targetCharacter.transform);
                newModel.name = newMixamoModel.name;

                // Remove the old model
                DestroyImmediate(oldModel);

                // Setup the new model's animator
                var newModelAnimator = newModel.GetComponent<Animator>();
                if (newModelAnimator == null)
                {
                    newModelAnimator = newModel.AddComponent<Animator>();
                }

                // Configure the new model's avatar
                if (newModelAnimator.avatar == null || !newModelAnimator.avatar.isValid || !newModelAnimator.avatar.isHuman)
                {
                    // Create a new avatar using the original avatar's human description
                    var newAvatar = AvatarBuilder.BuildHumanAvatar(newModel, originalAvatar.humanDescription);
                    if (newAvatar != null)
                    {
                        newModelAnimator.avatar = newAvatar;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", "Failed to create valid avatar for the new model!", "OK");
                        return;
                    }
                }

                // Restore NavMeshAgent settings if they existed
                if (originalAgent != null)
                {
                    var newNavMeshAgent = newModel.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (newNavMeshAgent == null)
                    {
                        newNavMeshAgent = newModel.AddComponent<UnityEngine.AI.NavMeshAgent>();
                    }

                    newNavMeshAgent.speed = originalSpeed;
                    newNavMeshAgent.angularSpeed = originalAngularSpeed;
                    newNavMeshAgent.acceleration = originalAcceleration;
                    newNavMeshAgent.stoppingDistance = originalStoppingDistance;
                    newNavMeshAgent.autoBraking = originalAutoBraking;
                    newNavMeshAgent.autoRepath = originalAutoRepath;
                }

                // Restore NavMeshLink components if package is available
                if (hasAINavigation)
                {
                    try
                    {
                        var navMeshLinkType = System.Type.GetType("UnityEngine.AI.NavMeshLink, UnityEngine.AIModule");
                        foreach (var linkData in originalLinks)
                        {
                            var newLink = newModel.AddComponent(navMeshLinkType);
                            newLink.GetType().GetProperty("startPoint").SetValue(newLink, linkData.startPoint);
                            newLink.GetType().GetProperty("endPoint").SetValue(newLink, linkData.endPoint);
                            newLink.GetType().GetProperty("width").SetValue(newLink, linkData.width);
                            newLink.GetType().GetProperty("costModifier").SetValue(newLink, linkData.costModifier);
                            newLink.GetType().GetProperty("bidirectional").SetValue(newLink, linkData.bidirectional);
                            newLink.GetType().GetProperty("autoUpdatePosition").SetValue(newLink, linkData.autoUpdatePosition);
                            newLink.GetType().GetProperty("area").SetValue(newLink, linkData.area);
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Could not restore NavMeshLink components: {e.Message}");
                    }
                }

                // Create a new animator controller based on the original
                if (originalController != null)
                {
                    // Create a new animator controller
                    var newController = new AnimatorController();
                    newController.name = $"{originalController.name}_New";

                    // If the original controller is an AnimatorController, copy its layers and parameters
                    if (originalController is AnimatorController originalAnimatorController)
                    {
                        // Copy all layers from the original controller
                        foreach (var layer in originalAnimatorController.layers)
                        {
                            newController.AddLayer(layer.name);
                            var newLayer = newController.layers[newController.layers.Length - 1];
                            newLayer.defaultWeight = layer.defaultWeight;
                            newLayer.syncedLayerIndex = layer.syncedLayerIndex;
                            newLayer.syncedLayerAffectsTiming = layer.syncedLayerAffectsTiming;
                            newLayer.iKPass = layer.iKPass;

                            // Deep copy the state machine
                            var originalStateMachine = layer.stateMachine;
                            var newStateMachine = newLayer.stateMachine;

                            // Copy states and their motions
                            foreach (var state in originalStateMachine.states)
                            {
                                var newState = newStateMachine.AddState(state.state.name);
                                newState.motion = state.state.motion;
                                newState.speed = state.state.speed;
                                newState.mirror = state.state.mirror;
                                newState.timeParameterActive = state.state.timeParameterActive;
                                newState.timeParameter = state.state.timeParameter;
                                newState.cycleOffsetParameter = state.state.cycleOffsetParameter;
                                newState.mirrorParameter = state.state.mirrorParameter;
                            }

                            // Copy transitions
                            foreach (var transition in originalStateMachine.anyStateTransitions)
                            {
                                var newTransition = newStateMachine.AddAnyStateTransition(
                                    newStateMachine.states.First(s => s.state.name == transition.destinationState.name).state
                                );
                                CopyTransitionSettings(transition, newTransition);
                            }

                            foreach (var state in originalStateMachine.states)
                            {
                                var newState = newStateMachine.states.First(s => s.state.name == state.state.name).state;
                                foreach (var transition in state.state.transitions)
                                {
                                    var newTransition = newState.AddTransition(
                                        newStateMachine.states.First(s => s.state.name == transition.destinationState.name).state
                                    );
                                    CopyTransitionSettings(transition, newTransition);
                                }
                            }
                        }

                        // Copy all parameters
                        foreach (var param in originalAnimatorController.parameters)
                        {
                            newController.AddParameter(param);
                        }
                    }

                    // Set the new controller
                    newModelAnimator.runtimeAnimatorController = newController;

                    // If the original controller is an override controller, handle the overrides
                    if (originalController is AnimatorOverrideController originalOverride)
                    {
                        var newOverride = new AnimatorOverrideController(newController);
                        newOverride.name = $"{originalOverride.name}_Override";

                        // Copy all animation overrides
                        var clips = originalOverride.animationClips;
                        foreach (var clip in clips)
                        {
                            if (clip != null)
                            {
                                newOverride[clip.name] = clip;
                            }
                        }

                        newModelAnimator.runtimeAnimatorController = newOverride;
                    }
                }

                // Set other animator properties
                newModelAnimator.avatar = originalAvatar;
                newModelAnimator.applyRootMotion = originalApplyRootMotion;
                newModelAnimator.updateMode = originalUpdateMode ? AnimatorUpdateMode.Fixed : AnimatorUpdateMode.Normal;
                newModelAnimator.speed = originalAnimatorSpeed;

                // Copy all animator parameters values
                var parameters = originalAnimator.parameters;
                foreach (var param in parameters)
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            newModelAnimator.SetFloat(param.name, originalAnimator.GetFloat(param.name));
                            break;
                        case AnimatorControllerParameterType.Int:
                            newModelAnimator.SetInteger(param.name, originalAnimator.GetInteger(param.name));
                            break;
                        case AnimatorControllerParameterType.Bool:
                            newModelAnimator.SetBool(param.name, originalAnimator.GetBool(param.name));
                            break;
                        case AnimatorControllerParameterType.Trigger:
                            if (originalAnimator.GetBool(param.name))
                            {
                                newModelAnimator.SetTrigger(param.name);
                            }
                            break;
                    }
                }

                // Reset transform
                newModel.transform.localPosition = Vector3.zero;
                newModel.transform.localRotation = Quaternion.identity;
                
                if (preserveScale)
                {
                    targetCharacter.transform.localScale = originalScale;
                }

                // Restore original transform values
                targetCharacter.transform.position = originalPosition;
                targetCharacter.transform.rotation = originalRotation;

                // After creating the new model, update script references
                UpdateScriptReferences(newModel);

                // Store the new character for undo
                lastReplacedCharacter = newModel;

                // Clean up any old NavMeshLinks
                CleanupNavMeshLinks();

                EditorUtility.DisplayDialog("Success", "Model replaced successfully!", "OK");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}\n\nPlease check the console for more details.", "OK");
                Debug.LogError($"Error in MixamoModelReplacer: {e.Message}\n{e.StackTrace}");
            }
        }

        private void RevertToOriginal()
        {
            try
            {
                if (string.IsNullOrEmpty(lastBackupPath) || !File.Exists(lastBackupPath))
                {
                    EditorUtility.DisplayDialog("Error", "No backup found to revert to!", "OK");
                    return;
                }

                // Load the backup prefab
                GameObject backupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(lastBackupPath);
                if (backupPrefab == null)
                {
                    EditorUtility.DisplayDialog("Error", "Could not load the backup prefab!", "OK");
                    return;
                }

                // Create a new instance of the backup
                GameObject restoredCharacter = PrefabUtility.InstantiatePrefab(backupPrefab) as GameObject;
                if (restoredCharacter == null)
                {
                    EditorUtility.DisplayDialog("Error", "Could not instantiate the backup prefab!", "OK");
                    return;
                }

                // Position the restored character where the current one is
                restoredCharacter.transform.position = targetCharacter.transform.position;
                restoredCharacter.transform.rotation = targetCharacter.transform.rotation;
                restoredCharacter.transform.localScale = targetCharacter.transform.localScale;

                // Destroy the current character
                DestroyImmediate(targetCharacter);

                // Select the restored character
                Selection.activeGameObject = restoredCharacter;

                EditorUtility.DisplayDialog("Success", "Character restored to original state!", "OK");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"An error occurred while reverting: {e.Message}\n\nPlease check the console for more details.", "OK");
                Debug.LogError($"Error in MixamoModelReplacer Revert: {e.Message}\n{e.StackTrace}");
            }
        }

        private bool IsPartOfModel(GameObject obj)
        {
            return obj.GetComponent<SkinnedMeshRenderer>() != null || 
                   obj.GetComponent<MeshRenderer>() != null ||
                   obj.GetComponent<MeshFilter>() != null;
        }

        private void CopyTransitionSettings(AnimatorStateTransition source, AnimatorStateTransition destination)
        {
            destination.duration = source.duration;
            destination.offset = source.offset;
            destination.hasExitTime = source.hasExitTime;
            destination.exitTime = source.exitTime;
            destination.hasFixedDuration = source.hasFixedDuration;
            destination.interruptionSource = source.interruptionSource;
            destination.orderedInterruption = source.orderedInterruption;
            destination.canTransitionToSelf = source.canTransitionToSelf;
            destination.conditions = source.conditions;
        }

        // Helper class to store NavMeshLink data
        private class NavMeshLinkData
        {
            public Vector3 startPoint;
            public Vector3 endPoint;
            public float width;
            public float costModifier;
            public bool bidirectional;
            public bool autoUpdatePosition;
            public int area;
        }

        private List<NavMeshLinkData> originalLinks = new List<NavMeshLinkData>();

        private void OnDestroy()
        {
            // Clean up any NavMeshLinks we created
            CleanupNavMeshLinks();
        }

        private void CleanupNavMeshLinks()
        {
            if (lastReplacedCharacter != null)
            {
                try
                {
                    var navMeshLinkType = System.Type.GetType("UnityEngine.AI.NavMeshLink, UnityEngine.AIModule");
                    if (navMeshLinkType != null)
                    {
                        var links = lastReplacedCharacter.GetComponentsInChildren(navMeshLinkType, true);
                        foreach (var link in links)
                        {
                            // Get the NavMeshLink's instance ID
                            var instanceID = link.GetInstanceID();
                            
                            // Try to remove the link using NavMesh.RemoveLink
                            var navMeshType = System.Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
                            if (navMeshType != null)
                            {
                                var removeLinkMethod = navMeshType.GetMethod("RemoveLink", 
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                if (removeLinkMethod != null)
                                {
                                    removeLinkMethod.Invoke(null, new object[] { instanceID });
                                }
                            }
                            
                            // Destroy the component
                            DestroyImmediate(link);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error cleaning up NavMeshLinks: {e.Message}");
                }
            }
        }
    }

    public class ModelSelectionWindow : EditorWindow
    {
        private List<GameObject> models;
        private MixamoModelReplacer replacer;
        private Vector2 scrollPosition;
        private string searchFilter = "";

        public void Initialize(List<GameObject> validModels, MixamoModelReplacer modelReplacer)
        {
            // Filter out any null or destroyed models
            models = validModels.Where(model => model != null && !model.Equals(null)).ToList();
            replacer = modelReplacer;
            minSize = new Vector2(400, 500);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Select a Model to Replace With", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Search filter
            EditorGUI.BeginChangeCheck();
            searchFilter = EditorGUILayout.TextField("Search:", searchFilter);
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            // Filter out any null or destroyed models before displaying
            var validModels = models.Where(model => model != null && !model.Equals(null)).ToList();
            
            foreach (var model in validModels)
            {
                if (string.IsNullOrEmpty(searchFilter) || 
                    model.name.ToLower().Contains(searchFilter.ToLower()))
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(model.name, EditorStyles.boldLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                    {
                        replacer.OnModelSelected(model);
                        Close();
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    // Show the model's path
                    string path = AssetDatabase.GetAssetPath(model);
                    EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                    EditorGUILayout.Space();
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
        }
    }

    /// <summary>
    /// Helper class for managing backups and undo operations
    /// </summary>
    public class MixamoBackupManager
    {
        private const string BACKUP_FOLDER = "Assets/Editor/CharacterBackups";
        private string lastBackupPath = "";

        public void CreateBackup(GameObject targetCharacter)
        {
            try
            {
                if (!Directory.Exists(BACKUP_FOLDER))
                {
                    Directory.CreateDirectory(BACKUP_FOLDER);
                }

                string backupName = $"{targetCharacter.name}_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                string backupFolder = Path.Combine(BACKUP_FOLDER, backupName);
                Directory.CreateDirectory(backupFolder);

                GameObject backupCharacter = Object.Instantiate(targetCharacter);
                backupCharacter.name = $"{targetCharacter.name}_Backup";
                
                if (PrefabUtility.IsPartOfAnyPrefab(backupCharacter))
                {
                    PrefabUtility.UnpackPrefabInstance(backupCharacter, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                
                string prefabPath = Path.Combine(backupFolder, $"{backupName}.prefab");
                PrefabUtility.SaveAsPrefabAsset(backupCharacter, prefabPath);
                
                lastBackupPath = prefabPath;
                
                Object.DestroyImmediate(backupCharacter);
                AssetDatabase.Refresh();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error creating backup: {e.Message}\n{e.StackTrace}");
            }
        }

        public string GetLastBackupPath()
        {
            return lastBackupPath;
        }
    }

    /// <summary>
    /// Helper class for managing avatar configuration and validation
    /// </summary>
    public class MixamoAvatarManager
    {
        public bool ValidateAvatarConfiguration(Animator animator)
        {
            if (animator == null || !animator.isHuman || animator.avatar == null)
                return false;

            var avatar = animator.avatar;
            if (!avatar.isValid || !avatar.isHuman)
                return false;

            var humanBones = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
                HumanBodyBones.UpperChest,
                HumanBodyBones.Neck,
                HumanBodyBones.Head,
                HumanBodyBones.LeftShoulder,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                HumanBodyBones.RightShoulder,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot
            };

            foreach (var bone in humanBones)
            {
                if (animator.GetBoneTransform(bone) == null)
                {
                    Debug.LogError($"Missing required bone: {bone}");
                    return false;
                }
            }

            return true;
        }

        public Avatar ConfigureAvatar(GameObject model)
        {
            var animator = model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
                return null;

            if (animator.avatar.isValid && animator.avatar.isHuman)
                return animator.avatar;

            var newAvatar = AvatarBuilder.BuildHumanAvatar(model, animator.avatar.humanDescription);
            if (newAvatar != null)
            {
                animator.avatar = newAvatar;
                return newAvatar;
            }

            return null;
        }
    }
} 