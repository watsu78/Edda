using Edda.Classes.Integrations;
using System;
using System.Threading.Tasks;

namespace Edda.Tests {
    internal static class Program {
        private static async Task Main() {
            ArchiveChecks.Run();
            await AccountChecks.RunAsync();
            await UploadChecks.RunAsync();
            // Exercise the real Windows vault using a unique test-only entry.
            // Never read or modify Edda's saved RagnaCustoms key.
            string target = $"Edda:Tests:RagnaCustoms:{Guid.NewGuid():N}";
            RagnaCustomsCredentialStore store = new(target);
            try {
                Require(store.Read() == null, "A missing credential should return null.");
                store.Delete();
                foreach (string invalid in new[] { null, "", " ", " key", "key ", "key\r\nvalue", new string('k', 1281) }) {
                    bool rejected = false;
                    try {
                        store.Save(invalid);
                    } catch (ArgumentException) {
                        rejected = true;
                    }
                    Require(rejected, "Invalid keys must be rejected.");
                }
                Require(store.Read() == null, "Invalid input must not create a credential.");
                store.Save("test-key-\u00e9-\u266b");
                Require(new RagnaCustomsCredentialStore(target).Read() == "test-key-\u00e9-\u266b", "A key should survive a new store instance without Unicode corruption.");
                store.Save("replacement-key");
                Require(store.Read() == "replacement-key", "Saving should replace the previous key.");
                store.Delete();
                Require(store.Read() == null, "Deleting should remove the key.");
                store.Delete();
                Console.WriteLine("PASS: missing key, validation, persistence, Unicode, replacement, deletion.");
            } finally {
                store.Delete();
            }
        }

        private static void Require(bool condition, string message) {
            if (!condition) {
                throw new InvalidOperationException(message);
            }
        }
    }
}