using Edda.Classes.MapEditorNS;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Edda.Windows {
    public partial class AdvancedHistoryWindow : Window {
        // highlight notes when selecting a history line
        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history == null) return;
            if (HistoryListBox.SelectedItem is HistoryRow row) {
                var allEdits = history.GetHistory();
                if (row.RawIndex >= 0 && row.RawIndex < allEdits.Count) {
                    var editList = allEdits[row.RawIndex];
                    var notes = new List<Edda.Classes.MapEditorNS.NoteNS.Note>();
                    foreach (var edit in editList.items) {
                        if (edit.item is Edda.Classes.MapEditorNS.NoteNS.Note note)
                            notes.Add(note);
                    }
                    // Select and highlight the relevant notes
                    mapEditor.SelectNewNotes(notes, false);
                }
            }
        }
        private class HistoryRow {
            public int Index { get; set; }
            public string Action { get; set; }
            public int NumberOfNotes { get; set; }
            public string DrumSummary { get; set; }
            public string DrumFull { get; set; }
            public string BeatSummary { get; set; }
            public string BeatFull { get; set; }
            public string TimestampSummary { get; set; }
            public string TimestampFull { get; set; }
            public string DetailSummary { get; set; }
            public string DetailFull { get; set; }
            public int RawIndex { get; set; } // for navigation 

            public System.Windows.Input.ICommand ShowDetailCommand { get; set; }
        }
        private MapEditor mapEditor;
        public AdvancedHistoryWindow(MapEditor editor) {
            InitializeComponent();
            mapEditor = editor;
            HistoryListBox.SelectionChanged += HistoryListBox_SelectionChanged;
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history != null)
                history.HistoryChanged += RefreshHistory;
            RefreshHistory();
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history != null)
            {
                history.Clear();
                RefreshHistory();
            }
        }

        private void HistoryListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history == null) return;
            if (HistoryListBox.SelectedItem is HistoryRow row) {
                int targetIndex = row.RawIndex + 1;
                while (history.GetCurrentIndex() > targetIndex)
                    mapEditor.Undo();
                while (history.GetCurrentIndex() < targetIndex)
                    mapEditor.Redo();
                // always reselect the double-clicked line
                if (HistoryListBox.ItemsSource is IList<HistoryRow> rows) {
                    for (int i = 0; i < rows.Count; i++) {
                        if (rows[i].RawIndex == row.RawIndex) {
                            HistoryListBox.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
        }

        public void RefreshHistory() {
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history == null) return;
            var allEdits = history.GetHistory();
            int count = allEdits.Count;
            var rows = new List<HistoryRow>();
            int maxSummary = 5;
            for (int idx = count - 1; idx >= 0; idx--) {
                var editList = allEdits[idx];
                    string actionType = "";
                    List<string> drums = new List<string>();
                    List<string> beats = new List<string>();
                    List<string> timestamps = new List<string>();
                    List<string> details = new List<string>();
                    bool isMoveAction = false;
                    List<string> moveDrums = new List<string>();
                    List<string> moveBeats = new List<string>();
                    List<string> moveTimestamps = new List<string>();
                    foreach (var edit in editList.items) {
                        if (edit.item is Edda.Classes.MapEditorNS.NoteNS.Note note) {
                            if (edit.isMove) {
                                isMoveAction = true;
                                actionType = "Move";
                                moveDrums.Add(note.col switch { 0 => "D1", 1 => "D2", 2 => "D3", 3 => "D4", _ => $"D{note.col + 1}" });
                                moveBeats.Add(note.beat.ToString("0.###"));
                                double bpm = mapEditor?.GlobalBPM ?? 120.0;
                                double timestampMs = note.beat * 60000.0 / bpm;
                                int minutes = (int)(timestampMs / 60000);
                                int seconds = (int)((timestampMs % 60000) / 1000);
                                int millis = (int)(timestampMs % 1000);
                                moveTimestamps.Add($"{minutes}:{seconds:D2}.{millis:D3}");
                            } else {
                                drums.Add(note.col switch { 0 => "D1", 1 => "D2", 2 => "D3", 3 => "D4", _ => $"D{note.col + 1}" });
                                beats.Add(note.beat.ToString("0.###"));
                                double bpm = mapEditor?.GlobalBPM ?? 120.0;
                                double timestampMs = note.beat * 60000.0 / bpm;
                                int minutes = (int)(timestampMs / 60000);
                                int seconds = (int)((timestampMs % 60000) / 1000);
                                int millis = (int)(timestampMs % 1000);
                                timestamps.Add($"{minutes}:{seconds:D2}.{millis:D3}");
                                details.Add($"{note.beat:0.###} [{note.col}] {note}");
                                if (!isMoveAction) actionType = edit.isAdd ? "Add" : "Remove";
                            }
                        } else {
                            if (!isMoveAction) actionType = edit.item.ToString();
                            details.Add(edit.item.ToString());
                        }
                    }
                    if (isMoveAction && moveDrums.Count >= 2) {
                        drums.Clear();
                        beats.Clear();
                        timestamps.Clear();
                        int moveCount = moveDrums.Count / 2;
                        for (int i = 0; i < moveCount; i++) {
                            drums.Add(moveDrums[i + moveCount] + " -> " + moveDrums[i]);
                            beats.Add(moveBeats[i + moveCount] + " -> " + moveBeats[i]);
                            timestamps.Add(moveTimestamps[i + moveCount] + " -> " + moveTimestamps[i]);
                        }
                    }
                string drumSummary = drums.Count > maxSummary
                    ? string.Join("\n", drums.GetRange(0, maxSummary)) + $"\n(+{drums.Count - maxSummary})"
                    : string.Join("\n", drums);
                string beatSummary = beats.Count > maxSummary
                    ? string.Join("\n", beats.GetRange(0, maxSummary)) + $"\n(+{beats.Count - maxSummary})"
                    : string.Join("\n", beats);
                string timestampSummary = timestamps.Count > maxSummary
                    ? string.Join("\n", timestamps.GetRange(0, maxSummary)) + $"\n(+{timestamps.Count - maxSummary})"
                    : string.Join("\n", timestamps);
                string detailSummary = details.Count > maxSummary
                    ? string.Join("\n", details.GetRange(0, maxSummary)) + $"\n(+{details.Count - maxSummary})"
                    : string.Join("\n", details);
                var row = new HistoryRow {
                    Index = idx + 1,
                    Action = actionType,
                    NumberOfNotes = isMoveAction && moveDrums.Count >= 2 ? moveDrums.Count / 2 : details.Count,
                    DrumSummary = drumSummary,
                    DrumFull = string.Join(", ", drums),
                    BeatSummary = beatSummary,
                    BeatFull = string.Join(", ", beats),
                    TimestampSummary = timestampSummary,
                    TimestampFull = string.Join(", ", timestamps),
                    DetailSummary = detailSummary,
                    DetailFull = string.Join("\n", details),
                    RawIndex = idx
                };
                row.ShowDetailCommand = new RelayCommand(() => MessageBox.Show(row.DetailFull, $"Details for row {row.Index}", MessageBoxButton.OK, MessageBoxImage.Information));
                rows.Add(row);
            }
            HistoryListBox.ItemsSource = rows;
            HistoryListBox.SelectedIndex = count - history.GetCurrentIndex();
        }

        // RelayCommand helper for binding
        public class RelayCommand : System.Windows.Input.ICommand
        {
            private readonly System.Action _execute;
            public RelayCommand(System.Action execute) { _execute = execute; }
            public event System.EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) => _execute();
        }
    }
}