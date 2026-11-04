using Edda.Classes.Integrations;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Edda.Windows {
    public partial class RagnaCustomsAccountWindow : Window {
        private readonly RagnaCustomsCredentialStore credentials = new();
        private CancellationTokenSource accountCheck;

        public RagnaCustomsAccountWindow() {
            InitializeComponent();
            try {
                Status.Text = string.IsNullOrEmpty(credentials.Read())
                    ? "No API key saved."
                    : "An API key is saved. Enter a new key only to replace it.";
            } catch (Exception) {
                Status.Text = "Could not read Windows Credential Manager. Try saving your key again.";
            }
        }

        private void OpenAccount_Click(object sender, RoutedEventArgs e) {
            Helper.OpenWebUrl("https://ragnacustoms.com/account");
        }

        private async void SaveKey_Click(object sender, RoutedEventArgs e) {
            try {
                credentials.Save(ApiKey.Password);
                ApiKey.Clear();
                Status.Text = "API key saved. You can now close this window.";
            } catch (ArgumentException ex) {
                Status.Text = ex.Message;
                return;
            } catch (Exception) {
                Status.Text = "Could not save the API key in Windows Credential Manager.";
                return;
            }
            await CheckAccountAsync();
        }

        private void RemoveKey_Click(object sender, RoutedEventArgs e) {
            try {
                credentials.Delete();
                ApiKey.Clear();
                Status.Text = "No API key saved.";
                AccountStatus.Text = "No account checked.";
            } catch (Exception) {
                Status.Text = "Could not remove the API key from Windows Credential Manager.";
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) {
            await CheckAccountAsync();
        }

        private async void CheckAccount_Click(object sender, RoutedEventArgs e) {
            await CheckAccountAsync();
        }

        private async Task CheckAccountAsync() {
            if (accountCheck != null) {
                return;
            }
            accountCheck = new CancellationTokenSource();
            SaveButton.IsEnabled = RemoveButton.IsEnabled = CheckButton.IsEnabled = ApiKey.IsEnabled = false;
            try {
                string key = credentials.Read();
                if (string.IsNullOrEmpty(key)) {
                    AccountStatus.Text = "Save an API key to check the associated account.";
                    return;
                }
                AccountStatus.Text = "Checking your RagnaCustoms account...";
                using RagnaCustomsClient client = new();
                RagnaCustomsAccount account = await client.GetAccountAsync(key, accountCheck.Token);
                AccountStatus.Text = account.Summary + (account.CanUpload
                    ? "\nEligible for map uploads."
                    : account.IsPremium && !account.PremiumTier.HasValue ? "\nThe account API did not return a Premium tier."
                    : "\nMap uploads require Premium Tier 2 or higher.");
            } catch (OperationCanceledException) {
                AccountStatus.Text = "Account check cancelled or timed out. Your saved key has not changed.";
            } catch (HttpRequestException ex) {
                AccountStatus.Text = ex.StatusCode.HasValue ? ex.Message : "Could not check your account. Check your connection and try again.";
            } catch (InvalidDataException ex) {
                AccountStatus.Text = ex.Message;
            } catch (Exception) {
                AccountStatus.Text = "Could not check the saved account. Try again.";
            } finally {
                accountCheck.Dispose();
                accountCheck = null;
                SaveButton.IsEnabled = RemoveButton.IsEnabled = CheckButton.IsEnabled = ApiKey.IsEnabled = true;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e) {
            accountCheck?.Cancel();
        }
    }
}