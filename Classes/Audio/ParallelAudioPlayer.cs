using Edda.Const;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;

public class ParallelAudioPlayer : IDisposable {
    const int numChannels = 4;
    const float maxPan = Audio.MaxPanDistance;
    int streams;
    int uniqueSamples;
    int lastPlayedStream;
    int desiredLatency;
    string basePath;
    MMDevice playbackDevice;
    DateTime[] lastPlayedTimes;
    AudioFileReader[] noteStreams;
    WasapiOut[] notePlayers;
    public bool isEnabled { get; set; }
    public bool isPanned { get; set; }
    float[] channelVolumes = new float[numChannels];
    float masterVolume = 1.0f;

    public ParallelAudioPlayer(MMDevice playbackDevice, string basePath, int streams, int desiredLatency, bool isEnabled, bool isPanned, float defaultVolume) {
        lastPlayedStream = 0;
        this.streams = streams;
        this.isEnabled = isEnabled;
        this.isPanned = isPanned;
        this.desiredLatency = desiredLatency;
        this.basePath = basePath;
        this.playbackDevice = playbackDevice;
        uniqueSamples = 0;
        while (File.Exists(GetFilePath(basePath, uniqueSamples + 1))) {
            uniqueSamples++;
        }
        if (uniqueSamples < 1) {
            throw new FileNotFoundException($"Couldn't find the file {GetFilePath(basePath, uniqueSamples + 1)}");
        }
        masterVolume = Math.Max(0f, Math.Min(1f, defaultVolume));
        for (int c = 0; c < numChannels; c++) {
            channelVolumes[c] = 1.0f;
        }
        InitAudioOut(masterVolume);
    }

    public void InitAudioOut(float defaultVolume) {
        noteStreams = new AudioFileReader[streams];
        notePlayers = new WasapiOut[streams];
        lastPlayedTimes = new DateTime[streams];
        for (int i = 0; i < streams; i++) {
            noteStreams[i] = new AudioFileReader(GetFilePath(basePath, (i % numChannels % uniqueSamples) + 1)) {
                Volume = channelVolumes[i % numChannels] * masterVolume
            };
            notePlayers[i] = new WasapiOut(playbackDevice, AudioClientShareMode.Shared, true, desiredLatency);
            if (isPanned && basePath != "mmatick") {
                var mono = new StereoToMonoSampleProvider(noteStreams[i]);
                if (basePath == "bassdrum") {
                    mono.LeftVolume = 1.0f;
                    mono.RightVolume = 0.0f;
                } else {
                    mono.LeftVolume = 0.5f;
                    mono.RightVolume = 0.5f;
                }

                var panProv = new PanningSampleProvider(mono);
                panProv.Pan = i % numChannels * 2 * maxPan / (numChannels - 1) - maxPan;
                notePlayers[i].Init(panProv);
            } else {
                notePlayers[i].Init(noteStreams[i]);
            }
        }
    }
    public ParallelAudioPlayer(MMDevice playbackDevice, string basePath, int streams, int desiredLatency, bool isPanned, float defaultVolume) : this(playbackDevice, basePath, streams, desiredLatency, true, isPanned, defaultVolume) { }

    public virtual bool Play() {
        return Play(0);
    }

    public virtual bool Play(int channel) {
        if (!isEnabled) {
            return true;
        }
        for (int i = 0; i < streams; i++) {
            if (isPanned && i % numChannels != channel) {
                continue;
            }
            // check that the stream is available to play
            DateTime now = DateTime.Now;
            if (now - lastPlayedTimes[i] > noteStreams[i].TotalTime) {
                notePlayers[i].Pause();
                noteStreams[i].CurrentTime = TimeSpan.Zero;

                notePlayers[i].Play();
                this.lastPlayedStream = i;
                lastPlayedTimes[i] = now;
                return true;
            }
        }
        return false;
    }
    public void ChangeVolume(double vol) {
        masterVolume = (float)Math.Min(Math.Abs(vol), 1);
        for (int i = 0; i < streams; i++) {
            noteStreams[i].Volume = masterVolume * channelVolumes[i % numChannels];
        }
    }
    public void ChangeChannelVolume(int channel, double vol) {
        if (channel < 0 || channel >= numChannels) return;
        var v = (float)Math.Min(Math.Abs(vol), 1);
        channelVolumes[channel] = v;
        for (int i = 0; i < streams; i++) {
            if (i % numChannels == channel) {
                noteStreams[i].Volume = masterVolume * v;
            }
        }
    }
    public void Dispose() {
        for (int i = 0; i < this.streams; i++) {
            noteStreams[i].Dispose();
            notePlayers[i].Dispose();
        }
        noteStreams = null;
        notePlayers = null;
        playbackDevice = null;
    }
    private string GetFilePath(string basePath, int sampleNumber) {
        return $"{Program.ResourcesPath}{basePath}{sampleNumber}.wav";
    }
}