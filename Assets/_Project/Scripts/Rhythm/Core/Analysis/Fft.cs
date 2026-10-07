using System;

namespace IWannabe.Rhythm.Charting
{
    /// <summary>반복형 radix-2 복소 FFT. 크기는 2의 거듭제곱이어야 한다.</summary>
    internal sealed class Fft
    {
        readonly int size;
        readonly int[] reversed;
        readonly float[] cos;
        readonly float[] sin;

        public Fft(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0)
                throw new ArgumentException("FFT 크기는 2의 거듭제곱이어야 합니다.", nameof(size));

            this.size = size;
            int bits = 0;
            while ((1 << bits) < size) bits++;

            reversed = new int[size];
            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    r |= ((i >> b) & 1) << (bits - 1 - b);
                reversed[i] = r;
            }

            cos = new float[size / 2];
            sin = new float[size / 2];
            for (int k = 0; k < size / 2; k++)
            {
                double angle = 2.0 * Math.PI * k / size;
                cos[k] = (float)Math.Cos(angle);
                sin[k] = (float)Math.Sin(angle);
            }
        }

        public void Forward(float[] re, float[] im)
        {
            for (int i = 0; i < size; i++)
            {
                int j = reversed[i];
                if (j <= i) continue;
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }

            for (int length = 2; length <= size; length <<= 1)
            {
                int half = length >> 1;
                int stride = size / length;
                for (int start = 0; start < size; start += length)
                {
                    for (int k = 0; k < half; k++)
                    {
                        float wr = cos[k * stride];
                        float wi = -sin[k * stride];
                        int a = start + k;
                        int b = a + half;
                        float tr = wr * re[b] - wi * im[b];
                        float ti = wr * im[b] + wi * re[b];
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
                }
            }
        }
    }
}
