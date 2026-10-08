using System;
using System.Collections.Generic;
using System.IO;

namespace IWannabe.Rhythm.EditorTools
{
    public enum ClickKind
    {
        Beat,
        Downbeat,
        /// <summary>누름(탭, 홀드 시작).</summary>
        Press,
        /// <summary>홀드 뗌.</summary>
        Release,
    }

    /// <summary>점검용 WAV 읽기·쓰기와 클릭 덧입히기. 귀로 비트·노트 위치를 확인할 때 쓴다.</summary>
    public static class AudioFiles
    {
        /// <summary>모노 16비트 PCM WAV로 쓴다.</summary>
        public static void WriteWav(string path, float[] samples, int sampleRate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + bytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(bytes);
                foreach (var s in samples) writer.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, s)) * 32767));
            }
        }

        /// <summary>16비트 PCM 또는 32비트 float WAV를 모노로 읽는다.</summary>
        public static float[] ReadWav(string path, out int sampleRate)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException($"WAV가 아닙니다: {path}");
                reader.ReadInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException($"WAV가 아닙니다: {path}");

                int channels = 1, bits = 16, format = 1;
                sampleRate = 44100;
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    string id = new string(reader.ReadChars(4));
                    int size = reader.ReadInt32();
                    if (id == "fmt ")
                    {
                        format = reader.ReadInt16();
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt16();
                        bits = reader.ReadInt16();
                        reader.BaseStream.Seek(size - 16, SeekOrigin.Current);
                    }
                    else if (id == "data")
                    {
                        int frames = size / (bits / 8) / channels;
                        var mono = new float[frames];
                        for (int i = 0; i < frames; i++)
                        {
                            float sum = 0;
                            for (int c = 0; c < channels; c++)
                                sum += format == 3 && bits == 32 ? reader.ReadSingle() : reader.ReadInt16() / 32768f;
                            mono[i] = sum / channels;
                        }
                        return mono;
                    }
                    else
                    {
                        reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
                    }
                }
                throw new InvalidDataException($"data 청크가 없습니다: {path}");
            }
        }

        /// <summary>
        /// 음악을 줄이고 클릭을 얹어 반 샘플레이트로 줄인 소리를 만든다(파일 크기를 줄이려고).
        /// 비트는 낮은 틱, 마디 첫 박은 조금 높은 틱, 누름은 높고 또렷한 소리, 홀드 뗌은 그보다 작은 소리다.
        /// </summary>
        public static float[] Overlay(float[] music, int sampleRate, IEnumerable<(double time, ClickKind kind)> clicks, out int outputRate,
            float musicGain = 0.55f)
        {
            var mix = new float[music.Length];
            for (int i = 0; i < mix.Length; i++) mix[i] = music[i] * musicGain;

            foreach (var (time, kind) in clicks)
            {
                double frequency, decay;
                float amp;
                switch (kind)
                {
                    case ClickKind.Downbeat: frequency = 1600; decay = 0.012; amp = 0.45f; break;
                    case ClickKind.Press: frequency = 2600; decay = 0.02; amp = 0.6f; break;
                    case ClickKind.Release: frequency = 1900; decay = 0.012; amp = 0.3f; break;
                    default: frequency = 1000; decay = 0.01; amp = 0.35f; break;
                }
                int start = (int)Math.Round(time * sampleRate), length = (int)(decay * 6 * sampleRate);
                for (int n = 0; n < length; n++)
                {
                    int i = start + n;
                    if (i < 0 || i >= mix.Length) continue;
                    double s = n / (double)sampleRate;
                    mix[i] += (float)(amp * Math.Exp(-s / decay) * Math.Sin(2 * Math.PI * frequency * s));
                }
            }

            outputRate = sampleRate / 2;
            var half = new float[mix.Length / 2];
            for (int i = 0; i < half.Length; i++) half[i] = 0.5f * (mix[2 * i] + mix[2 * i + 1]);
            return half;
        }
    }
}
