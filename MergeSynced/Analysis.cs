using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace MergeSynced
{
    public class Analysis
    {
        /// <summary>
        /// https://stackoverflow.com/questions/70993291/calculate-the-crosscorrelation-of-two-vectors-more-efficiently
        /// https://dsp.stackexchange.com/questions/736/how-do-i-implement-cross-correlation-to-prove-two-audio-files-are-similar
        /// </summary>
        /// <param name="a">Dataset A</param>
        /// <param name="b">Dataset B</param>
        /// <param name="c">Cross correlation</param>
        /// <returns></returns>
        public static void CrossCorrelation(float[]? a, float[]? b, out float[]? c)
        {
            if (a == null || b == null)
            {
                c = null;
                return;
            }
            // Both arrays must be same size, if not, take smaller count
            int size = a.Length < b.Length ? a.Length : b.Length;

            // Convert data to complex type and calculate norm sqrt(sum(X.^2)) //////////
            Complex[] aComp = new Complex[size];
            Complex[] bComp = new Complex[size];
            Complex normA = Complex.Zero;
            Complex normB = Complex.Zero;

            for (int i = 0; i < size; i++)
            {
                aComp[i] = a[i];
                bComp[i] = b[i];

                normA += Complex.Pow(aComp[i], 2);
                normB += Complex.Pow(bComp[i], 2);
            }

            normA = Complex.Sqrt(normA);
            normB = Complex.Sqrt(normB);
            Complex multipliedNorm = Complex.Multiply(normA, normB);

            // Fourier transformation of A and B ////////////////////////////////////////
            //A
            Stopwatch sw = Stopwatch.StartNew();
            MathNet.Numerics.IntegralTransforms.Fourier.Forward(aComp);
            Debug.WriteLine($"{sw.ElapsedMilliseconds}ms for first FFT");
            sw.Restart();

            //B
            MathNet.Numerics.IntegralTransforms.Fourier.Forward(bComp);
            Debug.WriteLine($"{sw.ElapsedMilliseconds}ms for second FFT");
            sw.Restart();

            // Complex conjugation of B /////////////////////////////////////////////////
            for (int i = 0; i < size; i++)
            {
                bComp[i] = Complex.Conjugate(bComp[i]);
            }
            Debug.WriteLine($"{sw.ElapsedMilliseconds}ms complex conjugation");
            sw.Restart();

            // Multiply FFTs ////////////////////////////////////////////////////////////
            Complex[] multipliedFft = new Complex[size];
            for (int i = 0; i < size; i++)
            {
                multipliedFft[i] = Complex.Multiply(aComp[i], bComp[i]);
            }
            Debug.WriteLine($"{sw.ElapsedMilliseconds}ms for multiply FFTs");
            sw.Restart();

            // Inverse FFT //////////////////////////////////////////////////////////////
            MathNet.Numerics.IntegralTransforms.Fourier.Inverse(multipliedFft);
            Debug.WriteLine($"{sw.ElapsedMilliseconds}ms for inverse FFT");
            sw.Reset();

            // Norm and convert complex type back to input type /////////////////////////
            c = new float[size];
            for (int i = 0; i < size; i++)
            {
                // Normalize to unity and use absolute values used to avoid
                // checking for min values to get correct peak, also don't care if phase shifted?!
                c[i] = Math.Abs(Convert.ToSingle(Complex.Divide(multipliedFft[i], multipliedNorm).Real));
                //c[i] = Math.Abs(Convert.ToSingle(multipliedFft[i].Real));
            }
        }

        public static double CalculateDelay(float[]? corrData, int sampleRate)
        {
            float max = corrData!.Max();
            int maxIndex = Array.IndexOf(corrData!, max);
            double resFromLeft = (double)maxIndex / sampleRate;
            double resFromRight = (double)(corrData!.Length - maxIndex) / sampleRate;
            Debug.WriteLine($"{resFromLeft}s _delay (from left)");
            Debug.WriteLine($"{resFromRight}s _delay (from right)");

            if (resFromLeft < resFromRight) return -1 * resFromLeft;
            return resFromRight;
        }

    }
}