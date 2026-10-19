using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Edda.Classes.MapEditorNS {
    // Shared by local export and RagnaCustoms upload. Only playable map assets
    // belong in the ZIP, never autosaves, caches or editor settings.
    public sealed class MapArchiveBuilder {
        private readonly Func<string, string, int> processAudio;

        public MapArchiveBuilder(Func<string, string, int> processAudio) {
            this.processAudio = processAudio;
        }

        public void Create(string mapFolder, string archivePath, bool offsetAppliedToAudio, bool requireCompleteMap, CancellationToken cancellationToken = default, Action<string> reportWarning = null) {
            JsonNode info = JsonNode.Parse(File.ReadAllText(Path.Combine(mapFolder, "info.dat")));
            string song = (string)info["_songFilename"];
            string cover = (string)info["_coverImageFilename"];
            HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
            void AddAsset(string name, bool required) {
                if (string.IsNullOrWhiteSpace(name)) {
                    if (required) {
                        throw new InvalidDataException("The map is missing its audio, cover or difficulty filename.");
                    }
                    return;
                }
                // Map filenames must stay within the map folder and must match
                // their names in info.dat after being archived.
                if (name != Path.GetFileName(name) || name.Contains('"') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
                    throw new InvalidDataException("Map assets must use filenames inside the map folder.");
                }
                if (!File.Exists(Path.Combine(mapFolder, name))) {
                    if (required) {
                        throw new FileNotFoundException($"The map asset '{name}' is missing.");
                    }
                    return;
                }
                files.Add(name);
            }

            AddAsset(song, requireCompleteMap);
            AddAsset(cover, requireCompleteMap);
            AddAsset("preview.ogg", false);
            int difficulties = 0;
            foreach (JsonNode set in info["_difficultyBeatmapSets"].AsArray()) {
                foreach (JsonNode difficulty in set["_difficultyBeatmaps"].AsArray()) {
                    AddAsset((string)difficulty["_beatmapFilename"], true);
                    difficulties++;
                }
            }
            if (requireCompleteMap && difficulties == 0) {
                throw new InvalidDataException("Add at least one difficulty before uploading the map.");
            }

            string stagingFolder = Path.Combine(Path.GetTempPath(), "Edda-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingFolder);
            try {
                foreach (string name in files) {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Copy(Path.Combine(mapFolder, name), Path.Combine(stagingFolder, name));
                }
                if (!string.IsNullOrEmpty(song) && files.Contains(song)) {
                    double.TryParse(info["_songTimeOffset"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double offsetMs);
                    if (!double.IsFinite(offsetMs)) {
                        throw new InvalidDataException("The song time offset must be a finite number.");
                    }
                    if (!offsetAppliedToAudio && Math.Abs(offsetMs) > 0.5) {
                        string source = Path.Combine(mapFolder, song);
                        string baked = Path.Combine(stagingFolder, song);
                        string filter = offsetMs > 0
                            ? "atrim=start=" + (offsetMs / 1000).ToString("R", CultureInfo.InvariantCulture) + ",asetpts=PTS-STARTPTS"
                            : "adelay=" + Math.Round(Math.Abs(offsetMs)).ToString(CultureInfo.InvariantCulture) + "|" + Math.Round(Math.Abs(offsetMs)).ToString(CultureInfo.InvariantCulture);
                        File.Delete(baked);
                        int exit = processAudio(mapFolder, $"-i \"{source}\" -y -af \"{filter}\" -ar 44100 -c:a libvorbis -q:a 6 \"{baked}\"");
                        cancellationToken.ThrowIfCancellationRequested();
                        if (exit != 0 || !File.Exists(baked)) {
                            if (requireCompleteMap) {
                                throw new IOException("Could not process the song time offset. The map has not been uploaded.");
                            }
                            reportWarning?.Invoke("Failed to process audio for export. Exporting the original file instead.");
                            File.Copy(source, baked, true);
                        } else {
                            info["_songTimeOffset"] = 0;
                        }
                    } else {
                        info["_songTimeOffset"] = 0;
                    }
                }
                File.WriteAllText(Path.Combine(stagingFolder, "info.dat"), info.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                cancellationToken.ThrowIfCancellationRequested();
                ZipFile.CreateFromDirectory(stagingFolder, archivePath);
            } finally {
                Directory.Delete(stagingFolder, true);
            }
        }
    }
}