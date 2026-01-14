using HKLib.hk2018;

using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.ANMB;

/// <summary>
/// Generic animation decoder utility for Havok animations.
/// Provides a unified interface to decode different animation formats.
/// </summary>
public static class HavokAnimationDecoder
{
    /// <summary>
    /// Decodes an animation into frame-major track data.
    /// </summary>
    /// <param name="animation">The animation to decode</param>
    /// <param name="skeleton">Optional skeleton for reference pose</param>
    /// <returns>List of frames, where each frame contains bone transforms</returns>
    public static List<List<hkQsTransform>> DecodeAnimation(hkaAnimation animation, hkaSkeleton? skeleton = null)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return animation.m_type switch
        {
            hkaAnimation.AnimationType.HK_PREDICTIVE_COMPRESSED_ANIMATION =>
                PredictiveCompressedDecoder.Decode((hkaPredictiveCompressedAnimation)animation, skeleton),

            hkaAnimation.AnimationType.HK_INTERLEAVED_ANIMATION =>
                InterleavedUncompressedDecoder.Decode((hkaInterleavedUncompressedAnimation)animation),

            hkaAnimation.AnimationType.HK_SPLINE_COMPRESSED_ANIMATION =>
                throw new NotImplementedException("Spline compressed animation decoding not yet implemented"),

            hkaAnimation.AnimationType.HK_QUANTIZED_COMPRESSED_ANIMATION =>
                throw new NotImplementedException("Quantized compressed animation decoding not yet implemented"),

            hkaAnimation.AnimationType.HK_REFERENCE_POSE_ANIMATION =>
                throw new NotImplementedException("Reference pose animation decoding not yet implemented"),

            _ => throw new NotSupportedException($"Animation type {animation.m_type} is not supported")
        };
    }

    /// <summary>
    /// Registers a custom decoder for a specific animation type.
    /// Allows external code to add support for new animation types.
    /// </summary>
    public static void RegisterDecoder(hkaAnimation.AnimationType type, Func<hkaAnimation, hkaSkeleton?, List<List<hkQsTransform>>> decoder)
    {
        _customDecoders[type] = decoder;
    }

    private static readonly Dictionary<hkaAnimation.AnimationType, Func<hkaAnimation, hkaSkeleton?, List<List<hkQsTransform>>>> _customDecoders = new();
}

/// <summary>
/// Decoder for predictive compressed animations.
/// Extracted from hkaPredictiveCompressedAnimation.fetchAllTracks()
/// </summary>
public static class PredictiveCompressedDecoder
{
    private enum IntArrayID
    {
        BLOCK_OFFSETS,
        FIRST_FLOAT_BLOCK_OFFSETS,
        IS_ANIMATED_BITMAP,
        IS_FIXED_RANGE_BITMAP,
        DYNAMIC_BONE_TRACK_INDEX,
        DYNAMIC_FLOAT_TRACK_INDEX,
        STATIC_BONE_TRACK_INDEX,
        STATIC_FLOAT_TRACK_INDEX,
        RENORM_QUATERNION_INDEX,
        NUM_INT_ARRAYS
    }

    private enum FloatArrayID
    {
        STATIC_VALUES,
        DYNAMIC_SCALES,
        DYNAMIC_OFFSETS,
        NUM_FLOAT_ARRAYS
    }

    public static List<List<hkQsTransform>> Decode(hkaPredictiveCompressedAnimation animation, hkaSkeleton? skeleton)
    {
        Dictionary<int, List<hkQsTransform>> allTracks = new();
        var refBones = skeleton?.m_referencePose.ToArray() ?? Array.Empty<hkQsTransform>();

        if (animation.m_numFloatSlots > 0)
        {
            Console.WriteLine("Warning: This animation has float tracks. Float track decoding is not fully implemented.");
        }

        var boneWeights = new byte[animation.m_numBones];
        var floatWeights = new byte[animation.m_numFloatSlots];
        Array.Fill(boneWeights, (byte)0xff);
        Array.Fill(floatWeights, (byte)0xff);

        ApplyWeights(GetArray(animation, IntArrayID.IS_ANIMATED_BITMAP), boneWeights, animation.m_numBones);
        ApplyWeights(
            GetArray(animation, IntArrayID.IS_ANIMATED_BITMAP).Slice((animation.m_numBones + 15) / 16),
            floatWeights,
            animation.m_numFloatSlots
        );

        if (animation.m_numBones > 0)
        {
            int numFloatsPerBone = animation.m_numBones * 3 * 4;

            // Initialize animated bone tracks with reference pose
            for (int i = 0; i < animation.m_numBones; i++)
            {
                if (boneWeights[i] == 0) continue;

                List<hkQsTransform> boneFrames = new();
                allTracks[i] = boneFrames;

                for (int f = 0; f < animation.m_numFrames; f++)
                {
                    var boneTransform = new hkQsTransform
                    {
                        m_translation = refBones[i].m_translation,
                        m_rotation = refBones[i].m_rotation,
                        m_scale = refBones[i].m_scale
                    };
                    boneFrames.Add(boneTransform);
                }
            }

            List<int> boneNeedRecoverW = [];

            // Process static values
            ProcessStaticValues(animation, allTracks, numFloatsPerBone, boneNeedRecoverW);

            // Process dynamic values
            ProcessDynamicValues(animation, allTracks, numFloatsPerBone, boneNeedRecoverW);

            // Recover quaternion W components
            RecoverQuaternions(allTracks, animation.m_numFrames, boneNeedRecoverW);
        }

        return ConvertToFrameMajor(allTracks, animation.m_numFrames);
    }

    private static void ProcessStaticValues(
        hkaPredictiveCompressedAnimation animation,
        Dictionary<int, List<hkQsTransform>> allTracks,
        int numFloatsPerBone,
        List<int> boneNeedRecoverW)
    {
        ReadOnlySpan<ushort> staticIdxArray = GetArray(animation, IntArrayID.STATIC_BONE_TRACK_INDEX);
        int nstatic = GetArrayLength(animation, IntArrayID.STATIC_BONE_TRACK_INDEX);

        if (nstatic > 0)
        {
            ReadOnlySpan<float> staticVals = GetArray(animation, FloatArrayID.STATIC_VALUES);

            for (int i = 0; i < nstatic && staticIdxArray[i] < numFloatsPerBone; i++)
            {
                int channelIdx = staticIdxArray[i];
                float v = staticVals[i];
                int boneIndex = channelIdx / 12;

                if (!allTracks.ContainsKey(boneIndex)) continue;

                for (int f = 0; f < animation.m_numFrames; f++)
                {
                    var boneFrame = allTracks[boneIndex][f];
                    AnimationChannelHelper.SetBoneChannelValue(ref boneFrame, channelIdx % 12, v);
                    allTracks[boneIndex][f] = boneFrame;
                }

                if (channelIdx % 12 >= 4 && channelIdx % 12 <= 6)
                {
                    if (!boneNeedRecoverW.Contains(boneIndex))
                        boneNeedRecoverW.Add(boneIndex);
                }
            }
        }
    }

    private static void ProcessDynamicValues(
        hkaPredictiveCompressedAnimation animation,
        Dictionary<int, List<hkQsTransform>> allTracks,
        int numFloatsPerBone,
        List<int> boneNeedRecoverW)
    {
        int ndynamic = GetArrayLength(animation, IntArrayID.DYNAMIC_BONE_TRACK_INDEX);
        if (ndynamic == 0) return;

        ReadOnlySpan<ushort> dynamicIdx = GetArray(animation, IntArrayID.DYNAMIC_BONE_TRACK_INDEX);
        ReadOnlySpan<ushort> blockOffsets = GetArray(animation, IntArrayID.BLOCK_OFFSETS);
        ReadOnlySpan<ushort> isFixedRange = GetArray(animation, IntArrayID.IS_FIXED_RANGE_BITMAP);
        ReadOnlySpan<float> scalePtr = GetArray(animation, FloatArrayID.DYNAMIC_SCALES);
        ReadOnlySpan<float> offsetPtr = GetArray(animation, FloatArrayID.DYNAMIC_OFFSETS);

        var dynamicValChannelFrame = PredictiveBlockCompression.DecodeAllFrameChannel(
            animation.m_compressedData.ToArray(),
            blockOffsets.ToArray(),
            ndynamic,
            animation.m_numFrames
        );

        float fixedScale = 1.0f / ((1 << 13) - 1);
        float fixedOffset = 0.0f;
        bool hasFixedRange = false;
        int scaleOffsetIndex = 0;

        for (int i = 0; i < ndynamic && dynamicIdx[i] < numFloatsPerBone; i++)
        {
            int channelIdx = dynamicIdx[i];

            int bitmapIndex = i / 16;
            int bitPosition = i % 16;
            bool useFixedRange = ((isFixedRange[bitmapIndex] >> bitPosition) & 1) != 0;

            float scale, offset;
            if (useFixedRange)
            {
                scale = fixedScale;
                offset = fixedOffset;
                hasFixedRange = true;
            }
            else
            {
                scale = scalePtr[scaleOffsetIndex];
                offset = offsetPtr[scaleOffsetIndex];
                scaleOffsetIndex++;
            }

            int boneIndex = channelIdx / 12;
            if (!allTracks.ContainsKey(boneIndex)) continue;

            for (int f = 0; f < animation.m_numFrames; f++)
            {
                var boneFrame = allTracks[boneIndex][f];
                AnimationChannelHelper.SetBoneChannelValue(
                    ref boneFrame,
                    channelIdx % 12,
                    dynamicValChannelFrame[i][f] * scale + offset
                );
                allTracks[boneIndex][f] = boneFrame;
            }

            if (channelIdx % 12 >= 4 && channelIdx % 12 <= 6)
            {
                if (!boneNeedRecoverW.Contains(boneIndex))
                    boneNeedRecoverW.Add(boneIndex);
            }
        }

        if (hasFixedRange)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: This animation uses fixed range channels");
            Console.ResetColor();
        }
    }

    private static void RecoverQuaternions(Dictionary<int, List<hkQsTransform>> allTracks, int numFrames, List<int> boneNeedRecoverW)
    {
        foreach (int boneIndex in boneNeedRecoverW)
        {
            if (!allTracks.ContainsKey(boneIndex)) continue;

            for (int f = 0; f < numFrames; f++)
            {
                var boneFrame = allTracks[boneIndex][f];
                AnimationChannelHelper.QuaternionRecoverW(ref boneFrame.m_rotation);
                allTracks[boneIndex][f].m_rotation = Quaternion.Normalize(boneFrame.m_rotation);
            }
        }
    }

    private static void ApplyWeights(ReadOnlySpan<ushort> bitmap, Span<byte> weights, int n)
    {
        if (weights.Length > 0)
        {
            for (int i = 0; i < n; i += 16)
            {
                int isAnimated = bitmap[i / 16];
                int numWeights = Math.Min(n - i, 16);
                for (int j = 0; j < numWeights; j++)
                {
                    weights[i + j] &= (byte)(isAnimated & 1);
                    isAnimated >>= 1;
                }
            }
        }
    }

    private static List<List<hkQsTransform>> ConvertToFrameMajor(Dictionary<int, List<hkQsTransform>> allTracks, int numFrames)
    {
        List<List<hkQsTransform>> convertedTracks = new List<List<hkQsTransform>>();

        for (int i = 0; i < numFrames; i++)
        {
            List<hkQsTransform> frameTransforms = new List<hkQsTransform>();
            foreach (var kvp in allTracks)
            {
                frameTransforms.Add(kvp.Value[i]);
            }
            convertedTracks.Add(frameTransforms);
        }

        return convertedTracks;
    }

    // Helper methods to access internal animation data arrays
    private static ReadOnlySpan<ushort> GetArray(hkaPredictiveCompressedAnimation animation, IntArrayID x)
    {
        return CollectionsMarshal.AsSpan(animation.m_intData)
            .Slice(animation.m_intArrayOffsets[(int)x], GetArrayLength(animation, x));
    }

    private static ReadOnlySpan<float> GetArray(hkaPredictiveCompressedAnimation animation, FloatArrayID x)
    {
        return CollectionsMarshal.AsSpan(animation.m_floatData)
            .Slice(animation.m_floatArrayOffsets[(int)x], GetArrayLength(animation, x));
    }

    private static int GetArrayLength(hkaPredictiveCompressedAnimation animation, IntArrayID x)
    {
        const int EXTRA_ELEMS = 8;
        int start = animation.m_intArrayOffsets[(int)x];
        int end = (x == IntArrayID.NUM_INT_ARRAYS - 1) ?
            (animation.m_intData.Count - EXTRA_ELEMS) :
            animation.m_intArrayOffsets[(int)x + 1];
        return end - start;
    }

    private static int GetArrayLength(hkaPredictiveCompressedAnimation animation, FloatArrayID x)
    {
        const int EXTRA_ELEMS = 4;
        int start = animation.m_floatArrayOffsets[(int)x];
        int end = (x == FloatArrayID.NUM_FLOAT_ARRAYS - 1) ?
            (animation.m_floatData.Count - EXTRA_ELEMS) :
            animation.m_floatArrayOffsets[(int)x + 1];
        return end - start;
    }
}

/// <summary>
/// Decoder for interleaved uncompressed animations.
/// Extracted from hkaInterleavedUncompressedAnimation.fetchAllTracks()
/// </summary>
public static class InterleavedUncompressedDecoder
{
    public static List<List<hkQsTransform>> Decode(hkaInterleavedUncompressedAnimation animation)
    {
        List<List<hkQsTransform>> allTracks = [];

        if (animation.m_numberOfTransformTracks == 0 || animation.m_transforms.Count == 0)
        {
            return allTracks;
        }

        int frameCount = animation.m_transforms.Count / animation.m_numberOfTransformTracks;

        if (animation.m_transforms.Count % animation.m_numberOfTransformTracks != 0)
        {
            throw new InvalidOperationException(
                $"Invalid transform data: {animation.m_transforms.Count} transforms is not evenly divisible by {animation.m_numberOfTransformTracks} tracks");
        }

        for (int frame = 0; frame < frameCount; frame++)
        {
            List<hkQsTransform> bonesAtFrame = new(animation.m_numberOfTransformTracks);

            for (int track = 0; track < animation.m_numberOfTransformTracks; track++)
            {
                int index = frame * animation.m_numberOfTransformTracks + track;
                bonesAtFrame.Add(animation.m_transforms[index]);
            }

            allTracks.Add(bonesAtFrame);
        }

        return allTracks;
    }
}

/// <summary>
/// Helper utilities for animation channel manipulation.
/// Shared across different decoder implementations.
/// </summary>
public static class AnimationChannelHelper
{
    public static void SetBoneChannelValue(ref hkQsTransform bone, int floatIndex, float value)
    {
        switch (floatIndex)
        {
            case 0: bone.m_translation.X = value; break;
            case 1: bone.m_translation.Y = value; break;
            case 2: bone.m_translation.Z = value; break;
            case 4: bone.m_rotation.X = value; break;
            case 5: bone.m_rotation.Y = value; break;
            case 6: bone.m_rotation.Z = value; break;
            case 8: bone.m_scale.X = value; break;
            case 9: bone.m_scale.Y = value; break;
            case 10: bone.m_scale.Z = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(floatIndex), "Invalid float index for hkQsTransform");
        }
    }

    public static void QuaternionRecoverW(ref Quaternion v, bool usingManhattan = true)
    {
        float w;
        if (usingManhattan)
        {
            float sum = MathF.Abs(v.X) + MathF.Abs(v.Y) + MathF.Abs(v.Z);
            w = 1f - sum;
            w = Math.Clamp(w, 0f, 1f);
        }
        else
        {
            float lengthSquared = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
            float wSquared = Math.Clamp(1.0f - lengthSquared, 0.0f, 1.0f);
            w = MathF.Sqrt(wSquared);
        }
        v = new Quaternion(v.X, v.Y, v.Z, w);
    }
}