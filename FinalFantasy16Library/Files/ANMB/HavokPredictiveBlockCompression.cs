using System;
using System.Runtime.CompilerServices;

namespace HKLib.hk2018;

/// <summary>
/// Standalone implementation of Havok's predictive block compression for animation data.
/// Compresses 16×16 blocks of 14-bit signed integer animation samples.
/// </summary>
public static class PredictiveBlockCompression
{
    public const int BLOCK_CHANNELS = 16;
    public const int BLOCK_FRAMES = 16;
    public const int DELTA_CODE_ORDER = 3;
    public const int MIN_CODEABLE = -(1 << 13); // -8192
    public const int MAX_CODEABLE = (1 << 13) - 1; // 8191
    public const int MAX_COMPRESSED_SIZE = 272; // 16 header + 16*16 max

    /// <summary>
    /// Block structure: 16 channels × 16 frames of 14-bit signed integers
    /// </summary>
    public class Block
    {
        public short[][] Data; // [channel][frame]

        public Block()
        {
            Data = new short[BLOCK_CHANNELS][];
            for (int i = 0; i < BLOCK_CHANNELS; i++)
                Data[i] = new short[BLOCK_FRAMES];
        }

        public Block Clone()
        {
            var clone = new Block();
            for (int i = 0; i < BLOCK_CHANNELS; i++)
                Array.Copy(Data[i], clone.Data[i], BLOCK_FRAMES);
            return clone;
        }
    }

    // IMPORTANT NOTE: Haven't verified correctness of this implementation against Havok's original.
    #region Encoding

    /// <summary>
    /// Encode a block of animation data
    /// </summary>
    /// <param name="block">Input block (16×16 samples)</param>
    /// <param name="nframes">Number of valid frames (1-16)</param>
    /// <param name="nchannels">Number of valid channels (1-16)</param>
    /// <returns>Compressed data bytes</returns>
    public static byte[] EncodeBlock(Block block, int nframes, int nchannels)
    {
        if (block == null) throw new ArgumentNullException(nameof(block));
        if (nframes < 1 || nframes > BLOCK_FRAMES)
            throw new ArgumentOutOfRangeException(nameof(nframes));
        if (nchannels < 1 || nchannels > BLOCK_CHANNELS)
            throw new ArgumentOutOfRangeException(nameof(nchannels));

        // Clone to avoid modifying input
        var workBlock = block.Clone();

        // Apply delta encoding to each channel
        for (int ch = 0; ch < nchannels; ch++)
        {
            DeltaEncode(workBlock.Data[ch]);
        }

        // Allocate output buffer
        var output = new byte[MAX_COMPRESSED_SIZE];
        int outPos = 0;

        // Reserve header space (16 bytes)
        var sizeBytes = new byte[BLOCK_CHANNELS];
        outPos += BLOCK_CHANNELS;

        // Encode each channel
        for (int ch = 0; ch < nchannels; ch++)
        {
            EncodeChannel(workBlock.Data[ch], nframes, output, ref outPos, out sizeBytes[ch]);
        }

        // Fill remaining channels with dummy data
        for (int ch = nchannels; ch < BLOCK_CHANNELS; ch++)
        {
            sizeBytes[ch] = 0x00; // seg0=1 byte, seg1=1 byte
            output[outPos++] = 0;
            output[outPos++] = 0;
        }

        // Write header at beginning
        Array.Copy(sizeBytes, 0, output, 0, BLOCK_CHANNELS);

        // Return only used bytes
        var result = new byte[outPos];
        Array.Copy(output, result, outPos);
        return result;
    }

    private static void DeltaEncode(short[] data)
    {
        // Step 1: Shift left by 2 (14-bit alignment for proper overflow)
        for (int i = 0; i < BLOCK_FRAMES; i++)
        {
            data[i] = (short)(data[i] << 2);
        }

        // Step 2: Apply delta encoding DELTA_CODE_ORDER times
        for (int order = 0; order < DELTA_CODE_ORDER; order++)
        {
            for (int i = BLOCK_FRAMES - 1; i >= 1; i--)
            {
                data[i] = (short)(data[i] - data[i - 1]);
            }
        }

        // Step 3: Shift right by 2 (convert back to 14-bit)
        for (int i = 0; i < BLOCK_FRAMES; i++)
        {
            data[i] = (short)(data[i] >> 2);
        }
    }

    private static void EncodeChannel(short[] data, int nframes, byte[] output,
        ref int outPos, out byte sizeByte)
    {
        int startPos = outPos;

        // Encode segment 0 (frames 0-7)
        int len1 = EncodeSegment(data, 0, Math.Min(8, nframes), output, ref outPos);

        // Encode segment 1 (frames 8-15)
        int len2;
        if (nframes > 8)
        {
            len2 = EncodeSegment(data, 8, nframes - 8, output, ref outPos);
        }
        else
        {
            output[outPos++] = 0; // Dummy segment
            len2 = 1;
        }

        // Create size byte: high 4 bits = len1-1, low 4 bits = len2-1
        sizeByte = (byte)(((len1 - 1) << 4) | (len2 - 1));
    }

    private static int EncodeSegment(short[] data, int offset, int count,
        byte[] output, ref int outPos)
    {
        if (count == 0) return 0;

        // Build bit stream (little-endian)
        var bitStream = new byte[16];
        int bitPos = 0;

        // Pad to even count
        int actualCount = count;
        if (count % 2 != 0)
            actualCount++;

        // Encode pairs
        for (int i = 0; i < actualCount; i += 2)
        {
            short val0 = offset + i < BLOCK_FRAMES ? data[offset + i] : (short)0;
            short val1 = offset + i + 1 < BLOCK_FRAMES ? data[offset + i + 1] : (short)0;

            // Find minimum width that fits both values
            int width = FindWidth(val0, val1);

            // Write: width (4 bits), value0 (width bits), value1 (width bits)
            WriteBits(bitStream, ref bitPos, width, 4);
            WriteBits(bitStream, ref bitPos, val0 & BitMask(width), width);
            WriteBits(bitStream, ref bitPos, val1 & BitMask(width), width);
        }

        // Calculate byte count
        int byteCount = (bitPos + 7) / 8;

        // Write segment backwards (big-endian)
        int segStart = outPos;
        for (int i = 0; i < byteCount; i++)
        {
            output[outPos++] = bitStream[byteCount - 1 - i];
        }

        return byteCount;
    }

    private static int FindWidth(short val0, short val1)
    {
        for (int width = 0; width <= 14; width++)
        {
            if (FitsIn(val0, width) && FitsIn(val1, width))
                return width;
        }
        return 14;
    }

    private static bool FitsIn(int value, int bitlen)
    {
        if (bitlen == 0) return value == 0;
        int v = value >> (bitlen - 1);
        return v == 0 || v == -1;
    }

    private static void WriteBits(byte[] data, ref int pos, int value, int nbits)
    {
        while (nbits > 0)
        {
            int byteIdx = pos / 8;
            int bitIdx = pos % 8;
            int bitsAvail = 8 - bitIdx;
            int bitsToWrite = Math.Min(bitsAvail, nbits);

            int mask = (1 << bitsToWrite) - 1;
            data[byteIdx] |= (byte)((value & mask) << bitIdx);

            value >>= bitsToWrite;
            nbits -= bitsToWrite;
            pos += bitsToWrite;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BitMask(int bits)
    {
        return bits == 0 ? 0 : (1 << bits) - 1;
    }

    #endregion

    #region Decoding
    public static short[][] DecodeAllFrameChannel(byte[] data, ushort[] blockOffsets, int totalChannelCount, int totalFrameCount)
    {
        short[][] channelFrameVal = new short[totalChannelCount][];

        // Initialize all channel arrays
        for (int i = 0; i < totalChannelCount; i++)
        {
            channelFrameVal[i] = new short[totalFrameCount];
        }

        int[] blockOffsetsVal = new int[blockOffsets.Length / 2 + 1];
        // start from 0
        blockOffsetsVal[0] = 0;
        for (int i = 0; i < blockOffsets.Length / 2; ++i)
        {
            ushort low = blockOffsets[i * 2];
            ushort high = blockOffsets[i * 2 + 1];

            blockOffsetsVal[i + 1] = (high << 16) | low;
        }

        int outputFrameOffset = 0; // Where we write in the output

        for (int frameDivideIdx = 0; frameDivideIdx < blockOffsetsVal.Length - 1; frameDivideIdx++)
        {
            int frameDataOffset = (int)blockOffsetsVal[frameDivideIdx];
            int endPos = 0;
            int fetchedChannelCount = 0;

            // Calculate the actual frame range this block contains in the compressed data
            // Block 0: frames 0-15 (starting at compressed frame 0)
            // Block 1: frames 15-30 (starting at compressed frame 15, overlaps at 15)
            // Block 2: frames 30-45 (starting at compressed frame 30, overlaps at 30)
            int compressedFrameStart = frameDivideIdx * (BLOCK_FRAMES - 1);
            int remainingFrames = totalFrameCount - compressedFrameStart;
            int frameCount = Math.Min(BLOCK_FRAMES, remainingFrames);

            //Console.WriteLine($"Block {frameDivideIdx}: compressedFrameStart={compressedFrameStart}, " +
            //                 $"frameCount={frameCount}, outputFrameOffset={outputFrameOffset}");

            // Process all channels for this frame block
            while (fetchedChannelCount < totalChannelCount)
            {
                int channelCount = (int)Math.Min(BLOCK_CHANNELS, totalChannelCount - fetchedChannelCount);

                Block block = DecodeWholeBlock(data, channelCount, frameCount, out endPos, frameDataOffset);

                frameDataOffset = endPos;

                // Copy decoded data to output arrays
                for (int i = 0; i < channelCount; i++)
                {
                    // Skip the first frame if this is not the first block (it's the overlapping frame)
                    int sourceOffset = (frameDivideIdx > 0) ? 1 : 0;
                    int framesToCopy = frameCount - sourceOffset;

                    if (framesToCopy > 0)
                    {
                        Array.Copy(block.Data[i], sourceOffset,
                            channelFrameVal[fetchedChannelCount + i],
                            outputFrameOffset,
                            framesToCopy);
                    }
                }

                fetchedChannelCount += channelCount;
            }

            // Update output offset: first block adds 16 frames, subsequent blocks add 15
            outputFrameOffset += (frameDivideIdx == 0) ? frameCount : (frameCount - 1);
        }

        return channelFrameVal;
    }

    /// <summary>
    /// Decode a compressed block
    /// </summary>
    /// <param name="data">Compressed data</param>
    /// <returns>Decoded block (16×16 samples)</returns>
    public static Block DecodeWholeBlock(byte[] data, int channelNum, int frameNum, out int endPos, int startOffset = 0)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length < BLOCK_CHANNELS + 2 * BLOCK_CHANNELS)
            throw new ArgumentException("Data too short for valid block");

        var block = new Block();
        int dataPos = (int)startOffset;

        // Read header (16 size bytes)
        var sizeBytes = new byte[BLOCK_CHANNELS];
        Array.Copy(data, (int)startOffset, sizeBytes, 0, BLOCK_CHANNELS);
        dataPos += BLOCK_CHANNELS;

        // Decode each channel
        for (int ch = 0; ch < channelNum; ch++)
        {
            int len0 = ((sizeBytes[ch] >> 4) & 0xF) + 1;
            int len1 = (sizeBytes[ch] & 0xF) + 1;

            // Decode segment 0 (frames 0-7)
            DecodeSegment(data, dataPos + len0, block.Data[ch], 0);
            dataPos += len0;

            // Decode segment 1 (frames 8-15)
            DecodeSegment(data, dataPos + len1, block.Data[ch], 8);
            dataPos += len1;

            // Apply delta decoding
            DeltaDecode(block.Data[ch], frameNum);
        }

        endPos = dataPos;

        return block;
    }


    private static void DecodeSegment(byte[] coded, int codedEnd, short[] decoded, int decodedOffset)
    {
        // Load 16 bytes (may read before segment start, but within header buffer)
        ulong high = 0, low = 0;

        high = ReadBigEndianUInt64(coded, codedEnd - 16);
        low = ReadBigEndianUInt64(coded, codedEnd - 8);

        int idx = decodedOffset;

        // Decode 4 pairs (8 samples)
        for (int pair = 0; pair < 4; pair++)
        {
            // Extract width (4 bits)
            int width = (int)(low & 0xF);

            // Extract value 0
            int val0 = (int)((low >> 4) & (ulong)BitMask(width));
            val0 = SignExtend(val0, width);
            decoded[idx++] = (short)(val0 << 2);

            // Extract value 1
            int val1 = (int)((low >> (4 + width)) & (ulong)BitMask(width));
            val1 = SignExtend(val1, width);
            decoded[idx++] = (short)(val1 << 2);

            // Shift by width*2 + 4 bits
            int shift = width * 2 + 4;
            low >>= shift;

            // Handle the 128-bit shift correctly
            if (shift < 64)
            {
                low |= (high << (64 - shift));
                high >>= shift;
            }
            else
            {
                // If shift >= 64, low becomes entirely from high
                low = high >> (shift - 64);
                high = 0;
            }
        }
    }

    private static ulong ReadBigEndianUInt64(byte[] data, int offset)
    {
        if (offset < 0 || offset + 8 > data.Length)
            return 0;

        return ((ulong)data[offset] << 56) |
                ((ulong)data[offset + 1] << 48) |
                ((ulong)data[offset + 2] << 40) |
                ((ulong)data[offset + 3] << 32) |
                ((ulong)data[offset + 4] << 24) |
                ((ulong)data[offset + 5] << 16) |
                ((ulong)data[offset + 6] << 8) |
                data[offset + 7];
    }

    private static int SignExtend(int value, int width)
    {
        if (width == 0) return 0;
        int shift = 32 - width;
        return (value << shift) >> shift;
    }

    private static void DeltaDecode(short[] data, int frameCount)
    {
        // Apply cumulative sum DELTA_CODE_ORDER times
        for (int order = 0; order < DELTA_CODE_ORDER; order++)
        {
            short sum = 0;
            for (int i = 0; i < frameCount; i++) // Use frameCount instead of BLOCK_FRAMES
            {
                sum = (short)(sum + data[i]);
                data[i] = sum;
            }
        }

        // Shift right by 2
        for (int i = 0; i < frameCount; i++) // Use frameCount instead of BLOCK_FRAMES
        {
            data[i] = (short)(data[i] >> 2);
        }
    }

    #endregion
}
