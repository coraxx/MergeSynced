using System;
using System.Diagnostics;
using System.IO;

namespace MergeSynced.Audio
{
    public class WavTools
    {
        /// <summary>
        /// Read wave file. Code from https://stackoverflow.com/questions/8754111/how-to-read-the-data-in-a-wav-file-to-an-array/11162668#11162668
        /// Added search for data chunks to skip unknown headers like LIST from e.g. RIFF files
        /// http://soundfile.sapp.org/doc/WaveFormat/ for header reference
        /// 
        /// Reads wav file as stereo
        /// </summary>
        /// <param name="filename">Path to file name</param>
        /// <param name="l">Output of left channel</param>
        /// <param name="r">Output of right channel</param>
        /// <returns>Details of wav file</returns>
        public WavHeader ReadWav(string filename, out float[] l, out float[] r)
        {
            l = r = null;

            try
            {
                using (FileStream fs = File.Open(filename, FileMode.Open))
                {
                    BinaryReader reader = new BinaryReader(fs);
                    WavHeader header = new WavHeader();

                    // chunk 0
                    // reading int 32 as 4 bytes/chars for debugging header
                    char chunkId1 = reader.ReadChar();
                    char chunkId2 = reader.ReadChar();
                    char chunkId3 = reader.ReadChar();
                    char chunkId4 = reader.ReadChar();
                    header.FileSize = reader.ReadInt32();
                    header.RiffType = reader.ReadInt32();


                    // chunk 1
                    // reading int 32 as 4 bytes/chars for debugging header
                    char fmtId1 = reader.ReadChar();
                    char fmtId2 = reader.ReadChar();
                    char fmtId3 = reader.ReadChar();
                    char fmtId4 = reader.ReadChar();
                    int fmtSize = reader.ReadInt32(); // min size 16 (default/legacy)

                    // Additional info
                    int fmtCode = reader.ReadInt16();
                    header.Channels = reader.ReadInt16();
                    header.SampleRate = reader.ReadInt32();
                    int byteRate = reader.ReadInt32();
                    int fmtBlockAlign = reader.ReadInt16();
                    header.BitDepth = reader.ReadInt16();

                    if (fmtSize > 16)
                    {
                        // Read any extra values that are not important to us
                        int fmtExtraSize = reader.ReadInt16();
                        if (fmtExtraSize > 0) reader.ReadBytes(fmtExtraSize);
                    }

                    // chunk 2 can have more header data before data
                    byte[] byteArray = null;
                    int bytes = 0;
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        string dataId = new string(reader.ReadChars(4));
                        bytes = reader.ReadInt32();

                        if (dataId.ToLower() != "data")
                        {
                            reader.ReadBytes(bytes);
                        }
                        else
                        {
                            byteArray = reader.ReadBytes(bytes);
                            break;
                        }
                    }

                    if (byteArray == null) return null;

                    int bytesForSamples = header.BitDepth / 8;
                    int nValues = bytes / bytesForSamples + 1;


                    float[] asFloat;
                    switch (header.BitDepth)
                    {
                        case 64:
                            double[] asDouble = new double[nValues];
                            Buffer.BlockCopy(byteArray, 0, asDouble, 0, bytes);
                            asFloat = Array.ConvertAll(asDouble, e => (float)e);
                            break;
                        case 32:
                            asFloat = new float[nValues];
                            Buffer.BlockCopy(byteArray, 0, asFloat, 0, bytes);
                            break;
                        case 16:
                            short[]
                                asInt16 = new short[nValues];
                            Buffer.BlockCopy(byteArray, 0, asInt16, 0, bytes);
                            asFloat = Array.ConvertAll(asInt16, e => e / (float)(short.MaxValue + 1));
                            break;
                        default:
                            return null;
                    }

                    int nSamples;
                    switch (header.Channels)
                    {
                        case 0:
                            return null;
                        case 1:
                            l = asFloat;
                            r = null;
                            return header;
                        case 2:
                            // de-interleave
                            nSamples = nValues / 2;
                            l = new float[nSamples];
                            r = new float[nSamples];
                            for (int s = 0, v = 0; s < nSamples; s++)
                            {
                                l[s] = asFloat[v++];
                                r[s] = asFloat[v++];
                            }
                            return header;
                        default:
                            // de-interleave
                            nSamples = nValues / header.Channels;
                            l = new float[nSamples];
                            r = new float[nSamples];
                            for (int s = 0, v = 0; s < nSamples; s++)
                            {
                                l[s] = asFloat[v++];
                                r[s] = asFloat[v++];
                                v = v + header.Channels - 2;
                            }
                            return header;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("...Failed to load: " + filename);
                Debug.WriteLine(ex);
                return null;
            }
        }

        /// <summary>
        /// Read wave file. Code from https://stackoverflow.com/questions/8754111/how-to-read-the-data-in-a-wav-file-to-an-array/11162668#11162668
        /// Added search for data chunks to skip unknown headers like LIST from e.g. RIFF files
        /// http://soundfile.sapp.org/doc/WaveFormat/ for header reference
        /// 
        /// Reads wav file as mono
        /// </summary>
        /// <param name="filename">Path to file name</param>
        /// <param name="m">Output of left channel</param>
        /// <returns>Details of wav file</returns>
        public WavHeader ReadWav(string filename, out float[] m)
        {
            m = null;

            try
            {
                using (FileStream fs = File.Open(filename, FileMode.Open))
                {
                    BinaryReader reader = new BinaryReader(fs);
                    WavHeader header = new WavHeader();

                    // chunk 0
                    // reading int 32 as 4 bytes/chars for debugging header
                    char chunkId1 = reader.ReadChar();
                    char chunkId2 = reader.ReadChar();
                    char chunkId3 = reader.ReadChar();
                    char chunkId4 = reader.ReadChar();
                    header.FileSize = reader.ReadInt32();
                    header.RiffType = reader.ReadInt32();


                    // chunk 1
                    // reading int 32 as 4 bytes/chars for debugging header
                    char fmtId1 = reader.ReadChar();
                    char fmtId2 = reader.ReadChar();
                    char fmtId3 = reader.ReadChar();
                    char fmtId4 = reader.ReadChar();
                    int fmtSize = reader.ReadInt32(); // min size 16 (default/legacy)

                    // Additional info
                    int fmtCode = reader.ReadInt16();
                    header.Channels = reader.ReadInt16();
                    header.SampleRate = reader.ReadInt32();
                    int byteRate = reader.ReadInt32();
                    int fmtBlockAlign = reader.ReadInt16();
                    header.BitDepth = reader.ReadInt16();

                    if (fmtSize > 16)
                    {
                        // Read any extra values that are not important to us
                        int fmtExtraSize = reader.ReadInt16();
                        if (fmtExtraSize > 0) reader.ReadBytes(fmtExtraSize);
                    }

                    // chunk 2 can have more header data before data
                    byte[] byteArray = null;
                    int bytes = 0;
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        string dataId = new string(reader.ReadChars(4));
                        bytes = reader.ReadInt32();

                        if (dataId.ToLower() != "data")
                        {
                            reader.ReadBytes(bytes);
                        }
                        else
                        {
                            byteArray = reader.ReadBytes(bytes);
                            break;
                        }
                    }

                    if (byteArray == null) return null;

                    int bytesForSampling = header.BitDepth / 8;
                    int nValues = bytes / bytesForSampling + 1;


                    float[] asFloat;
                    switch (header.BitDepth)
                    {
                        case 64:
                            double[] asDouble = new double[nValues];
                            Buffer.BlockCopy(byteArray, 0, asDouble, 0, bytes);
                            asFloat = Array.ConvertAll(asDouble, e => (float)e);
                            break;
                        case 32:
                            asFloat = new float[nValues];
                            Buffer.BlockCopy(byteArray, 0, asFloat, 0, bytes);
                            break;
                        case 16:
                            short[]
                                asInt16 = new short[nValues];
                            Buffer.BlockCopy(byteArray, 0, asInt16, 0, bytes);
                            asFloat = Array.ConvertAll(asInt16, e => e / (float)(short.MaxValue + 1));
                            break;
                        default:
                            return null;
                    }

                    int nSamples;
                    switch (header.Channels)
                    {
                        case 0:
                            return null;
                        case 1:
                            m = asFloat;
                            return header;
                        case 2:
                            // de-interleave
                            nSamples = nValues / 2;
                            m = new float[nSamples];
                            for (int s = 0, v = 0; s < nSamples; s++)
                            {
                                m[s] = asFloat[v++];
                                v++;
                            }
                            return header;
                        default:
                            // de-interleave
                            nSamples = nValues / header.Channels;
                            m = new float[nSamples];
                            for (int s = 0, v = 0; s < nSamples; s++)
                            {
                                m[s] = asFloat[v++];
                                v = v + header.Channels - 1;
                            }
                            return header;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("...Failed to load: " + filename);
                Debug.WriteLine(ex);
                return null;
            }
        }
    }
}