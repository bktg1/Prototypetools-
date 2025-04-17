using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public static class MixamoAnimationUtils
{
    public static bool IsLoopable(AnimationClip clip)
    {
        // Get all curves in the animation
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        
        // Check position curves
        EditorCurveBinding[] positionBindings = bindings.Where(b => b.propertyName.Contains("m_LocalPosition")).ToArray();
        if (positionBindings.Length > 0)
        {
            foreach (var binding in positionBindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null && curve.length > 1)
                {
                    float startValue = curve[0].value;
                    float endValue = curve[curve.length - 1].value;
                    if (Mathf.Abs(startValue - endValue) > 0.01f)
                        return false;
                }
            }
        }

        // Check rotation curves
        EditorCurveBinding[] rotationBindings = bindings.Where(b => b.propertyName.Contains("m_LocalRotation")).ToArray();
        if (rotationBindings.Length > 0)
        {
            foreach (var binding in rotationBindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null && curve.length > 1)
                {
                    float startValue = curve[0].value;
                    float endValue = curve[curve.length - 1].value;
                    if (Mathf.Abs(startValue - endValue) > 0.01f)
                        return false;
                }
            }
        }

        return true;
    }

    public static bool HasRootMotion(AnimationClip clip, float threshold = 0.1f)
    {
        // Check root motion curves
        EditorCurveBinding[] rootBindings = AnimationUtility.GetCurveBindings(clip)
            .Where(b => b.path == "" && (b.propertyName.Contains("m_LocalPosition") || b.propertyName.Contains("m_LocalRotation")))
            .ToArray();

        foreach (var binding in rootBindings)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve != null && curve.length > 1)
            {
                float startValue = curve[0].value;
                float endValue = curve[curve.length - 1].value;
                if (Mathf.Abs(startValue - endValue) > threshold)
                    return true;
            }
        }

        return false;
    }

    public static void AddAnimationEvents(AnimationClip clip, bool addFootsteps = true, bool addAttacks = false)
    {
        List<AnimationEvent> events = new List<AnimationEvent>();

        if (addFootsteps)
        {
            // Analyze animation curves to find foot contact points
            EditorCurveBinding[] footBindings = AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.path.Contains("Foot") && b.propertyName.Contains("m_LocalPosition.y"))
                .ToArray();

            foreach (var binding in footBindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null)
                {
                    // Find local minima (foot contact points)
                    for (int i = 1; i < curve.length - 1; i++)
                    {
                        if (curve[i].value < curve[i - 1].value && curve[i].value < curve[i + 1].value)
                        {
                            events.Add(new AnimationEvent
                            {
                                time = curve[i].time,
                                functionName = "OnFootstep",
                                messageOptions = SendMessageOptions.RequireReceiver
                            });
                        }
                    }
                }
            }
        }

        if (addAttacks)
        {
            // Analyze animation curves to find attack points
            EditorCurveBinding[] handBindings = AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.path.Contains("Hand") && b.propertyName.Contains("m_LocalPosition"))
                .ToArray();

            foreach (var binding in handBindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null)
                {
                    // Find rapid position changes (attack motions)
                    for (int i = 1; i < curve.length; i++)
                    {
                        float velocity = Mathf.Abs(curve[i].value - curve[i - 1].value) / (curve[i].time - curve[i - 1].time);
                        if (velocity > 5.0f) // Threshold for attack motion
                        {
                            events.Add(new AnimationEvent
                            {
                                time = curve[i].time,
                                functionName = "OnAttack",
                                messageOptions = SendMessageOptions.RequireReceiver
                            });
                        }
                    }
                }
            }
        }

        AnimationUtility.SetAnimationEvents(clip, events.ToArray());
    }

    public static void OptimizeForMobile(AnimationClip clip, bool bakeBones = true, float quality = 0.8f)
    {
        // Set compression settings
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = IsLoopable(clip);
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        settings.keepOriginalOrientation = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Set compression
        ModelImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as ModelImporter;
        if (importer != null)
        {
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.animationPositionError = 1.0f - quality;
            importer.animationRotationError = 1.0f - quality;
            importer.animationScaleError = 1.0f - quality;
            
            if (bakeBones)
            {
                importer.optimizeGameObjects = true;
                importer.extraExposedTransformPaths = new string[] { };
            }
            
            importer.SaveAndReimport();
        }
    }

    public static TimelineAsset CreateTimelineForAnimations(List<AnimationClip> clips, string timelineName, bool createTransitions = true)
    {
        TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        timeline.name = timelineName;

        // Create animation track
        AnimationTrack animationTrack = timeline.CreateTrack<AnimationTrack>(null, "Animation Track");
        
        float startTime = 0f;
        foreach (var clip in clips)
        {
            TimelineClip timelineClip = animationTrack.CreateClip(clip);
            timelineClip.displayName = clip.name;
            timelineClip.start = startTime;
            timelineClip.duration = clip.length;
            
            if (createTransitions && startTime > 0)
            {
                // Add transition from previous clip
                timelineClip.easeInDuration = 0.25f;
            }
            
            startTime += clip.length;
        }

        // Save the timeline
        string timelinePath = $"Assets/Timelines/{timelineName}.playable";
        if (!Directory.Exists("Assets/Timelines"))
        {
            Directory.CreateDirectory("Assets/Timelines");
        }
        AssetDatabase.CreateAsset(timeline, timelinePath);
        AssetDatabase.SaveAssets();

        return timeline;
    }
} 