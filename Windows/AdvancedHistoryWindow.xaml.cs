using System.Windows;
using System.Windows.Controls;
using Edda.Classes.MapEditorNS;
using System.Collections.Generic;

namespace Edda.Windows
{
    public partial class AdvancedHistoryWindow : Window
    {
        // highlight notes when selecting a history line
        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history == null) return;
            if (HistoryListBox.SelectedItem is HistoryRow row)
            {
                var allEdits = history.GetHistory();
                if (row.RawIndex >= 0 && row.RawIndex < allEdits.Count)
                {
                    var editList = allEdits[row.RawIndex];
                    var notes = new List<Edda.Classes.MapEditorNS.NoteNS.Note>();
                    foreach (var edit in editList.items)
                    {
                        if (edit.item is Edda.Classes.MapEditorNS.NoteNS.Note note)
                            notes.Add(note);
                    }
                    // Select and highlight the relevant notes
                    mapEditor.SelectNewNotes(notes, false);
                }
            }
        }
        private class HistoryRow
        {
            public int Index { get; set; }
            public string Action { get; set; }
            public string Drum { get; set; }
            public string Beat { get; set; }
            public string Timestamp { get; set; }
            public int RawIndex { get; set; } // for navigation 
        }
        private MapEditor mapEditor;
        public AdvancedHistoryWindow(MapEditor editor)
        {
            InitializeComponent();
            mapEditor = editor;
            HistoryListBox.SelectionChanged += HistoryListBox_SelectionChanged;
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history != null)
                history.HistoryChanged += RefreshHistory;
            RefreshHistory();
        }

        private void HistoryListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history == null) return;
            if (HistoryListBox.SelectedItem is HistoryRow row)
            {
                int targetIndex = row.RawIndex + 1;
                while (history.GetCurrentIndex() > targetIndex)
                    mapEditor.Undo();
                while (history.GetCurrentIndex() < targetIndex)
                    mapEditor.Redo();
                    // always reselect the double-clicked line
                    if (HistoryListBox.ItemsSource is IList<HistoryRow> rows)
                    {
                        for (int i = 0; i < rows.Count; i++)
                        {
                            if (rows[i].RawIndex == row.RawIndex)
                            {
                                HistoryListBox.SelectedIndex = i;
                                break;
                            }
                        }
                    }
            }
        }

        public void RefreshHistory()
        {
            var history = mapEditor?.currentMapDifficulty?.editorHistory;
            if (history == null) return;
            var allEdits = history.GetHistory();
            int count = allEdits.Count;
            var rows = new List<HistoryRow>();
            for (int idx = count - 1; idx >= 0; idx--)
            {
                var editList = allEdits[idx];
                string actionType = "";
                List<string> drums = new List<string>();
                List<string> beats = new List<string>();
                List<string> timestamps = new List<string>();
                foreach (var edit in editList.items)
                {
                    if (edit.item is Edda.Classes.MapEditorNS.NoteNS.Note note)
                    {
                        drums.Add(note.col switch { 0 => "D1", 1 => "D2", 2 => "D3", 3 => "D4", _ => $"D{note.col+1}" });
                        beats.Add(note.beat.ToString("0.###"));
                        double bpm = mapEditor?.GlobalBPM ?? 120.0;
                        double timestampMs = note.beat * 60000.0 / bpm;
                        int minutes = (int)(timestampMs / 60000);
                        int seconds = (int)((timestampMs % 60000) / 1000);
                        int millis = (int)(timestampMs % 1000);
                        timestamps.Add($"{minutes}:{seconds:D2}.{millis:D3}");
                        actionType = edit.isAdd ? "Add" : "Remove";
                    }
                    else
                    {
                        actionType = edit.item.ToString();
                    }
                }
                rows.Add(new HistoryRow
                {
                    Index = idx + 1,
                    Action = actionType,
                    Drum = string.Join("\n", drums),
                    Beat = string.Join("\n", beats),
                    Timestamp = string.Join("\n", timestamps),
                    RawIndex = idx
                });
            }
            HistoryListBox.ItemsSource = rows;
            HistoryListBox.SelectedIndex = count - history.GetCurrentIndex();
        }
    }
}
