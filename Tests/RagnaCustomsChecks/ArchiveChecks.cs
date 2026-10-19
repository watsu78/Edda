using Edda.Classes.MapEditorNS;
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace Edda.Tests {
    internal static class ArchiveChecks {
        public static void Run() {
            string folder = Path.Combine(Path.GetTempPath(), "Edda-archive-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try {
                JsonNode info = JsonNode.Parse("""
                    {"_songFilename":"song.ogg","_coverImageFilename":"cover.png","_songTimeOffset":0,
                     "_difficultyBeatmapSets":[{"_difficultyBeatmaps":[{"_beatmapFilename":"Easy.dat"},{"_beatmapFilename":"Hard.dat"}]}]}
                    """);
                void SaveInfo() => File.WriteAllText(Path.Combine(folder, "info.dat"), info.ToJsonString());
                SaveInfo();
                foreach (string name in new[] { "song.ogg", "cover.png", "Easy.dat", "Hard.dat", "preview.ogg", "settings.txt", "unrelated.txt" }) {
                    File.WriteAllText(Path.Combine(folder, name), "test asset");
                }
                Directory.CreateDirectory(Path.Combine(folder, "autosaves"));
                File.WriteAllText(Path.Combine(folder, "autosaves", "old.dat"), "backup");
                string archivePath = Path.Combine(folder, "map.zip");
                MapArchiveBuilder builder = new((_, _) => throw new InvalidOperationException("Unexpected audio conversion."));
                builder.Create(folder, archivePath, false, true);
                using (ZipArchive zip = ZipFile.OpenRead(archivePath)) {
                    string[] expected = { "info.dat", "song.ogg", "cover.png", "Easy.dat", "Hard.dat", "preview.ogg" };
                    Require(zip.Entries.Select(entry => entry.FullName).OrderBy(name => name).SequenceEqual(expected.OrderBy(name => name)), "The ZIP must contain only map assets and every difficulty.");
                }
                File.Delete(archivePath);
                info["_songTimeOffset"] = 1250;
                SaveInfo();
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string audioArguments = null;
                MapArchiveBuilder converting = new((_, arguments) => {
                    audioArguments = arguments;
                    string output = arguments.Split('"')[^2];
                    File.WriteAllText(output, "processed audio");
                    return 0;
                });
                converting.Create(folder, archivePath, false, true);
                Require(audioArguments.Contains("atrim=start=1.25,"), "Audio offset must use an invariant decimal separator.");
                using (ZipArchive zip = ZipFile.OpenRead(archivePath)) {
                    using StreamReader reader = new(zip.GetEntry("info.dat").Open());
                    Require((double)JsonNode.Parse(reader.ReadToEnd())["_songTimeOffset"] == 0, "The exported offset must be reset after baking audio.");
                }
                Require((double)JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "info.dat")))["_songTimeOffset"] == 1250, "Export must not modify source metadata.");
                Require(File.ReadAllText(Path.Combine(folder, "song.ogg")) == "test asset", "Export must not modify source audio.");
                File.Delete(archivePath);
                info["_songTimeOffset"] = -500;
                SaveInfo();
                converting.Create(folder, archivePath, false, true);
                Require(audioArguments.Contains("adelay=500|500"), "A negative offset must insert silence.");
                File.Delete(archivePath);
                builder.Create(folder, archivePath, true, true);
                File.Delete(archivePath);
                MapArchiveBuilder failing = new((_, _) => 1);
                ExpectFailure(() => failing.Create(folder, archivePath, false, true), "Failed audio processing must prevent upload.");
                info["_songTimeOffset"] = 0;
                info["_songFilename"] = "../outside.ogg";
                SaveInfo();
                ExpectFailure(() => builder.Create(folder, archivePath, false, true), "Assets outside the map folder must be rejected.");
                info["_songFilename"] = "missing.ogg";
                SaveInfo();
                ExpectFailure(() => builder.Create(folder, archivePath, false, true), "A missing audio file must prevent upload.");
                info["_songFilename"] = "song.ogg";
                info["_difficultyBeatmapSets"][0]["_difficultyBeatmaps"] = new JsonArray();
                SaveInfo();
                ExpectFailure(() => builder.Create(folder, archivePath, false, true), "A map without difficulties must be rejected.");
                info["_difficultyBeatmapSets"][0]["_difficultyBeatmaps"] = JsonNode.Parse("[{\"_beatmapFilename\":\"Easy.dat\"}]");
                SaveInfo();
                using CancellationTokenSource cancelled = new();
                cancelled.Cancel();
                bool observedCancellation = false;
                try {
                    builder.Create(folder, archivePath, false, true, cancelled.Token);
                } catch (OperationCanceledException) {
                    observedCancellation = true;
                }
                Require(observedCancellation && !File.Exists(archivePath), "Cancellation must prevent creating a ZIP.");
                Console.WriteLine("PASS: ZIP assets, difficulties, offsets, source preservation, validation, cancellation.");
            } finally {
                CultureInfo.CurrentCulture = originalCulture;
                Directory.Delete(folder, true);
            }
        }

        private static void ExpectFailure(Action action, string message) {
            bool failed = false;
            try {
                action();
            } catch (InvalidDataException) {
                failed = true;
            } catch (IOException) {
                failed = true;
            }
            Require(failed, message);
        }

        private static void Require(bool condition, string message) {
            if (!condition) {
                throw new InvalidOperationException(message);
            }
        }
    }
}