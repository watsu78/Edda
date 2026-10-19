using Edda.Classes.Integrations;
using Edda.Classes.MapEditorNS;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Edda.Windows {
    public partial class RagnaCustomsUploadWindow : Window {
        private readonly string mapFolder;
        private readonly bool offsetAppliedToAudio;
        private readonly Action saveMap;
        private CancellationTokenSource cancellation;
        private Uri songUri;
        private bool closeRequested;
        private bool uploaded;

        public RagnaCustomsUploadWindow(string mapFolder, bool offsetAppliedToAudio, Action saveMap) {
            InitializeComponent();
            this.mapFolder = mapFolder;
            this.offsetAppliedToAudio = offsetAppliedToAudio;
            this.saveMap = saveMap;
        }

        private void ManageAccount_Click(object sender, RoutedEventArgs e) {
            new RagnaCustomsAccountWindow { Owner = this }.ShowDialog();
            AccountStatus.Text = "Your account and Premium tier will be checked before upload.";
        }

        private async void Upload_Click(object sender, RoutedEventArgs e) {
            if (cancellation != null || uploaded) {
                return;
            }
            string uploadFolder = Path.Combine(Path.GetTempPath(), "Edda-upload-" + Guid.NewGuid().ToString("N"));
            cancellation = new CancellationTokenSource();
            UploadButton.IsEnabled = false;
            AccountButton.IsEnabled = false;
            CloseButton.Content = "Cancel";
            Progress.Visibility = Visibility.Visible;
            bool uploadStarted = false;
            try {
                string apiKey;
                try {
                    apiKey = new RagnaCustomsCredentialStore().Read();
                } catch (Win32Exception) {
                    Status.Text = "Could not access Windows Credential Manager. Save your API key again in Settings.";
                    return;
                }
                if (string.IsNullOrEmpty(apiKey)) {
                    Status.Text = "Save your API key using Manage API key first.";
                    return;
                }
                Status.Text = "Checking your RagnaCustoms account and Premium tier...";
                using RagnaCustomsClient client = new();
                RagnaCustomsAccount account = await client.GetAccountAsync(apiKey, cancellation.Token);
                AccountStatus.Text = account.Summary;
                if (!account.CanUpload) {
                    Status.Text = account.IsPremium && !account.PremiumTier.HasValue
                        ? "RagnaCustoms did not return your Premium tier. Check your account again before uploading."
                        : "Upload requires Premium Tier 2 or higher. This account is not eligible.";
                    return;
                }
                Status.Text = "Saving and preparing the map ZIP...";
                saveMap();
                Directory.CreateDirectory(uploadFolder);
                string archivePath = Path.Combine(uploadFolder, "map.zip");
                MapArchiveBuilder builder = new(Helper.FFmpeg);
                await Task.Run(() => builder.Create(mapFolder, archivePath, offsetAppliedToAudio, true, cancellation.Token));
                Status.Text = "Uploading the map to RagnaCustoms...";
                uploadStarted = true;
                RagnaCustomsUploadResult result = await client.UploadAsync(archivePath, apiKey, cancellation.Token);
                uploaded = true;
                songUri = result.SongUri;
                OpenMapButton.Visibility = songUri == null ? Visibility.Collapsed : Visibility.Visible;
                Status.Text = $"Map uploaded as WIP (ID {result.Id}). Review it on RagnaCustoms before publishing.";
            } catch (OperationCanceledException) {
                Status.Text = uploadStarted
                    ? "Upload cancelled or timed out. Check your RagnaCustoms account before trying again; the server may have received the map."
                    : "The account check or map preparation was cancelled or timed out. No map was uploaded.";
            } catch (HttpRequestException ex) {
                Status.Text = ex.StatusCode.HasValue
                    ? ex.Message
                    : uploadStarted ? "Could not complete the upload. Check your connection and your RagnaCustoms account before trying again."
                    : "Could not check your account. Check your connection and try again. No map was uploaded.";
            } catch (InvalidDataException ex) {
                Status.Text = ex.Message;
            } catch (IOException ex) {
                Status.Text = "Could not prepare or read the map ZIP. " + ex.Message;
            } catch (Exception) {
                Status.Text = "Could not complete the upload. Check the map files and your RagnaCustoms account before trying again.";
            } finally {
                try {
                    if (Directory.Exists(uploadFolder)) {
                        Directory.Delete(uploadFolder, true);
                    }
                } catch (IOException) {
                    Status.Text += " The temporary upload ZIP could not be removed.";
                } catch (UnauthorizedAccessException) {
                    Status.Text += " The temporary upload ZIP could not be removed.";
                }
                cancellation.Dispose();
                cancellation = null;
                UploadButton.IsEnabled = !uploaded;
                AccountButton.IsEnabled = true;
                CloseButton.Content = "Close";
                Progress.Visibility = Visibility.Collapsed;
                if (closeRequested) {
                    Close();
                }
            }
        }

        private void OpenMap_Click(object sender, RoutedEventArgs e) {
            if (songUri != null) {
                Helper.OpenWebUrl(songUri.AbsoluteUri);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e) {
            if (cancellation != null) {
                e.Cancel = true;
                closeRequested = true;
                Status.Text = "Cancelling the upload...";
                cancellation.Cancel();
            }
        }
    }
}