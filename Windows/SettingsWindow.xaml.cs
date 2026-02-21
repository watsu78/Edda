using Edda.Const;
using Microsoft.WindowsAPICodePack.Dialogs;
using NAudio.CoreAudioApi;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Path = System.IO.Path;

namespace Edda {
    /// <summary>
    /// Interaction logic for WindowSettings.xaml
    /// </summary>
    public partial class SettingsWindow : Window {
        MainWindow caller;
        UserSettingsManager userSettings;
        bool doneInit = false;
        public SettingsWindow(MainWindow caller, UserSettingsManager userSettings) {
            InitializeComponent();
            this.caller = caller;
            this.userSettings = userSettings;
            InitComboPlaybackDevices();
            InitComboDrumSample();
            txtDefaultMapper.Text = userSettings.GetValueForKey(UserSettingsKey.DefaultMapper);
            txtDefaultNoteSpeed.Text = userSettings.GetValueForKey(UserSettingsKey.DefaultNoteSpeed);
            txtDefaultGridSpacing.Text = userSettings.GetValueForKey(UserSettingsKey.DefaultGridSpacing);
            InitComboNotePasteBehavior();
            txtAudioLatency.Text = userSettings.GetValueForKey(UserSettingsKey.EditorAudioLatency);
            checkPanNotes.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.PanDrumSounds);
            sliderSongVol.Value = float.Parse(userSettings.GetValueForKey(UserSettingsKey.DefaultSongVolume));
            sliderDrumVol.Value = float.Parse(userSettings.GetValueForKey(UserSettingsKey.DefaultNoteVolume));
            checkDiscord.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableDiscordRPC);
            CheckAutosave.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableAutosave);
            CheckShowSpectrogram.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableSpectrogram);
            ToggleSpectrogramOptionsVisibility();
            InitComboSpectrogramType();
            InitComboSpectrogramQuality();
            InitComboSpectrogramColormap();
            checkSpectrogramCache.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.SpectrogramCache);
            txtSpectrogramFrequency.Text = userSettings.GetValueForKey(UserSettingsKey.SpectrogramFrequency);
            checkSpectrogramFlipped.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.SpectrogramFlipped);
            checkSpectrogramChunking.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.SpectrogramChunking);
            checkStartupUpdate.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.CheckForUpdates);
            var savedMapPath = userSettings.GetValueForKey(UserSettingsKey.MapSaveLocationPath);
            txtMapSaveFolderPath.Text = string.IsNullOrEmpty(savedMapPath) ? Program.DocumentsMapFolder : savedMapPath;
            txtAudioQualityConversion.Text = userSettings.GetValueForKey("ExportQuality") ?? "6";
            doneInit = true;
        }

        private void InitComboPlaybackDevices() {
            int i;
            if (caller.defaultDeviceAvailable) {
                i = comboPlaybackDevice.Items.Add(new PlaybackDevice(null, "Default"));
                comboPlaybackDevice.SelectedIndex = i;
            }
            foreach (var device in caller.availablePlaybackDevices) {
                // Having MMDevice as Item lags the ComboBox quite a bit, so we use a simple data class instead.
                i = comboPlaybackDevice.Items.Add(new PlaybackDevice(device));
                if (!caller.playingOnDefaultDevice && device.ID == caller.playbackDeviceID) {
                    comboPlaybackDevice.SelectedIndex = i;
                }
            }
            if (!comboPlaybackDevice.HasItems) {
                comboPlaybackDevice.IsEnabled = false;
            }
        }
        private void TxtAudioLatency_LostFocus(object sender, RoutedEventArgs e) {
            double latency;
            double prevLatency = double.Parse(userSettings.GetValueForKey(UserSettingsKey.EditorAudioLatency));
            if (double.TryParse(txtAudioLatency.Text, out latency)) {
                userSettings.SetValueForKey(UserSettingsKey.EditorAudioLatency, latency);
                UpdateSettings();
                caller.PauseSong();
            } else {
                MessageBox.Show(this, $"The latency must be numerical.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                latency = prevLatency;
            }
            txtAudioLatency.Text = latency.ToString();
        }
        private void ComboDrumSample_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.DrumSampleFile, comboDrumSample.SelectedItem.ToString());
            if (doneInit) {
                UpdateSettings();
                caller.PauseSong();
                caller.RestartDrummer();
            }
        }
        private void InitComboDrumSample() {
            string selectedSampleFile = userSettings.GetValueForKey(UserSettingsKey.DrumSampleFile);
            var files = Directory.GetFiles(Program.ResourcesPath);
            foreach (var file in files) {
                if (file.EndsWith("1.wav") || file.EndsWith("1.mp3")) {
                    var localFile = file.Split(Program.ResourcesPath)[1];
                    var strippedLocalFile = localFile.Substring(0, localFile.Length - 5);
                    int i = comboDrumSample.Items.Add(strippedLocalFile);

                    if (strippedLocalFile == selectedSampleFile) {
                        comboDrumSample.SelectedIndex = i;
                    }
                }
            }
        }
        private void checkPanNotes_Click(object sender, RoutedEventArgs e) {
            bool newStatus = checkPanNotes.IsChecked ?? false;
            userSettings.SetValueForKey(UserSettingsKey.PanDrumSounds, newStatus);
            UpdateSettings();
            caller.PauseSong();
            caller.RestartDrummer();
        }

        private void SliderSongVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            txtSongVol.Text = $"{(int)(sliderSongVol.Value * 100)}%";
        }

        private void sliderSongVol_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.DefaultSongVolume, sliderSongVol.Value);
            UpdateSettings();
        }

        private void sliderSongVol_DragCompleted(object sender, DragCompletedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.DefaultSongVolume, sliderSongVol.Value);
            UpdateSettings();
        }

        private void SliderDrumVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            txtDrumVol.Text = $"{(int)(sliderDrumVol.Value * 100)}%";
            UpdateSettings();
        }
        private void sliderDrumVol_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.DefaultNoteVolume, sliderDrumVol.Value);
            UpdateSettings();
        }

        private void sliderDrumVol_DragCompleted(object sender, DragCompletedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.DefaultNoteVolume, sliderDrumVol.Value);
            UpdateSettings();
        }

        // General settings handlers re-added after refactor
        private void CheckShowSpectrogram_Click(object sender, RoutedEventArgs e) {
            bool newStatus = CheckShowSpectrogram.IsChecked ?? false;
            ToggleSpectrogramOptionsVisibility();
            userSettings.SetValueForKey(UserSettingsKey.EnableSpectrogram, newStatus);
            UpdateSettings();
        }
        private void TxtDefaultMapper_LostFocus(object sender, RoutedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.DefaultMapper, txtDefaultMapper.Text);
            UpdateSettings();
        }
        private void TxtDefaultNoteSpeed_LostFocus(object sender, RoutedEventArgs e) {
            string prev = userSettings.GetValueForKey(UserSettingsKey.DefaultNoteSpeed);
            double speed;
            if (double.TryParse(txtDefaultNoteSpeed.Text, out speed) && speed > 0) {
                userSettings.SetValueForKey(UserSettingsKey.DefaultNoteSpeed, speed);
                UpdateSettings();
            } else {
                MessageBox.Show(this, "The note speed must be a positive number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtDefaultNoteSpeed.Text = prev;
            }
        }
        private void TxtDefaultGridSpacing_LostFocus(object sender, RoutedEventArgs e) {
            string prev = userSettings.GetValueForKey(UserSettingsKey.DefaultGridSpacing);
            int spacing;
            if (int.TryParse(txtDefaultGridSpacing.Text, out spacing) && spacing > 0) {
                userSettings.SetValueForKey(UserSettingsKey.DefaultGridSpacing, spacing);
                UpdateSettings();
            } else {
                MessageBox.Show(this, "The grid spacing must be a positive integer.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtDefaultGridSpacing.Text = prev;
            }
        }
        private void ComboNotePasteBehavior_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (comboNotePasteBehavior.SelectedItem is NotePasteBehaviorOption npb) {
                userSettings.SetValueForKey(UserSettingsKey.NotePasteBehavior, npb.Value);
                UpdateSettings();
            }
        }
        private void InitComboNotePasteBehavior() {
            string selected = userSettings.GetValueForKey(UserSettingsKey.NotePasteBehavior);
            int idx = -1;
            idx = comboNotePasteBehavior.Items.Add(new NotePasteBehaviorOption(global::Edda.Const.Editor.NotePasteBehavior.AlignToGlobalBeat, "Align to Global Beat"));
            if (selected == global::Edda.Const.Editor.NotePasteBehavior.AlignToGlobalBeat) comboNotePasteBehavior.SelectedIndex = idx;
            idx = comboNotePasteBehavior.Items.Add(new NotePasteBehaviorOption(global::Edda.Const.Editor.NotePasteBehavior.AlignToFirstNoteBPM, "Align to First Note BPM"));
            if (selected == global::Edda.Const.Editor.NotePasteBehavior.AlignToFirstNoteBPM) comboNotePasteBehavior.SelectedIndex = idx;
            idx = comboNotePasteBehavior.Items.Add(new NotePasteBehaviorOption(global::Edda.Const.Editor.NotePasteBehavior.AlignToNoteBPM, "Align to Note BPM"));
            if (string.IsNullOrEmpty(selected) || selected == global::Edda.Const.Editor.NotePasteBehavior.AlignToNoteBPM) comboNotePasteBehavior.SelectedIndex = idx;
        }
        private void ComboPlaybackDevice_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (comboPlaybackDevice.SelectedItem is PlaybackDevice pd) {
                userSettings.SetValueForKey(UserSettingsKey.PlaybackDeviceID, pd.ID);
                UpdateSettings();
                caller.PauseSong();
                caller.RestartDrummer();
                caller.RestartMetronome();
            }
        }



        // Spectrogram options
        private void ToggleSpectrogramOptionsVisibility() {
            if (CheckShowSpectrogram.IsChecked ?? false) {
                spectrogramOptionsLabel.Visibility = Visibility.Visible;
                spectrogramOptions.Visibility = Visibility.Visible;
            } else {
                spectrogramOptionsLabel.Visibility = Visibility.Collapsed;
                spectrogramOptions.Visibility = Visibility.Collapsed;
            }
        }
        private void checkSpectrogramCache_Click(object sender, RoutedEventArgs e) {
            bool newStatus = checkSpectrogramCache.IsChecked ?? false;
            userSettings.SetValueForKey(UserSettingsKey.SpectrogramCache, newStatus);
            if (doneInit) {
                UpdateSettings();
            }
        }
        private void ComboSpectrogramType_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.SpectrogramType, comboSpectrogramType.SelectedItem.ToString());
            if (doneInit) {
                UpdateSettings();
            }
        }
        private void InitComboSpectrogramType() {
            string selectedSpectrogramType = userSettings.GetValueForKey(UserSettingsKey.SpectrogramType);
            foreach (var type in Enum.GetNames(typeof(VorbisSpectrogramGenerator.SpectrogramType))) {
                int i = comboSpectrogramType.Items.Add(type);
                if (type == selectedSpectrogramType) {
                    comboSpectrogramType.SelectedIndex = i;
                }
            }
        }
        private void ComboSpectrogramQuality_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.SpectrogramQuality, comboSpectrogramQuality.SelectedItem.ToString());
            if (doneInit) {
                UpdateSettings();
            }
        }
        private void InitComboSpectrogramQuality() {
            string selectedSpectrogramQuality = userSettings.GetValueForKey(UserSettingsKey.SpectrogramQuality);
            foreach (var quality in Enum.GetNames(typeof(VorbisSpectrogramGenerator.SpectrogramQuality))) {
                int i = comboSpectrogramQuality.Items.Add(quality);
                if (quality == selectedSpectrogramQuality) {
                    comboSpectrogramQuality.SelectedIndex = i;
                }
            }
        }
        private void TxtSpectrogramFrequency_LostFocus(object sender, RoutedEventArgs e) {
            int.TryParse(userSettings.GetValueForKey(UserSettingsKey.SpectrogramFrequency), out int prevFrequency);
            int frequency;
            if (int.TryParse(txtSpectrogramFrequency.Text, out frequency) && frequency >= Editor.Spectrogram.MinFreq && frequency <= Editor.Spectrogram.MaxFreq) {
                if (frequency != prevFrequency) {
                    userSettings.SetValueForKey(UserSettingsKey.SpectrogramFrequency, frequency);
                    UpdateSettings();
                }
            } else {
                MessageBox.Show(this, $"The frequency must be an integer between {Editor.Spectrogram.MinFreq} and {Editor.Spectrogram.MaxFreq}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                frequency = prevFrequency;
            }
            txtSpectrogramFrequency.Text = frequency.ToString();
        }
        private void TxtSpectrogramFrequency_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Return) {
                TxtSpectrogramFrequency_LostFocus(sender, null);
            }
        }
        private void ComboSpectrogramColormap_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.SpectrogramColormap, comboSpectrogramColormap.SelectedItem.ToString());
            if (doneInit) {
                UpdateSettings();
            }
        }
        private void InitComboSpectrogramColormap() {
            string selectedSpectrogramColormap = userSettings.GetValueForKey(UserSettingsKey.SpectrogramColormap);
            foreach (var colormap in Spectrogram.Colormap.GetColormapNames()) {
                int i = comboSpectrogramColormap.Items.Add(colormap);
                if (colormap == selectedSpectrogramColormap) {
                    comboSpectrogramColormap.SelectedIndex = i;
                }
            }
        }
        private void checkSpectrogramFlipped_Click(object sender, RoutedEventArgs e) {
            bool newStatus = checkSpectrogramFlipped.IsChecked ?? false;
            userSettings.SetValueForKey(UserSettingsKey.SpectrogramFlipped, newStatus);
            if (doneInit) {
                UpdateSettings();
            }
        }

        private void checkSpectrogramChunking_Click(object sender, RoutedEventArgs e) {
            bool newStatus = checkSpectrogramChunking.IsChecked ?? false;
            userSettings.SetValueForKey(UserSettingsKey.SpectrogramChunking, newStatus);
            if (doneInit) {
                UpdateSettings();
            }
        }

        private void CheckDiscord_Click(object sender, RoutedEventArgs e) {
            bool newStatus = checkDiscord.IsChecked ?? false;
            userSettings.SetValueForKey(UserSettingsKey.EnableDiscordRPC, newStatus);
            UpdateSettings();
        }
        private void CheckAutosave_Click(object sender, RoutedEventArgs e) {
            bool newStatus = CheckAutosave.IsChecked ?? false;
            userSettings.SetValueForKey(UserSettingsKey.EnableAutosave, newStatus);
            UpdateSettings();
        }
        private void CheckStartupUpdate_Click(object sender, RoutedEventArgs e) {
            bool newStatus = checkStartupUpdate.IsChecked ?? false;
            userSettings.SetValueForKey(UserSettingsKey.CheckForUpdates, newStatus);
            UpdateSettings();
        }



        // Browse button for selecting an arbitrary map save folder
        private void BtnBrowseMapSaveFolder_Click(object sender, RoutedEventArgs e) {
            var d = new CommonOpenFileDialog();
            d.Title = "Select the folder to save your maps";
            d.IsFolderPicker = true;
            var prevPath = userSettings.GetValueForKey(UserSettingsKey.MapSaveLocationPath);
            if (!string.IsNullOrEmpty(prevPath) && Directory.Exists(prevPath)) {
                d.InitialDirectory = prevPath;
            } else if (!string.IsNullOrEmpty(Program.DocumentsMapFolder) && Directory.Exists(Program.DocumentsMapFolder)) {
                d.InitialDirectory = Program.DocumentsMapFolder;
            }
            if (d.ShowDialog() != CommonFileDialogResult.Ok) {
                return;
            }
            // create directory if it doesn't exist
            if (!Directory.Exists(d.FileName)) {
                Directory.CreateDirectory(d.FileName);
            }
            txtMapSaveFolderPath.Text = d.FileName;
            // mark as custom selection
            userSettings.SetValueForKey(UserSettingsKey.MapSaveLocationIndex, 1);
            userSettings.SetValueForKey(UserSettingsKey.MapSaveLocationPath, d.FileName);
            UpdateSettings();
        }


        private void BtnSave_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private void BtnResetLayout_Click(object sender, RoutedEventArgs e) {
            try {
                // Reset spectrogram width to default and persist
                userSettings.SetValueForKey(UserSettingsKey.SpectrogramWidth, DefaultUserSettings.SpectrogramWidth);
                UpdateSettings();
                // Apply immediately in the caller
                if (caller != null && caller.gridSpectrogram?.ColumnDefinitions?.Count > 0) {
                    caller.gridSpectrogram.ColumnDefinitions[0].Width = new GridLength(DefaultUserSettings.SpectrogramWidth);
                }
            } catch {
                // ignore reset errors
            }
        }

        private void UpdateSettings() {
            userSettings.Write();
            caller.LoadSettingsFile(true);
        }

        private void TxtMapSaveFolderPath_LostFocus(object sender, RoutedEventArgs e) {
            var path = txtMapSaveFolderPath.Text;
            if (!string.IsNullOrWhiteSpace(path)) {
                try {
                    if (!Directory.Exists(path)) {
                        Directory.CreateDirectory(path);
                    }
                    userSettings.SetValueForKey(UserSettingsKey.MapSaveLocationIndex, 1);
                    userSettings.SetValueForKey(UserSettingsKey.MapSaveLocationPath, path);
                    UpdateSettings();
                } catch (Exception ex) {
                    MessageBox.Show(this, $"Unable to set map save path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TxtAudioQualityConversion_LostFocus(object sender, RoutedEventArgs e) {
            string prev = userSettings.GetValueForKey("ExportQuality");
            int quality;
            string input = txtAudioQualityConversion.Text.Trim();
            if (string.IsNullOrEmpty(input)) {
                quality = 6;
                txtAudioQualityConversion.Text = "6";
                userSettings.SetValueForKey("ExportQuality", quality);
                UpdateSettings();
            } else if (int.TryParse(input, out quality) && quality >= 1 && quality <= 10) {
                userSettings.SetValueForKey("ExportQuality", quality);
                UpdateSettings();
            } else {
                MessageBox.Show(this, "Audio quality must be an integer between 1 et 10.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtAudioQualityConversion.Text = prev;
            }
        }

        private void TxtAudioQualityConversion_PreviewTextInput(object sender, RoutedEventArgs e) {

        }

        // Removed game install picker and toggle; replaced by free-path selection

        class PlaybackDevice {
            public string ID { get; private set; }
            public string Name { get; private set; }
            public PlaybackDevice(string ID, string Name) {
                this.ID = ID;
                this.Name = Name;
            }
            public PlaybackDevice(MMDevice device) {
                this.ID = device.ID;
                this.Name = device.FriendlyName;
            }
        }

        class NotePasteBehaviorOption(string value, string label) {
            public string Value { get; private set; } = value;
            public string Label { get; private set; } = label;
        }
    }
}