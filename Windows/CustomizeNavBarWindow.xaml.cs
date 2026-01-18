using ColorPicker;
using Edda.Const;
using System;
using System.Reactive.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace Edda {
    /// <summary>
    /// Interaction logic for CustomizeNavBarWindow.xaml
    /// </summary>
    public partial class CustomizeNavBarWindow : Window {
        readonly MainWindow caller;
        readonly UserSettingsManager userSettings;
        readonly bool doneInit = false;

        IDisposable ColorWaveformColorChangedDebounce;
        IDisposable ColorBookmarkColorChangedDebounce;
        IDisposable ColorBPMChangeColorChangedDebounce;
        // Global notes controls removed
        IDisposable ColorNoteCol0ColorChangedDebounce;
        IDisposable ColorNoteCol1ColorChangedDebounce;
        IDisposable ColorNoteCol2ColorChangedDebounce;
        IDisposable ColorNoteCol3ColorChangedDebounce;
        // Per-column tint sliders (no debounce)
        // Tint intensity does not need debounce; it's lightweight

        public CustomizeNavBarWindow(MainWindow caller, UserSettingsManager userSettings) {
            InitializeComponent();
            this.caller = caller;
            this.userSettings = userSettings;

            CheckWaveform.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableNavWaveform);
            var waveformColor = userSettings.GetValueForKey(UserSettingsKey.NavWaveformColor) ?? Editor.Waveform.ColourWPF.ToString();
            ColorWaveform.SelectedColor = (Color)ColorConverter.ConvertFromString(waveformColor);
            ToggleWaveformColorIsEnabled();
            ColorWaveformColorChangedDebounce = Observable
                .FromEventPattern<RoutedEventArgs>(ColorWaveform, nameof(PortableColorPicker.ColorChanged))
                .Throttle(TimeSpan.FromMilliseconds(Editor.DrawDebounceInterval))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(eventPattern =>
                    Dispatcher.Invoke(() =>
                        ColorWaveform_ColorChanged(eventPattern.Sender, eventPattern.EventArgs)
                    )
                );

            CheckBookmark.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableNavBookmarks);
            ColorBookmark.SelectedColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavBookmarkColor) ?? Editor.NavBookmark.Colour);
            ColorBookmark.SecondaryColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavBookmarkNameColor) ?? Editor.NavBookmark.NameColour);
            SliderBookmarkShadowOpacity.Value = double.Parse(userSettings.GetValueForKey(UserSettingsKey.NavBookmarkShadowOpacity));
            ToggleBookmarkColorIsEnabled();
            ColorBookmarkColorChangedDebounce = Observable
                .FromEventPattern<RoutedEventArgs>(ColorBookmark, nameof(PortableColorPicker.ColorChanged))
                .Throttle(TimeSpan.FromMilliseconds(Editor.DrawDebounceInterval))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(eventPattern =>
                    Dispatcher.Invoke(() =>
                        ColorBookmark_ColorChanged(eventPattern.Sender, eventPattern.EventArgs)
                    )
                );

            CheckBPMChange.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableNavBPMChanges);
            ColorBPMChange.SelectedColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavBPMChangeColor) ?? Editor.NavBPMChange.Colour);
            ColorBPMChange.SecondaryColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavBPMChangeLabelColor) ?? Editor.NavBPMChange.LabelColour);
            SliderBPMChangeShadowOpacity.Value = double.Parse(userSettings.GetValueForKey(UserSettingsKey.NavBPMChangeShadowOpacity));
            ToggleBPMChangeColorIsEnabled();
            ColorBPMChangeColorChangedDebounce = Observable
                .FromEventPattern<RoutedEventArgs>(ColorBPMChange, nameof(PortableColorPicker.ColorChanged))
                .Throttle(TimeSpan.FromMilliseconds(Editor.DrawDebounceInterval))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(eventPattern =>
                    Dispatcher.Invoke(() =>
                        ColorBPMChange_ColorChanged(eventPattern.Sender, eventPattern.EventArgs)
                    )
                );

            // Per-column note controls
            CheckNoteCol0.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableNavNotesCol0);
            CheckNoteCol1.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableNavNotesCol1);
            CheckNoteCol2.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableNavNotesCol2);
            CheckNoteCol3.IsChecked = userSettings.GetBoolForKey(UserSettingsKey.EnableNavNotesCol3);
            try { SliderNoteTintIntensityCol0.Value = double.Parse(userSettings.GetValueForKey(UserSettingsKey.NavNoteTintIntensityCol0) ?? DefaultUserSettings.NavNoteTintIntensityCol0.ToString()); } catch { SliderNoteTintIntensityCol0.Value = DefaultUserSettings.NavNoteTintIntensityCol0; }
            try { SliderNoteTintIntensityCol1.Value = double.Parse(userSettings.GetValueForKey(UserSettingsKey.NavNoteTintIntensityCol1) ?? DefaultUserSettings.NavNoteTintIntensityCol1.ToString()); } catch { SliderNoteTintIntensityCol1.Value = DefaultUserSettings.NavNoteTintIntensityCol1; }
            try { SliderNoteTintIntensityCol2.Value = double.Parse(userSettings.GetValueForKey(UserSettingsKey.NavNoteTintIntensityCol2) ?? DefaultUserSettings.NavNoteTintIntensityCol2.ToString()); } catch { SliderNoteTintIntensityCol2.Value = DefaultUserSettings.NavNoteTintIntensityCol2; }
            try { SliderNoteTintIntensityCol3.Value = double.Parse(userSettings.GetValueForKey(UserSettingsKey.NavNoteTintIntensityCol3) ?? DefaultUserSettings.NavNoteTintIntensityCol3.ToString()); } catch { SliderNoteTintIntensityCol3.Value = DefaultUserSettings.NavNoteTintIntensityCol3; }
            ColorNoteCol0.SelectedColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavNoteColorCol0) ?? userSettings.GetValueForKey(UserSettingsKey.NavNoteColor) ?? Editor.NavNote.Colour);
            ColorNoteCol1.SelectedColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavNoteColorCol1) ?? userSettings.GetValueForKey(UserSettingsKey.NavNoteColor) ?? Editor.NavNote.Colour);
            ColorNoteCol2.SelectedColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavNoteColorCol2) ?? userSettings.GetValueForKey(UserSettingsKey.NavNoteColor) ?? Editor.NavNote.Colour);
            ColorNoteCol3.SelectedColor = (Color)ColorConverter.ConvertFromString(userSettings.GetValueForKey(UserSettingsKey.NavNoteColorCol3) ?? userSettings.GetValueForKey(UserSettingsKey.NavNoteColor) ?? Editor.NavNote.Colour);
            ToggleNoteRowsEnabled();
            ColorNoteCol0ColorChangedDebounce = Observable
                .FromEventPattern<RoutedEventArgs>(ColorNoteCol0, nameof(PortableColorPicker.ColorChanged))
                .Throttle(TimeSpan.FromMilliseconds(Editor.DrawDebounceInterval))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(eventPattern => Dispatcher.Invoke(() => ColorNoteCol0_ColorChanged(eventPattern.Sender, eventPattern.EventArgs)));
            ColorNoteCol1ColorChangedDebounce = Observable
                .FromEventPattern<RoutedEventArgs>(ColorNoteCol1, nameof(PortableColorPicker.ColorChanged))
                .Throttle(TimeSpan.FromMilliseconds(Editor.DrawDebounceInterval))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(eventPattern => Dispatcher.Invoke(() => ColorNoteCol1_ColorChanged(eventPattern.Sender, eventPattern.EventArgs)));
            ColorNoteCol2ColorChangedDebounce = Observable
                .FromEventPattern<RoutedEventArgs>(ColorNoteCol2, nameof(PortableColorPicker.ColorChanged))
                .Throttle(TimeSpan.FromMilliseconds(Editor.DrawDebounceInterval))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(eventPattern => Dispatcher.Invoke(() => ColorNoteCol2_ColorChanged(eventPattern.Sender, eventPattern.EventArgs)));
            ColorNoteCol3ColorChangedDebounce = Observable
                .FromEventPattern<RoutedEventArgs>(ColorNoteCol3, nameof(PortableColorPicker.ColorChanged))
                .Throttle(TimeSpan.FromMilliseconds(Editor.DrawDebounceInterval))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(eventPattern => Dispatcher.Invoke(() => ColorNoteCol3_ColorChanged(eventPattern.Sender, eventPattern.EventArgs)));


            doneInit = true;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private void UpdateSettings() {
            userSettings.Write();
            caller.LoadSettingsFile();
        }

        private void UpdateWaveform() {
            UpdateSettings();
            caller.gridController.DrawNavWaveform();
        }

        private void UpdateBookmarks() {
            UpdateSettings();
            caller.gridController.DrawNavBookmarks();
        }

        private void UpdateBPMChanges() {
            UpdateSettings();
            caller.gridController.DrawNavBPMChanges();
        }

        private void UpdateNotes() {
            UpdateSettings();
            // Ensure nav note brushes reflect latest settings immediately
            caller.gridController.InvalidateNavNoteBrushes();
            caller.canvasNavNotes.Children.Clear();
            caller.gridController.DrawNavNotes(caller.mapEditor.currentMapDifficulty.notes);
            caller.gridController.HighlightNavNotes(caller.mapEditor.currentMapDifficulty.selectedNotes);
        }

        private void CheckWaveform_Click(object sender, RoutedEventArgs e) {
            ToggleWaveformColorIsEnabled();
            userSettings.SetValueForKey(UserSettingsKey.EnableNavWaveform, CheckWaveform.IsChecked ?? false);
            UpdateWaveform();
        }
        private void ColorWaveform_ColorChanged(object sender, RoutedEventArgs e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavWaveformColor, ColorWaveform.SelectedColor.ToString() ?? Editor.Waveform.ColourWPF.ToString());
                UpdateWaveform();
            }
        }
        private void ToggleWaveformColorIsEnabled() {
            ColorWaveform.IsEnabled = CheckWaveform.IsChecked ?? false;
        }

        private void CheckBookmark_Click(object sender, RoutedEventArgs e) {
            ToggleBookmarkColorIsEnabled();
            userSettings.SetValueForKey(UserSettingsKey.EnableNavBookmarks, CheckBookmark.IsChecked ?? false);
            UpdateBookmarks();
        }
        private void ColorBookmark_ColorChanged(object sender, RoutedEventArgs e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavBookmarkColor, ColorBookmark.SelectedColor.ToString() ?? Editor.NavBookmark.Colour);
                userSettings.SetValueForKey(UserSettingsKey.NavBookmarkNameColor, ColorBookmark.SecondaryColor.ToString() ?? Editor.NavBookmark.NameColour);
                UpdateBookmarks();
            }
        }
        private void SliderBookmarkShadowOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavBookmarkShadowOpacity, SliderBookmarkShadowOpacity.Value.ToString());
                UpdateBookmarks();
            }
        }
        private void SliderBookmarkShadowOpacity_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            SliderBookmarkShadowOpacity.Value = Editor.NavBookmark.ShadowOpacity;
        }
        private void ToggleBookmarkColorIsEnabled() {
            var status = CheckBookmark.IsChecked ?? false;
            ColorBookmark.IsEnabled = status;
            SliderBookmarkShadowOpacity.IsEnabled = status;
        }

        private void CheckBPMChange_Click(object sender, RoutedEventArgs e) {
            ToggleBPMChangeColorIsEnabled();
            userSettings.SetValueForKey(UserSettingsKey.EnableNavBPMChanges, CheckBPMChange.IsChecked ?? false);
            UpdateBPMChanges();
        }
        private void ColorBPMChange_ColorChanged(object sender, RoutedEventArgs e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavBPMChangeColor, ColorBPMChange.SelectedColor.ToString() ?? Editor.NavBPMChange.Colour);
                userSettings.SetValueForKey(UserSettingsKey.NavBPMChangeLabelColor, ColorBPMChange.SecondaryColor.ToString() ?? Editor.NavBPMChange.LabelColour);
                UpdateBPMChanges();
            }
        }
        private void SliderBPMChangeShadowOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavBPMChangeShadowOpacity, SliderBPMChangeShadowOpacity.Value.ToString());
                UpdateBPMChanges();
            }
        }
        private void SliderBPMChangeShadowOpacity_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            SliderBPMChangeShadowOpacity.Value = Editor.NavBPMChange.ShadowOpacity;
        }
        private void ToggleBPMChangeColorIsEnabled() {
            var status = CheckBPMChange.IsChecked ?? false;
            ColorBPMChange.IsEnabled = status;
            SliderBPMChangeShadowOpacity.IsEnabled = status;
        }

        private void ToggleNoteRowsEnabled() {
            ToggleNoteRowEnabled(0, CheckNoteCol0.IsChecked ?? false);
            ToggleNoteRowEnabled(1, CheckNoteCol1.IsChecked ?? false);
            ToggleNoteRowEnabled(2, CheckNoteCol2.IsChecked ?? false);
            ToggleNoteRowEnabled(3, CheckNoteCol3.IsChecked ?? false);
        }
        private void ToggleNoteRowEnabled(int col, bool enabled) {
            switch (col) {
                case 0:
                    ColorNoteCol0.IsEnabled = enabled;
                    SliderNoteTintIntensityCol0.IsEnabled = enabled;
                    break;
                case 1:
                    ColorNoteCol1.IsEnabled = enabled;
                    SliderNoteTintIntensityCol1.IsEnabled = enabled;
                    break;
                case 2:
                    ColorNoteCol2.IsEnabled = enabled;
                    SliderNoteTintIntensityCol2.IsEnabled = enabled;
                    break;
                case 3:
                    ColorNoteCol3.IsEnabled = enabled;
                    SliderNoteTintIntensityCol3.IsEnabled = enabled;
                    break;
            }
        }

        private void SliderNoteTintIntensityCol0_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteTintIntensityCol0, SliderNoteTintIntensityCol0.Value);
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }
        private void SliderNoteTintIntensityCol1_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteTintIntensityCol1, SliderNoteTintIntensityCol1.Value);
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }
        private void SliderNoteTintIntensityCol2_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteTintIntensityCol2, SliderNoteTintIntensityCol2.Value);
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }
        private void SliderNoteTintIntensityCol3_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteTintIntensityCol3, SliderNoteTintIntensityCol3.Value);
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }
        private void SliderNoteTintIntensityCol0_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { SliderNoteTintIntensityCol0.Value = DefaultUserSettings.NavNoteTintIntensityCol0; }
        private void SliderNoteTintIntensityCol1_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { SliderNoteTintIntensityCol1.Value = DefaultUserSettings.NavNoteTintIntensityCol1; }
        private void SliderNoteTintIntensityCol2_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { SliderNoteTintIntensityCol2.Value = DefaultUserSettings.NavNoteTintIntensityCol2; }
        private void SliderNoteTintIntensityCol3_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { SliderNoteTintIntensityCol3.Value = DefaultUserSettings.NavNoteTintIntensityCol3; }

        private void ButtonResetWaveform_Click(object sender, RoutedEventArgs e) {
            ColorWaveform.SelectedColor = Editor.Waveform.ColourWPF;
            UpdateWaveform();
        }

        private void ButtonResetBookmark_Click(object sender, RoutedEventArgs e) {
            ColorBookmark.SelectedColor = (Color)ColorConverter.ConvertFromString(Editor.NavBookmark.Colour);
            ColorBookmark.SecondaryColor = (Color)ColorConverter.ConvertFromString(Editor.NavBookmark.NameColour);
            SliderBookmarkShadowOpacity.Value = Editor.NavBookmark.ShadowOpacity;
        }

        private void ButtonResetBPMChange_Click(object sender, RoutedEventArgs e) {
            ColorBPMChange.SelectedColor = (Color)ColorConverter.ConvertFromString(Editor.NavBPMChange.Colour);
            ColorBPMChange.SecondaryColor = (Color)ColorConverter.ConvertFromString(Editor.NavBPMChange.LabelColour);
            SliderBPMChangeShadowOpacity.Value = Editor.NavBPMChange.ShadowOpacity;
        }

        // Global notes reset removed
        private void CheckNoteCol0_Click(object sender, RoutedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.EnableNavNotesCol0, CheckNoteCol0.IsChecked ?? false);
            ToggleNoteRowEnabled(0, CheckNoteCol0.IsChecked ?? false);
            UpdateNotes();
        }
        private void CheckNoteCol1_Click(object sender, RoutedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.EnableNavNotesCol1, CheckNoteCol1.IsChecked ?? false);
            ToggleNoteRowEnabled(1, CheckNoteCol1.IsChecked ?? false);
            UpdateNotes();
        }
        private void CheckNoteCol2_Click(object sender, RoutedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.EnableNavNotesCol2, CheckNoteCol2.IsChecked ?? false);
            ToggleNoteRowEnabled(2, CheckNoteCol2.IsChecked ?? false);
            UpdateNotes();
        }
        private void CheckNoteCol3_Click(object sender, RoutedEventArgs e) {
            userSettings.SetValueForKey(UserSettingsKey.EnableNavNotesCol3, CheckNoteCol3.IsChecked ?? false);
            ToggleNoteRowEnabled(3, CheckNoteCol3.IsChecked ?? false);
            UpdateNotes();
        }

        private void ColorNoteCol0_ColorChanged(object sender, RoutedEventArgs e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteColorCol0, ColorNoteCol0.SelectedColor.ToString() ?? Editor.NavNote.Colour);
                UpdateNotes();
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }
        private void ColorNoteCol1_ColorChanged(object sender, RoutedEventArgs e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteColorCol1, ColorNoteCol1.SelectedColor.ToString() ?? Editor.NavNote.Colour);
                UpdateNotes();
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }
        private void ColorNoteCol2_ColorChanged(object sender, RoutedEventArgs e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteColorCol2, ColorNoteCol2.SelectedColor.ToString() ?? Editor.NavNote.Colour);
                UpdateNotes();
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }
        private void ColorNoteCol3_ColorChanged(object sender, RoutedEventArgs e) {
            if (doneInit) {
                userSettings.SetValueForKey(UserSettingsKey.NavNoteColorCol3, ColorNoteCol3.SelectedColor.ToString() ?? Editor.NavNote.Colour);
                UpdateNotes();
                caller.gridController.UpdateRuneColorsAndRetint();
            }
        }

        private void ButtonResetNoteCol0_Click(object sender, RoutedEventArgs e) {
            ColorNoteCol0.SelectedColor = (Color)ColorConverter.ConvertFromString(Editor.NavNote.Colour);
        }
        private void ButtonResetNoteCol1_Click(object sender, RoutedEventArgs e) {
            ColorNoteCol1.SelectedColor = (Color)ColorConverter.ConvertFromString(Editor.NavNote.Colour);
        }
        private void ButtonResetNoteCol2_Click(object sender, RoutedEventArgs e) {
            ColorNoteCol2.SelectedColor = (Color)ColorConverter.ConvertFromString(Editor.NavNote.Colour);
        }
        private void ButtonResetNoteCol3_Click(object sender, RoutedEventArgs e) {
            ColorNoteCol3.SelectedColor = (Color)ColorConverter.ConvertFromString(Editor.NavNote.Colour);
        }

    }
}