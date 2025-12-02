using FinalFantasy16Library.Files.SKL;
using FinalFantasy16Library.Utils;
using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;
using IONET;
using IONET.Core;
using IONET.Core.Animation;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using System.Numerics;
using static HKLib.hk2018.hclVolumeConstraint;

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
    public void Import(SklFile sklFile, string path, float frameRate = 30.0f, ProgressTracker progress = null)
    {
        progress?.SetProgress(10, "Loading animation file");

        var importSettings = new ImportSettings
        {
            FrameRate = frameRate
        };
        var scene = IOManager.LoadScene(path, importSettings);

        var animation = scene.Animations[0];
        var sourceSkeleton = scene.Models[0].Skeleton;
        var targetSkeleton = sklFile.m_Skeleton;

        int numTargetTracks = targetSkeleton.m_bones.Count;
        int frameCount = (int)animation.GetFrameCount();
        float duration = (frameCount - 1) / frameRate;

        progress?.SetProgress(30, "Binding bones");

        // Map animation groups to bone indices in target skeleton
        // Key: track index (in animation), Value: bone index (in skeleton)
        SortedDictionary<int, int> trackToBoneMap = [];
        int trackIndex = 0;

        foreach (var group in animation.Groups)
        {
            // Find the bone in target skeleton by name
            int boneIndex = -1;
            for (int i = 0; i < targetSkeleton.m_bones.Count; i++)
            {
                if (targetSkeleton.m_bones[i].m_name == group.Name)
                {
                    boneIndex = i;
                    break;
                }
            }

            if (boneIndex != -1 && group.Name != "skeleton")
            {
                trackToBoneMap[trackIndex] = boneIndex;
                trackIndex++;
            }
        }

        int validTrackCount = trackToBoneMap.Count;
        Console.WriteLine($"Bound {validTrackCount} bones with animation data.");

        progress?.SetProgress(50, "Setting up ANMB");

        // Pre-allocate empty transforms to set "m_transforms" correctly
        int totalTransforms = validTrackCount * frameCount;
        var transforms = new hkQsTransform[totalTransforms];
        for (int i = 0; i < totalTransforms; i++)
        {
            transforms[i] = new hkQsTransform();
        }

        // Set new animation and binding base values
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

        trackIndex = 0;

        // Iterate over animation groups (bones with animation data)
        foreach (var group in animation.Groups)
        {
            // Find the bone index in target skeleton
            int boneIndex = -1;
            for (int i = 0; i < targetSkeleton.m_bones.Count; i++)
            {
                if (targetSkeleton.m_bones[i].m_name == group.Name)
                {
                    boneIndex = i;
                    break;
                }
            }

            if (boneIndex == -1 || group.Name == "skeleton")
                continue;

            // Map this track to the bone index
            newBinding.m_transformTrackToBoneIndices[trackIndex] = (short)boneIndex;

            // Set reference pose (target skeleton) as fallback
            Vector4 translation = targetSkeleton.m_referencePose[boneIndex].m_translation;
            Quaternion rotation = targetSkeleton.m_referencePose[boneIndex].m_rotation;
            Vector4 scale = targetSkeleton.m_referencePose[boneIndex].m_scale;

            // Getting transform components for each track channel at every frame
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

                newAnimation.m_transforms[frame * validTrackCount + trackIndex].m_translation = translation;
                newAnimation.m_transforms[frame * validTrackCount + trackIndex].m_rotation = rotation;
                newAnimation.m_transforms[frame * validTrackCount + trackIndex].m_scale = scale;
            }

            trackIndex++;
        }

        Console.WriteLine($"Transferred transform values from {validTrackCount} bones across {frameCount - 1} frames.");
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

        path = Path.ChangeExtension(path, ".anmb");

        using (FileStream fs = new(path, FileMode.Create, FileAccess.Write))
        {
            _serializer.Write(root, fs);
        }

        Console.WriteLine("Animation converted successfully!");
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

    /// <summary>
    /// Export format for animations.
    /// </summary>
    public enum ExportFormat
    {
        GLTF,
        DAE
    }

    /// <summary>
    /// Exports Havok animation(s) to GLTF or DAE format.
    /// DAE not exposed to user yet since can not make the dae imports work properly for now although there is no problem on export side.
    /// </summary>
    public void Export(SklFile sklFile, string animationPath, 
        ExportFormat exportFormat = ExportFormat.GLTF, ProgressTracker? progress = null)
    {
        progress?.SetProgress(5, "Validating skeleton file");

        var skeleton = sklFile.m_Skeleton;
        if (skeleton == null)
        {
            throw new InvalidOperationException("Failed to load skeleton.");
        }

        string? inputBaseFolder = null;

        if (File.Exists(animationPath))
        {
            // Single file
            inputBaseFolder = Path.GetDirectoryName(animationPath);
        }
        else
        {
            throw new FileNotFoundException($"Animation path not found: {animationPath}");
        }

        Console.WriteLine($"Export format: {exportFormat}");
        Console.WriteLine($"Output folder: {inputBaseFolder}");

        // Process animation file
        progress?.SetProgress(20, $"Exporting {Path.GetFileName(animationPath)}");
        ExportSingleAnimation(animationPath, skeleton, inputBaseFolder, exportFormat, inputBaseFolder);

        progress?.SetProgress(100, "Export complete");
        Console.WriteLine("All animations processed successfully.");
    }

    /// <summary>
    /// Exports a single animation file.
    /// </summary>
    private void ExportSingleAnimation(string animationPath, hkaSkeleton skeleton, string? outputFolder, 
        ExportFormat exportFormat, string? inputBaseFolder = null, ProgressTracker? progress = null)
    {
        Console.WriteLine($"Loading anmb file: {animationPath}");
        progress?.SetProgress(30, $"Loading anmb file {Path.GetFileName(animationPath)}");

        AnmbFile animFile = AnmbFile.Open(File.OpenRead(animationPath));

        var animation = animFile.m_Animation;

        if (animation == null)
        {
            Console.WriteLine($"  Warning: No animation found in {animationPath}");
            return;
        }

        var animationBinding = animFile.m_Binding;

        if (animationBinding == null)
        {
            Console.WriteLine($"  Warning: No animation binding found in {animationPath}");
            return;
        }

        progress?.SetProgress(50, $"Decoding animation data from {Path.GetFileName(animationPath)}");
        // Use HavokAnimationDecoder to decode the animation
        var allTracks = HavokAnimationDecoder.DecodeAnimation(animation, skeleton);

        progress?.SetProgress(70, $"Building export scene for {Path.GetFileName(animationPath)}");
        // Get file name without extension for animation name
        string animationName = Path.GetFileNameWithoutExtension(animationPath);
        var scene = BuildAnimationScene(skeleton, animationBinding, allTracks, exportFormat, animationName);

        progress?.SetProgress(90, $"Exporting animation to {exportFormat} format");
        // Generate output path preserving folder structure
        string fileName = Path.GetFileNameWithoutExtension(animationPath);
        string outputDir = outputFolder ?? Directory.GetCurrentDirectory();

        // Preserve folder structure if processing from a folder
        if (inputBaseFolder != null)
        {
            string? animDirectory = Path.GetDirectoryName(animationPath);
            if (animDirectory != null)
            {
                string relativePath = Path.GetRelativePath(inputBaseFolder, animDirectory);
                if (relativePath != ".")
                {
                    outputDir = Path.Combine(outputDir, relativePath);
                }
            }
        }

        // Create output directory if it doesn't exist
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string fileExtension = exportFormat == ExportFormat.GLTF ? ".gltf" : ".dae";
        string outputPath = Path.Combine(outputDir, $"{fileName}{fileExtension}");

        IOManager.ExportScene(scene, outputPath, new ExportSettings
        {
            ExportAnimations = true,
            FrameRate = 30.0f,
            BlenderMode = true
        });

        Console.WriteLine($"  Exported: {outputPath}");
    }


    /// <summary>
    /// Builds an IOScene with skeleton and animation data for export.
    /// </summary>
    private IOScene BuildAnimationScene(hkaSkeleton hkaSkeleton, hkaAnimationBinding hkaAnimationBinding, 
        List<List<hkQsTransform>> allFrames, ExportFormat exportFormat, string animationName)
    {
        // Build skeleton
        List<IOBone> bones = new List<IOBone>();
        for (int i = 0; i < hkaSkeleton.m_bones.Count; i++)
        {
            var bone = hkaSkeleton.m_bones[i];

            var normalizedRotation = Quaternion.Normalize(hkaSkeleton.m_referencePose[i].m_rotation);
            var outputBone = new IOBone
            {
                Name = bone.m_name,
                Translation = new Vector3(
                    hkaSkeleton.m_referencePose[i].m_translation.X,
                    hkaSkeleton.m_referencePose[i].m_translation.Y,
                    hkaSkeleton.m_referencePose[i].m_translation.Z),
                Rotation = new Quaternion(
                    normalizedRotation.X,
                    normalizedRotation.Y,
                    normalizedRotation.Z,
                    normalizedRotation.W),
                Scale = Vector3.One
            };

            bones.Add(outputBone);
        }

        // Establish parent-child relationships
        IOBone? rootBone = null;
        for (int i = 0; i < hkaSkeleton.m_bones.Count; i++)
        {
            var boneParentIndex = hkaSkeleton.m_parentIndices[i];

            if (boneParentIndex < 0)
            {
                rootBone = bones[i];
            }
            else if (boneParentIndex >= 0)
            {
                bones[i].Parent = bones[boneParentIndex];
            }
        }

        var skeleton = new IOSkeleton();
        if (rootBone != null)
        {
            skeleton.RootBones.Add(rootBone);
        }

        // Create model with skeleton only (no meshes)
        var model = new IOModel { Name = "Model", Skeleton = skeleton };
        model.Meshes.Clear();

        // Build animation
        int numFrames = allFrames.Count;
        var sceneAnim = new IOAnimation { Name = animationName, StartFrame = 0, EndFrame = numFrames - 1 };

        for (int i = 0; i < hkaAnimationBinding.m_transformTrackToBoneIndices.Count; i++)
        {
            int boneIndex = hkaAnimationBinding.m_transformTrackToBoneIndices[i];
            string boneName = hkaSkeleton.m_bones[boneIndex].m_name;
            var boneGroup = new IOAnimation { Name = boneName };

            // Position tracks
            var posXTrack = new IOAnimationTrack(IOAnimationTrackType.PositionX);
            var posYTrack = new IOAnimationTrack(IOAnimationTrackType.PositionY);
            var posZTrack = new IOAnimationTrack(IOAnimationTrackType.PositionZ);

            // Scale tracks
            var scaleXTrack = new IOAnimationTrack(IOAnimationTrackType.ScaleX);
            var scaleYTrack = new IOAnimationTrack(IOAnimationTrackType.ScaleY);
            var scaleZTrack = new IOAnimationTrack(IOAnimationTrackType.ScaleZ);

            // Process all frames for this bone
            for (int f = 0; f < numFrames; f++)
            {
                hkQsTransform transform = allFrames[f][i];

                posXTrack.InsertKeyframe(f, transform.m_translation.X);
                posYTrack.InsertKeyframe(f, transform.m_translation.Y);
                posZTrack.InsertKeyframe(f, transform.m_translation.Z);

                scaleXTrack.InsertKeyframe(f, transform.m_scale.X);
                scaleYTrack.InsertKeyframe(f, transform.m_scale.Y);
                scaleZTrack.InsertKeyframe(f, transform.m_scale.Z);
            }

            boneGroup.Tracks.Add(posXTrack);
            boneGroup.Tracks.Add(posYTrack);
            boneGroup.Tracks.Add(posZTrack);
            boneGroup.Tracks.Add(scaleXTrack);
            boneGroup.Tracks.Add(scaleYTrack);
            boneGroup.Tracks.Add(scaleZTrack);

            // Add rotation tracks based on export format
            if (exportFormat == ExportFormat.DAE)
            {
                // DAE mode: use Euler rotation tracks
                var rotXTrack = new IOAnimationTrack(IOAnimationTrackType.RotationEulerX);
                var rotYTrack = new IOAnimationTrack(IOAnimationTrackType.RotationEulerY);
                var rotZTrack = new IOAnimationTrack(IOAnimationTrackType.RotationEulerZ);

                for (int f = 0; f < numFrames; f++)
                {
                    hkQsTransform transform = allFrames[f][i];

                    // Convert quaternion to Euler angles (XYZ rotation order)
                    float sinr_cosp = 2.0f * (transform.m_rotation.W * transform.m_rotation.X + transform.m_rotation.Y * transform.m_rotation.Z);
                    float cosr_cosp = 1.0f - 2.0f * (transform.m_rotation.X * transform.m_rotation.X + transform.m_rotation.Y * transform.m_rotation.Y);
                    float angleX = MathF.Atan2(sinr_cosp, cosr_cosp);

                    float sinp = 2.0f * (transform.m_rotation.W * transform.m_rotation.Y - transform.m_rotation.Z * transform.m_rotation.X);
                    float angleY = MathF.Abs(sinp) >= 1.0f ? MathF.CopySign(MathF.PI / 2.0f, sinp) : MathF.Asin(sinp);

                    float siny_cosp = 2.0f * (transform.m_rotation.W * transform.m_rotation.Z + transform.m_rotation.X * transform.m_rotation.Y);
                    float cosy_cosp = 1.0f - 2.0f * (transform.m_rotation.Y * transform.m_rotation.Y + transform.m_rotation.Z * transform.m_rotation.Z);
                    float angleZ = MathF.Atan2(siny_cosp, cosy_cosp);

                    rotXTrack.InsertKeyframe(f, angleZ);
                    rotYTrack.InsertKeyframe(f, angleY);
                    rotZTrack.InsertKeyframe(f, angleX);
                }

                boneGroup.Tracks.Add(rotZTrack);
                boneGroup.Tracks.Add(rotYTrack);
                boneGroup.Tracks.Add(rotXTrack);
            }
            else // GLTF mode
            {
                // GLTF mode: use quaternion tracks
                var quatXTrack = new IOAnimationTrack(IOAnimationTrackType.QuatX);
                var quatYTrack = new IOAnimationTrack(IOAnimationTrackType.QuatY);
                var quatZTrack = new IOAnimationTrack(IOAnimationTrackType.QuatZ);
                var quatWTrack = new IOAnimationTrack(IOAnimationTrackType.QuatW);

                for (int f = 0; f < numFrames; f++)
                {
                    hkQsTransform transform = allFrames[f][i];

                    quatXTrack.InsertKeyframe(f, transform.m_rotation.X);
                    quatYTrack.InsertKeyframe(f, transform.m_rotation.Y);
                    quatZTrack.InsertKeyframe(f, transform.m_rotation.Z);
                    quatWTrack.InsertKeyframe(f, transform.m_rotation.W);
                }

                boneGroup.Tracks.Add(quatXTrack);
                boneGroup.Tracks.Add(quatYTrack);
                boneGroup.Tracks.Add(quatZTrack);
                boneGroup.Tracks.Add(quatWTrack);
            }

            sceneAnim.Groups.Add(boneGroup);
        }

        // Create scene with skeleton and animation
        var scene = new IOScene { Name = "Scene" };
        scene.Models.Add(model);
        scene.Animations.Add(sceneAnim);

        return scene;
    }
}