using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Edda {
    /// <summary>
    /// Interaction logic for BPMCalcWindow.xaml
    /// </summary>
    public partial class BPMCalcWindow : Window {

        Stopwatch stopwatch;
        List<long> intervalSamples;
        int numInputs = 0;
        long prevTime = 0;

        public BPMCalcWindow() {
            InitializeComponent();
            stopwatch = new();
            intervalSamples = new();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e) {
            // reset variables
            lblAvgBPM.Content = 0;
            lblUnroundedAvgBPM.Content = "(0.00)";
            //lblMedBPM.Content = 0;
            prevTime = 0;
            numInputs = 0;
            lblInputCounter.Content = numInputs;
            intervalSamples.Clear();

            stopwatch.Reset();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e) {
            // start timer
            if (!stopwatch.IsRunning) {
                stopwatch.Start();
            } else {

                // count an input
                long now = stopwatch.ElapsedMilliseconds;
                intervalSamples.Add(now - prevTime);

                // increment input counter
                numInputs++;
                lblInputCounter.Content = numInputs;

                prevTime = now;

                // calculate BPM
                CalculateBPM();
            }
        }
        private void CalculateBPM() {
            int numSamples = intervalSamples.Count;

            if (numSamples == 0) {
                return;
            }

            intervalSamples.Sort();

            // calculate mean
            double avgInterval = intervalSamples.Sum() / (double)intervalSamples.Count;

            /*
            // calculate median
            double medInterval;
            if (numSamples % 2 == 1) { // middle element exists
                medInterval = intervalSamples[numSamples / 2];
            } else { // take average of two middle elements
                medInterval = 0.5 * (intervalSamples[numSamples / 2] + intervalSamples[numSamples / 2 - 1]);
            }
            */
            lblUnroundedAvgBPM.Content = "(" + (60000 / avgInterval).ToString("0.00") + ")";
            lblAvgBPM.Content = (60000 / avgInterval).ToString("0.");
            //lblMedBPM.Content = (60000 / medInterval).ToString("0.00");
        }

        // BPM estimation
        private void BtnAutoBPM_Click(object sender, RoutedEventArgs e)
        {
            // Get audio file path from MainWindow
            var mainWin = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWin == null || mainWin.songStream == null)
            {
                lblAutoBPM.Content = "Auto: N/A";
                MessageBox.Show("No audio loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Onset detection (slope-based)
                List<double> onsetTimes = new List<double>();
                var reader = mainWin.songStream;
                int sampleRate = reader.WaveFormat.SampleRate;
                int channels = reader.WaveFormat.Channels;
                int totalSamples = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8) / channels);
                float[] samples = new float[totalSamples];
                reader.Position = 0;
                int read = 0;
                int offset = 0;
                var buffer = new float[1024 * channels];
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0 && offset < samples.Length)
                {
                    for (int i = 0; i < read; i += channels)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; c++)
                            sum += buffer[i + c];
                        samples[offset++] = sum / channels;
                    }
                }
                // Slope-based onset detection
                int wh = sampleRate / 20; // window half-width (20Hz)
                if (samples.Length < wh * 2)
                    throw new Exception("Audio too short for onset detection.");
                float[] slopes = new float[samples.Length];
                // Initial sums
                float sumL = 0, sumR = 0;
                for (int i = 0, j = wh; i < wh; ++i, ++j)
                {
                    sumL += Math.Abs(samples[i]);
                    sumR += Math.Abs(samples[j]);
                }
                float scalar = 1.0f / wh;
                for (int i = wh, end = samples.Length - wh; i < end; ++i)
                {
                    slopes[i] = Math.Max(0.0f, (sumR - sumL) * scalar);
                    float cur = Math.Abs(samples[i]);
                    sumL -= Math.Abs(samples[i - wh]);
                    sumL += cur;
                    sumR -= cur;
                    sumR += Math.Abs(samples[i + wh]);
                }
                // Find local maxima above threshold
                float meanSlope = slopes.Average();
                float stdSlope = (float)Math.Sqrt(slopes.Skip(wh).Take(samples.Length - 2 * wh).Select(s => (s - meanSlope) * (s - meanSlope)).Average());
                float thresholdSlope = meanSlope + stdSlope * 1.5f;
                for (int i = wh + 1; i < samples.Length - wh - 1; i++)
                {
                    if (slopes[i] > thresholdSlope && slopes[i] > slopes[i - 1] && slopes[i] > slopes[i + 1])
                        onsetTimes.Add(i / (double)sampleRate);
                }
                // Interval testing and fitness normalization
                // BPM range and interval
                double minBPM = 89.0, maxBPM = 205.0;
                int numOnsets = onsetTimes.Count;
                if (numOnsets < 2)
                {
                    lblAutoBPM.Content = "Auto: N/A";
                    return;
                }
                List<(double bpm, double fitness)> bpmCandidates = new List<(double, double)>();
                // Test BPMs in range
                for (double bpm = minBPM; bpm <= maxBPM; bpm += 0.1)
                {
                    double interval = 60.0 / bpm;
                    // Build histogram of onset times modulo interval
                    int bins = (int)Math.Round(interval * sampleRate);
                    double[] hist = new double[bins];
                    foreach (var t in onsetTimes)
                    {
                        int pos = (int)Math.Round((t * sampleRate) % bins);
                        if (pos >= 0 && pos < bins)
                            hist[pos] += 1.0;
                    }
                    // Fitness: max value in histogram
                    double fitness = hist.Max();
                    bpmCandidates.Add((bpm, fitness));
                }
                // Normalize fitness
                double fitnessSum = bpmCandidates.Sum(x => x.Item2);
                double maxFitness = bpmCandidates.Max(x => x.Item2);
                List<(double, double)> normalized;
                if (fitnessSum > 0)
                    normalized = bpmCandidates.Select(x => (x.Item1, x.Item2 / fitnessSum)).ToList();
                else
                    normalized = bpmCandidates.Select(x => (x.Item1, x.Item2 / maxFitness)).ToList();
                // Filter near-duplicates and multiples
                List<(double bpm, double fitness)> filtered = new List<(double, double)>();
                foreach (var cand in normalized.OrderByDescending(x => x.Item2))
                {
                    bool duplicate = filtered.Any(f => Math.Abs(f.Item1 - cand.Item1) < 0.1 || Math.Abs(f.Item1 * 2 - cand.Item1) < 0.1 || Math.Abs(f.Item1 / 2 - cand.Item1) < 0.1);
                    if (!duplicate)
                        filtered.Add(cand);
                    if (filtered.Count == 3) break;
                }
                // Display top 3 distinct BPMs with normalized confidence
                // Normalize confidence only among top 3
                double sumTop = filtered.Sum(x => x.Item2);
                List<string> bpmResults = new List<string>();
                foreach (var r in filtered)
                {
                    double conf = sumTop > 0 ? (r.Item2 / sumTop) : 0;
                    bpmResults.Add($"{r.Item1:F2} ({conf * 100:F0}%)");
                }
                lblAutoBPM.Content = "Auto: " + string.Join(" | ", bpmResults);
            }
            catch (Exception ex)
            {
                lblAutoBPM.Content = "Auto: N/A";
                MessageBox.Show($"Error during BPM estimation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}