using FinalFantasy16Library.Files.SKL;
using FinalFantasy16Library.Utils;
using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;
using IONET;
using IONET.Core.Animation;
using IONET.Core.Skeleton;
using System.Numerics;

namespace FinalFantasy16Library.Files.ANMB;

/// <summary>
/// Handles Havok animation related operations.
/// </summary>
public class AnimationUtils
{
    /// <summary>
    /// Imports animation data with a given Havok skeleton and a file path.
    /// Supported input (animation source file): .glb, .gltf
    /// </summary>
    public void Import(SklFile sklFile, string path, ProgressTracker progress = null)
    {
        progress?.SetProgress(10, "Loading animation file");

        var scene = IOManager.LoadScene(path, new ImportSettings());
        var animation = scene.Animations[0];
        var sourceSkeleton = scene.Models[0].Skeleton;
        var targetSkeleton = sklFile.m_Skeleton;

        int numTargetTracks = targetSkeleton.m_bones.Count;
        int frameCount = (int)animation.GetFrameCount();
        // Set as 24 because IONET also does it when importing
        float duration = (frameCount - 1) / 24.0f;

        progress?.SetProgress(30, "Binding bones");

        // Bone binding between skeletons
        SortedDictionary<int, int> boundMap = new();
        for (int track = 0; track < numTargetTracks; ++track)
        {
            string boneName = targetSkeleton.m_bones[track].m_name;

            if (boneName == "skeleton")
                continue;

            var currentBone = sourceSkeleton.GetBoneByName(boneName);

            int nodeId = sourceSkeleton.IndexOf(currentBone);
            if (nodeId != -1)
            {
                boundMap[track] = nodeId;
            }
        }

        int validTrackCount = boundMap.Count;
        Console.WriteLine($"Bound {validTrackCount} bones.");

        progress?.SetProgress(50, "Setting up ANMB");

        int totalTransforms = validTrackCount * frameCount;
        var transforms = new hkQsTransform[totalTransforms];
        for (int i = 0; i < totalTransforms; i++)
        {
            transforms[i] = new hkQsTransform();
        }

        hkaInterleavedUncompressedAnimation newAnimation = new()
        {
            m_duration = duration,
            m_type = hkaAnimation.AnimationType.HK_INTERLEAVED_ANIMATION,
            m_numberOfTransformTracks = validTrackCount,
            m_transforms = new(transforms),
            m_annotationTracks = [],
            m_numberOfFloatTracks = 0,
            m_floats = []
        };

        hkaAnimationBinding newBinding = new()
        {
            m_animation = newAnimation,
            m_originalSkeletonName = targetSkeleton.m_name,
            m_transformTrackToBoneIndices = new(new short[validTrackCount])
        };

        progress?.SetProgress(70, "Converting animation");

        int currentTrack = 0;

        foreach (var trackInfo in boundMap)
        {
            newBinding.m_transformTrackToBoneIndices[currentTrack] = (short)trackInfo.Key;
            IOBone sourceBone = sourceSkeleton.GetBoneByIndex(trackInfo.Value);
            IOAnimation? group = animation.Groups.FirstOrDefault(g => g.Name == sourceBone.Name);

            if (group != null)
            {
                // Ref pose (target skeleton) as fallback
                Vector4 translation = targetSkeleton.m_referencePose[trackInfo.Key].m_translation;
                Quaternion rotation = targetSkeleton.m_referencePose[trackInfo.Key].m_rotation;
                Vector4 scale = targetSkeleton.m_referencePose[trackInfo.Key].m_scale;

                // Getting transform for current bone at each frame
                for (int frame = 0; frame < frameCount; ++frame)
                {
                    translation = new(
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.PositionX)?.GetFrameValue(frame) ?? translation.X,
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.PositionY)?.GetFrameValue(frame) ?? translation.Y,
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.PositionZ)?.GetFrameValue(frame) ?? translation.Z,
                        0
                    );

                    rotation = new(
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.QuatX)?.GetFrameValue(frame) ?? rotation.X,
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.QuatY)?.GetFrameValue(frame) ?? rotation.Y,
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.QuatZ)?.GetFrameValue(frame) ?? rotation.Z,
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.QuatW)?.GetFrameValue(frame) ?? rotation.W
                    );

                    scale = new(
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.ScaleX)?.GetFrameValue(frame) ?? scale.X,
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.ScaleY)?.GetFrameValue(frame) ?? scale.Y,
                        group.Tracks.FirstOrDefault(t => t.ChannelType == IOAnimationTrackType.ScaleZ)?.GetFrameValue(frame) ?? scale.Z,
                        1
                    );

                    newAnimation.m_transforms[frame * validTrackCount + currentTrack].m_translation = CleanTransform(translation);
                    newAnimation.m_transforms[frame * validTrackCount + currentTrack].m_rotation = CleanTransform(rotation);
                    newAnimation.m_transforms[frame * validTrackCount + currentTrack].m_scale = CleanTransform(scale);
                }
            }

            currentTrack++;
        }

        Console.WriteLine($"Transfered transform values from {boundMap.Count} bones across {frameCount - 1} frames.");
        progress?.SetProgress(90, "Saving animation");

        // Havok containers setup
        hkRootLevelContainer root = new();
        hkaAnimationContainer container = new();

        container.m_animations.Add(newAnimation);
        container.m_bindings.Add(newBinding);

        hkRootLevelContainer.NamedVariant variant = new()
        {
            m_name = "Animation Container",
            m_className = "hkaAnimationContainer",
            m_variant = container
        };

        root.m_namedVariants.Add(variant);

        // Serialization
        HavokBinarySerializer _serializer = new();

        if (path.EndsWith(".glb"))
        {
            path = path.Replace(".glb", ".anmb");
        }
        else
        {
            path = path.Replace(".gltf", ".anmb");
        }

        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            _serializer.Write(root, fs);
        }
    }

    /// <summary>
    /// Logs basic info about a provided Havok animation file to the console
    /// </summary>
    public void GetStats(AnmbFile anim)
    {
        Console.WriteLine($"Animation type: {anim.m_Animation.m_type}");
        Console.WriteLine($"Animation duration: {anim.m_Animation.m_duration}");
        
        if (anim.m_Animation.m_type == hkaAnimation.AnimationType.HK_INTERLEAVED_ANIMATION)
        {
            var castedAnim = (hkaInterleavedUncompressedAnimation)anim.m_Animation;
            Console.WriteLine($"Frame count: {castedAnim.m_transforms.Count / anim.m_Animation.m_numberOfTransformTracks}");
        }
        else if (anim.m_Animation.m_type == hkaAnimation.AnimationType.HK_PREDICTIVE_COMPRESSED_ANIMATION)
        {
            var castedAnim = (hkaPredictiveCompressedAnimation)anim.m_Animation;
            Console.WriteLine($"Frame count: {castedAnim.m_numFrames}");
            Console.WriteLine($"Bone count: {castedAnim.m_numBones}");
        }

        Console.WriteLine($"Number of transform tracks: {anim.m_Animation.m_numberOfTransformTracks}");
        Console.WriteLine($"Number of float tracks: {anim.m_Animation.m_numberOfFloatTracks}");
        Console.WriteLine($"Number of annotation tracks: {anim.m_Animation.m_annotationTracks.Count}");
    }

    // Clean-up function for Vector4-based transform tracks
    private static Vector4 CleanTransform(Vector4 transform)
    {
        Vector4 newTransform = new();

        for (int i = 0; i < 4; i++)
        {
            float value = transform[i];

            if (value > 0.999 && value < 1)
            {
                newTransform[i] = 1;
            }
            else if (value > -0.001 && value < 0)
            {
                newTransform[i] = 0;
            }
            else if (value < 0.001 && value > 0)
            {
                newTransform[i] = 0;
            }
            else
            {
                newTransform[i] = value;
            }
        }

        return newTransform;
    }

    // Clean-up function for Quaternion-based transform tracks
    private static Quaternion CleanTransform(Quaternion transform)
    {
        Quaternion newTransform = new();

        for (int i = 0; i < 4; i++)
        {
            float value = transform[i];

            if (value > 0.999 && value < 1)
            {
                newTransform[i] = 1;
            }
            else if (value > -0.001 && value < 0)
            {
                newTransform[i] = 0;
            }
            else if (value < 0.001 && value > 0)
            {
                newTransform[i] = 0;
            }
            else
            {
                newTransform[i] = value;
            }
        }

        newTransform = Quaternion.Normalize(newTransform);

        return newTransform;
    }
}