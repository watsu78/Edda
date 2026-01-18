using System.Threading;
using System.Threading.Tasks;
using Edda;
using Edda.Classes.MapEditorNS;
using Edda.Classes.MapEditorNS.NoteNS;
using Edda.Const;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using DrawingColor = System.Drawing.Color;
using Image = System.Windows.Controls.Image;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;

public class EditorGridController : IDisposable {
    // Coalesced redraw scheduling
    private bool redrawScheduled = false;
    private bool pendingRedrawWaveform = false;
    private CancellationTokenSource? redrawCts;
    private const int InteractiveDebounceMs = 16; // ~60 FPS

    MapEditor mapEditor;

    // constructor variables
    MainWindow parentWindow;
    Canvas MainWaveform;
    Canvas EditorGrid;
    ScrollViewer scrollEditor;
    ColumnDefinition referenceCol;
    RowDefinition referenceRow;
    Border borderNavWaveform;
    ColumnDefinition colWaveformVertical;
    Image imgWaveformVertical;
    ScrollViewer scrollSpectrogram;
    StackPanel panelSpectrogram;
    Canvas canvasSpectrogramLowerOffset;
    Canvas canvasSpectrogramUpperOffset;
    Image[] imgSpectrogramChunks;
    ImageSource[] cachedSpectrogramImages;
    System.Threading.CancellationTokenSource? spectrogramCts;
    System.Threading.CancellationTokenSource? mainWaveformCts;
    System.Threading.CancellationTokenSource? navWaveformCts;
    Grid editorMarginGrid;
    Canvas canvasNavInputBox;
    Canvas canvasNavNotes;
    Image navNotesImage;
    
    Canvas canvasBookmarks;
    Canvas canvasBookmarkLabels;
    Canvas canvasTimingChanges;
    Canvas canvasTimingChangeLabels;
    Line lineSongMouseover;
    // cached nav note brushes
    SolidColorBrush[] navNoteBrushes = new SolidColorBrush[4];
    SolidColorBrush navSelectedBrush;
    bool navBrushesInitialised = false;

    // dispatcher of the main application UI thread
    Dispatcher dispatcher;

    // user-defined settings 
    public double gridSpacing;
    public int gridDivision;
    public bool showWaveform;
    public bool snapToGrid = true;
    // spectrogram settings
    public bool? showSpectrogram = null;
    public bool spectrogramCache = true;
    public VorbisSpectrogramGenerator.SpectrogramType spectrogramType = VorbisSpectrogramGenerator.SpectrogramType.Standard;
    public VorbisSpectrogramGenerator.SpectrogramQuality spectrogramQuality = VorbisSpectrogramGenerator.SpectrogramQuality.Medium;
    public int spectrogramFrequency = Editor.Spectrogram.DefaultFreq;
    public string spectrogramColormap = Spectrogram.Colormap.Blues.Name;
    public bool spectrogramFlipped = false;
    public bool? spectrogramChunking = null;

    // dynamically added controls
    Border dragSelectBorder = new();
    Line lineGridMouseover = new();
    Canvas noteCanvas = new();
    Image imgAudioWaveform = new();
    Image imgPreviewNote = new();

    // rune tint cache
    Dictionary<string, BitmapSource> runeTintCache = new();
    MediaColor[] runeBaseColors = new MediaColor[4];
    MediaColor runeHighlightColor;
    bool tintColorsInitialised = false;
    double[] runeTintIntensityCols = new double[4] { 0.5, 0.5, 0.5, 0.5 };
    // pooled resources for rune tinting
    Dictionary<string, WriteableBitmap> runeWbPool = new();
    Dictionary<string, byte[]> runePixelPool = new();

    // editing grid
    bool mouseOutOfBounds;
    List<double> gridBeatLines = new();
    List<double> majorGridBeatLines = new();

    // waveform
    VorbisSpectrogramGenerator audioSpectrogram;
    VorbisWaveformGenerator audioWaveform;
    VorbisWaveformGenerator navWaveform;

    // Virtualized nav layers (bookmarks & BPM changes)
    Dictionary<double, Line> navBookmarkLines = new();
    Dictionary<double, Label> navBookmarkLabels = new();
    Dictionary<double, Line> navBpmLines = new();
    Dictionary<double, List<Label>> navBpmLabels = new();

    // marker editing
    bool isEditingMarker = false;
    double markerDragOffset = 0;
    Canvas currentlyDraggingMarker;
    Bookmark currentlyDraggingBookmark;
    BPMChange currentlyDraggingBPMChange;

    // dragging variables
    bool isDragging = false;
    Point dragSelectStart;

    // other
    public bool isMapDifficultySelected {
        get {
            return mapEditor?.currentMapDifficulty != null;
        }
    }
    public int currentMapDifficultyIndex {
        get {
            return mapEditor.currentDifficultyIndex;
        }
    }
    public SortedSet<Note> currentMapDifficultyNotes {
        get {
            return mapEditor?.currentMapDifficulty?.notes;
        }
    }
    public SortedSet<BPMChange> currentMapDifficultyBpmChanges {
        get {
            return mapEditor.currentMapDifficulty.bpmChanges;
        }
    }

    // grid measurements
    double unitLength {
        get { return referenceCol.ActualWidth * gridSpacing; }
    }
    double unitLengthUnscaled {
        get { return referenceCol.ActualWidth; }
    }
    double unitSubLength {
        get { return referenceCol.ActualWidth / 3; }
    }
    double unitHeight {
        get { return referenceRow.ActualHeight; }
    }
    double currentSeekBeat {
        get {
            return BeatForPosition(scrollEditor.VerticalOffset + scrollEditor.ActualHeight - unitLengthUnscaled / 2, snapToGrid);
        }
    }

    // info on currently selected beat/col from mouse position
    int mouseGridCol;
    double mouseBeatUnsnapped;
    double mouseBeatSnapped;
    public Note mouseNote {
        get {
            return new Note(mouseBeat, mouseColumn);
        }
    }
    public double mouseBeat {
        get {
            return snapToGrid ? snappedBeat : unsnappedBeat;
        }
    }
    public double snappedBeat {
        get {
            return mouseBeatSnapped;
        }
    }
    public double unsnappedBeat {
        get {
            return mouseBeatUnsnapped;
        }
    }
    public int mouseColumn {
        get {
            return mouseGridCol;
        }
    }
    public bool isMouseOnEditingGrid {
        get {
            return imgPreviewNote.Opacity > 0;
        }
    }

    // constructor
    public EditorGridController(
        MainWindow parentWindow,
        Canvas MainWaveform,
        Canvas EditorGrid,
        ScrollViewer scrollEditor,
        ColumnDefinition referenceCol,
        RowDefinition referenceRow,
        Border borderNavWaveform,
        ColumnDefinition colWaveformVertical,
        Image imgWaveformVertical,
        ScrollViewer scrollSpectrogram,
        StackPanel panelSpectrogram,
        Grid editorMarginGrid,
        Canvas canvasNavInputBox,
        Canvas canvasNavNotes,
        Canvas canvasBookmarks,
        Canvas canvasBookmarkLabels,
        Canvas canvasTimingChanges,
        Canvas canvasTimingChangeLabels,
        Line lineSongMouseover
    ) {
        this.parentWindow = parentWindow;
        this.MainWaveform = MainWaveform;
        this.EditorGrid = EditorGrid;
        this.referenceCol = referenceCol;
        this.referenceRow = referenceRow;
        this.scrollEditor = scrollEditor;
        this.borderNavWaveform = borderNavWaveform;
        this.colWaveformVertical = colWaveformVertical;
        this.imgWaveformVertical = imgWaveformVertical;
        this.scrollSpectrogram = scrollSpectrogram;
        this.panelSpectrogram = panelSpectrogram;
        this.editorMarginGrid = editorMarginGrid;
        this.canvasNavInputBox = canvasNavInputBox;
        this.canvasNavNotes = canvasNavNotes;
        this.canvasBookmarks = canvasBookmarks;
        this.canvasBookmarkLabels = canvasBookmarkLabels;
        this.canvasTimingChanges = canvasTimingChanges;
        this.canvasTimingChangeLabels = canvasTimingChangeLabels;
        this.lineSongMouseover = lineSongMouseover;

        dispatcher = parentWindow.Dispatcher;

        imgWaveformVertical.Opacity = Editor.NavWaveformOpacity;
        imgWaveformVertical.Stretch = Stretch.Fill;

        SetupSpectrogramContent();

        lineSongMouseover.Opacity = 0;

        RenderOptions.SetBitmapScalingMode(imgAudioWaveform, BitmapScalingMode.NearestNeighbor);

        noteCanvas.SetBinding(Canvas.WidthProperty, new Binding("ActualWidth") { Source = EditorGrid });
        noteCanvas.SetBinding(Canvas.HeightProperty, new Binding("ActualHeight") { Source = EditorGrid });

        imgPreviewNote.Opacity = Editor.PreviewNoteOpacity;
        imgPreviewNote.Width = unitLength;
        imgPreviewNote.Height = unitHeight;
        noteCanvas.Children.Add(imgPreviewNote);

        dragSelectBorder.BorderBrush = Brushes.Black;
        dragSelectBorder.BorderThickness = new Thickness(2);
        dragSelectBorder.Background = Brushes.LightBlue;
        dragSelectBorder.Opacity = 0.5;
        dragSelectBorder.Visibility = Visibility.Hidden;

        lineGridMouseover.Opacity = 0;
        lineGridMouseover.X1 = 0;
        lineGridMouseover.SetBinding(Line.X2Property, new Binding("ActualWidth") { Source = EditorGrid });
        lineGridMouseover.Stroke = (SolidColorBrush)new BrushConverter().ConvertFrom(Editor.GridPreviewLine.Colour);
        lineGridMouseover.StrokeThickness = Editor.GridPreviewLine.Thickness;
        lineGridMouseover.Visibility = Visibility.Hidden;
        EditorGrid.Children.Add(lineGridMouseover);

        // Single image host for batched nav notes drawing
        navNotesImage = new Image();
        navNotesImage.Stretch = Stretch.None;
        navNotesImage.SnapsToDevicePixels = true;
        navNotesImage.CacheMode = new BitmapCache();
        RenderOptions.SetBitmapScalingMode(navNotesImage, BitmapScalingMode.LowQuality);
        navNotesImage.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = borderNavWaveform });
        navNotesImage.SetBinding(FrameworkElement.HeightProperty, new Binding("ActualHeight") { Source = borderNavWaveform });
        if (!canvasNavNotes.Children.Contains(navNotesImage)) {
            canvasNavNotes.Children.Add(navNotesImage);
        }
    }

    // Request a coalesced redraw; multiple calls within debounce window merge into one DrawGrid
    public void RequestRedraw(bool redrawWaveform = true, int? debounceMs = null) {
        int delay = debounceMs ?? InteractiveDebounceMs;
        pendingRedrawWaveform = pendingRedrawWaveform || redrawWaveform;
        if (redrawScheduled) {
            // push out the scheduled redraw with updated flags
            try { redrawCts?.Cancel(); } catch { }
        } else {
            redrawScheduled = true;
        }
        var cts = new CancellationTokenSource();
        redrawCts = cts;
        Task.Run(async () => {
            try {
                await Task.Delay(delay, cts.Token);
                dispatcher.Invoke(() => {
                    var rw = pendingRedrawWaveform;
                    pendingRedrawWaveform = false;
                    redrawScheduled = false;
                    DrawGrid(rw);
                });
            } catch (TaskCanceledException) {
                // coalesced; a new request will reschedule
            }
        });
    }

    public void Dispose() {
        // Clear the most memory-heavy components
        noteCanvas.Children.Clear();
        EditorGrid.Children.Clear();
        MainWaveform.Children.Clear();
        panelSpectrogram.Children.Clear();
        imgWaveformVertical.Source = null;
        imgAudioWaveform.Source = null;
        foreach (var imgSpectorgramChunk in imgSpectrogramChunks) {
            imgSpectorgramChunk.Source = null;
        }

        // Unbind references
        mapEditor = null;
        parentWindow = null;
        MainWaveform = null;
        EditorGrid = null;
        scrollEditor = null;
        referenceCol = null;
        referenceRow = null;
        borderNavWaveform = null;
        colWaveformVertical = null;
        imgWaveformVertical = null;
        scrollSpectrogram = null;
        panelSpectrogram = null;
        canvasSpectrogramLowerOffset = null;
        canvasSpectrogramUpperOffset = null;
        imgSpectrogramChunks = null;
        cachedSpectrogramImages = null;
        // cancel any in-flight rendering tasks
        try { spectrogramCts?.Cancel(); } catch { }
        try { mainWaveformCts?.Cancel(); } catch { }
        try { navWaveformCts?.Cancel(); } catch { }
        spectrogramCts?.Dispose(); spectrogramCts = null;
        mainWaveformCts?.Dispose(); mainWaveformCts = null;
        navWaveformCts?.Dispose(); navWaveformCts = null;
        editorMarginGrid = null;
        canvasNavInputBox = null;
        canvasNavNotes = null;
        canvasBookmarks = null;
        canvasBookmarkLabels = null;
        canvasTimingChanges = null;
        canvasTimingChangeLabels = null;
        lineSongMouseover = null;
        dispatcher = null;
        dragSelectBorder = null;
        lineGridMouseover = null;
        noteCanvas = null;
        imgAudioWaveform = null;
        imgPreviewNote = null;

        audioSpectrogram?.Dispose();
        audioSpectrogram = null;
        audioWaveform?.Dispose();
        audioWaveform = null;
        navWaveform?.Dispose();
        navWaveform = null;

        // Clear rune tint pools
        runeWbPool.Clear();
        runePixelPool.Clear();

        currentlyDraggingMarker = null;
        currentlyDraggingBookmark = null;
        currentlyDraggingBPMChange = null;
    }

    // pooled helpers for tinting
    private string RuneSizeKey(int width, int height) {
        return $"{width}x{height}";
    }
    private WriteableBitmap GetScratchWriteableBitmap(int width, int height, double dpiX, double dpiY) {
        var key = RuneSizeKey(width, height);
        if (!runeWbPool.TryGetValue(key, out var wb) || wb.PixelWidth != width || wb.PixelHeight != height) {
            wb = new WriteableBitmap(width, height, dpiX, dpiY, PixelFormats.Bgra32, null);
            runeWbPool[key] = wb;
        }
        return wb;
    }
    private byte[] GetScratchPixelBuffer(int width, int height) {
        var key = RuneSizeKey(width, height);
        if (!runePixelPool.TryGetValue(key, out var buf) || buf.Length != width * height * 4) {
            buf = new byte[width * height * 4];
            runePixelPool[key] = buf;
        }
        return buf;
    }

    public void InitMap(MapEditor me) {
        this.mapEditor = me;
        // No retained gridlines initialization; gridlines are drawn as individual Line elements
    }

    public void SetMouseoverLinePosition(double newPos) {
        lineGridMouseover.Y1 = newPos;
        lineGridMouseover.Y2 = newPos;
    }
    public void SetSongMouseoverLinePosition(double newLinePos) {
        lineSongMouseover.Y1 = newLinePos;
        lineSongMouseover.Y2 = newLinePos;
    }
    public void SetMouseoverLineVisibility(Visibility newVis) {
        lineGridMouseover.Visibility = newVis;
    }
    public void SetPreviewNoteVisibility(Visibility newVis) {
        imgPreviewNote.Visibility = newVis;
    }
    public void SetPreviewNote(double bottom, double left, ImageSource source) {
        Canvas.SetBottom(imgPreviewNote, bottom);
        imgPreviewNote.Source = source;
        Canvas.SetLeft(imgPreviewNote, left);
    }

    // waveform drawing
    public void InitWaveforms(string songPath) {
        // cancel running renders before reimporting
        try { spectrogramCts?.Cancel(); } catch { }
        try { mainWaveformCts?.Cancel(); } catch { }
        try { navWaveformCts?.Cancel(); } catch { }
        audioSpectrogram = new VorbisSpectrogramGenerator(songPath, spectrogramCache, spectrogramType, spectrogramQuality, spectrogramFrequency, spectrogramColormap, spectrogramFlipped);
        cachedSpectrogramImages = null; // reimport audio → force regeneration
        audioWaveform = new VorbisWaveformGenerator(songPath, Editor.Waveform.ColourWPF);
        navWaveform = new VorbisWaveformGenerator(songPath, (MediaColor)(ColorConverter.ConvertFromString(parentWindow.GetUserSetting(UserSettingsKey.NavWaveformColor)) ?? Editor.Waveform.ColourWPF));
    }
    public void RefreshSpectrogramWaveform() {
        // cancel running spectrogram render on settings change
        try { spectrogramCts?.Cancel(); } catch { }
        audioSpectrogram?.InitSettings(spectrogramCache, spectrogramType, spectrogramQuality, spectrogramFrequency, spectrogramColormap, spectrogramFlipped);
        cachedSpectrogramImages = null; // settings change → invalidate local cache
    }
    public void DrawScrollingWaveforms() {
        if (showWaveform) {
            DrawMainWaveform();
        }
        if (showSpectrogram == true) {
            DrawSpectrogram();
        }
    }
    public void DrawMainWaveform() {
        if (!MainWaveform.Children.Contains(imgAudioWaveform)) {
            MainWaveform.Children.Add(imgAudioWaveform);
        }
        ResizeMainWaveform();
        double height = MainWaveform.Height - scrollEditor.ActualHeight;
        double width = MainWaveform.ActualWidth * Editor.Waveform.Width;
        CreateMainWaveform(height, width);
    }
    public void UndrawMainWaveform() {
        MainWaveform.Children.Remove(imgAudioWaveform);
    }
    private void ResizeMainWaveform() {
        imgAudioWaveform.Height = MainWaveform.Height - scrollEditor.ActualHeight;
        imgAudioWaveform.Width = MainWaveform.ActualWidth;
        Canvas.SetBottom(imgAudioWaveform, unitHeight / 2);
    }
    private void CreateMainWaveform(double height, double width) {
        // cancel previous waveform task and start a new one
        try { mainWaveformCts?.Cancel(); } catch { }
        mainWaveformCts = new System.Threading.CancellationTokenSource();
        var token = mainWaveformCts.Token;
        Task.Run(() => {
            DateTime before = DateTime.Now;
            if (token.IsCancellationRequested) return;
            ImageSource bmp = audioWaveform.Draw(height, width);
            Trace.WriteLine($"INFO: Drew big waveform in {(DateTime.Now - before).TotalSeconds} sec");

            if (token.IsCancellationRequested) return;
            this.dispatcher.Invoke(() => {
                if (token.IsCancellationRequested) return;
                if (bmp != null && showWaveform) {
                    imgAudioWaveform.Source = bmp;
                    ResizeMainWaveform();
                }
            });
        }, token);
    }
    internal void DrawNavWaveform() {
        // cancel previous nav waveform task and start a new one
        try { navWaveformCts?.Cancel(); } catch { }
        navWaveformCts = new System.Threading.CancellationTokenSource();
        var token = navWaveformCts.Token;
        Task.Run(() => {
            DateTime before = DateTime.Now;
            if (token.IsCancellationRequested) return;
            navWaveform.ChangeColor((MediaColor)(ColorConverter.ConvertFromString(parentWindow.GetUserSetting(UserSettingsKey.NavWaveformColor)) ?? Editor.Waveform.ColourWPF));
            if (token.IsCancellationRequested) return;
            ImageSource bmp = navWaveform.Draw(borderNavWaveform.ActualHeight, colWaveformVertical.ActualWidth);
            Trace.WriteLine($"INFO: Drew nav waveform in {(DateTime.Now - before).TotalSeconds} sec");

            if (token.IsCancellationRequested) return;
            if (bmp != null) {
                this.dispatcher.Invoke(() => {
                    if (token.IsCancellationRequested) return;
                    imgWaveformVertical.Source = bmp;
                });
            }
        }, token);
    }
    public void SetupSpectrogramContent() {
        panelSpectrogram.Children.Clear();
        // Upper offset
        canvasSpectrogramUpperOffset = new Canvas();
        canvasSpectrogramUpperOffset.SnapsToDevicePixels = true;
        panelSpectrogram.Children.Add(canvasSpectrogramUpperOffset);
        // Image chunks - inserted in inverse order
        var numChunks = spectrogramChunking.HasValue ? (spectrogramChunking.Value ? Editor.Spectrogram.NumberOfChunks : 1) : 0;
        imgSpectrogramChunks = new Image[numChunks];
        for (int i = 0; i < numChunks; ++i) {
            Image imgChunk = new Image();
            imgChunk.Stretch = Stretch.Fill;
            imgChunk.SnapsToDevicePixels = true;
            imgSpectrogramChunks[i] = imgChunk;
            panelSpectrogram.Children.Insert(1, imgChunk);
        }
        // Lower offset
        canvasSpectrogramLowerOffset = new Canvas();
        canvasSpectrogramLowerOffset.SnapsToDevicePixels = true;
        panelSpectrogram.Children.Add(canvasSpectrogramLowerOffset);
    }
    private void ResizeSpectrogram() {
        // Upper offset
        canvasSpectrogramUpperOffset.Height = scrollEditor.ActualHeight - (unitHeight / 2);
        // Image chunks
        var numChunks = imgSpectrogramChunks.Length;
        for (int i = 0; i < numChunks; ++i) {
            Image imgChunk = imgSpectrogramChunks[i];
            imgChunk.Width = scrollSpectrogram.ActualWidth;
            imgChunk.Height = (EditorGrid.Height - scrollEditor.ActualHeight) / (double)numChunks;
        }
        // Lower offset
        canvasSpectrogramLowerOffset.Height = unitHeight / 2;
    }
    internal void DrawSpectrogram() {
        ResizeSpectrogram();
        CreateSpectrogram();
    }
    private void CreateSpectrogram() {
        // cancel previous spectrogram task and start a new one
        try { spectrogramCts?.Cancel(); } catch { }
        spectrogramCts = new System.Threading.CancellationTokenSource();
        var token = spectrogramCts.Token;
        Task.Run(() => {
            DateTime before = DateTime.Now;
            var numChunks = EditorGrid.ActualHeight == 0 || scrollSpectrogram.ActualWidth == 0 ? 0 : imgSpectrogramChunks.Length;
            if (token.IsCancellationRequested) return;
            ImageSource[] bmps = null;
            // Reuse existing chunk images on resize; regenerate only if cache is missing or parameters changed
            if (cachedSpectrogramImages != null && cachedSpectrogramImages.Length == numChunks && cachedSpectrogramImages.All(img => img != null)) {
                bmps = cachedSpectrogramImages;
            } else {
                bmps = audioSpectrogram.Draw(numChunks);
                cachedSpectrogramImages = bmps;
            }
            Trace.WriteLine($"INFO: Drew spectrogram in {(DateTime.Now - before).TotalSeconds} sec");

            if (token.IsCancellationRequested) return;
            if (bmps != null && bmps.Length == numChunks) {
                this.dispatcher.Invoke(() => {
                    if (token.IsCancellationRequested) return;
                    double totalBmpLength = bmps.Sum(bmp => bmp.Height);
                    for (int i = 0; i < numChunks; ++i) {
                        if (bmps != null) {
                            imgSpectrogramChunks[i].Source = bmps[i];
                            imgSpectrogramChunks[i].Height = (EditorGrid.Height - scrollEditor.ActualHeight) * bmps[i].Height / totalBmpLength;
                        }
                    }
                    DrawingColor bgColor = audioSpectrogram.GetBackgroundColor();
                    var spectrogramBackgroundBrush = new SolidColorBrush(MediaColor.FromArgb(bgColor.A, bgColor.R, bgColor.G, bgColor.B));
                    canvasSpectrogramLowerOffset.Background = spectrogramBackgroundBrush;
                    canvasSpectrogramUpperOffset.Background = spectrogramBackgroundBrush;
                });
            }
        }, token);
    }

    // grid drawing
    public void UpdateGridHeight() {
        // resize editor grid height to fit scrollEditor height
        if (parentWindow.songTotalTimeInSeconds.HasValue) {
            double beats = mapEditor.GlobalBPM / 60 * parentWindow.songTotalTimeInSeconds.Value;
            EditorGrid.Height = beats * unitLength + scrollEditor.ActualHeight;
        }
    }
    public void DrawGrid(bool redrawWaveform = true) {
        UpdateGridHeight();

        EditorGrid.Children.Clear();

        DateTime start = DateTime.Now;

        // draw gridlines using Line elements
        DrawGridLines(EditorGrid.Height - scrollEditor.ActualHeight);
        // mouseover line above gridlines
        EditorGrid.Children.Add(lineGridMouseover);

        // then draw the waveform
        if (redrawWaveform && EditorGrid.Height - scrollEditor.ActualHeight > 0) {
            DrawScrollingWaveforms();
        }

        // then draw the notes
        noteCanvas.Children.Clear();
        canvasNavNotes.Children.Clear();
        DrawNotes(mapEditor.currentMapDifficulty.notes);
        DrawNavNotes(mapEditor.currentMapDifficulty.notes);
        HighlightNotes(mapEditor.currentMapDifficulty.selectedNotes);
        HighlightNavNotes(mapEditor.currentMapDifficulty.selectedNotes);

        // including the mouseover preview note
        imgPreviewNote.Width = unitLength;
        imgPreviewNote.Height = unitHeight;
        noteCanvas.Children.Add(imgPreviewNote);

        EditorGrid.Children.Add(noteCanvas);

        // then the drag selection rectangle
        EditorGrid.Children.Add(dragSelectBorder);

        // finally, draw the markers
        DrawBookmarks();
        DrawNavBookmarks();
        DrawBPMChanges();
        DrawNavBPMChanges();

        Trace.WriteLine($"INFO: Redrew editor grid in {(DateTime.Now - start).TotalSeconds} seconds.");
    }
    internal void DrawGridLines(double gridHeight) {
        // Draw gridlines as individual Line UI elements
        majorGridBeatLines.Clear();
        gridBeatLines.Clear();

        var minorBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(Editor.MinorGridlineColour);
        var majorBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(Editor.MajorGridlineColour);

        double offset = 0.0;
        double localBPM = mapEditor.GlobalBPM;
        int localGridDiv = gridDivision;
        int counter = 0;
        var bpmChangesEnumerator = mapEditor.currentMapDifficulty.bpmChanges.GetEnumerator();
        var hasNextBpmChange = bpmChangesEnumerator.MoveNext();

        while (offset <= gridHeight) {
            bool isMajor = counter % localGridDiv == 0;
            double y = EditorGrid.Height - (offset + unitHeight / 2);
            var l = MakeLine(EditorGrid.ActualWidth, y);
            l.Stroke = isMajor ? majorBrush : minorBrush;
            l.StrokeThickness = isMajor ? Editor.MajorGridlineThickness : Editor.MinorGridlineThickness;
            EditorGrid.Children.Add(l);
            if (isMajor) {
                majorGridBeatLines.Add(offset / unitLength);
            }
            gridBeatLines.Add(offset / unitLength);

            offset += mapEditor.GlobalBPM / localBPM * unitLength / localGridDiv;
            counter++;

            // check for BPM change
            if (hasNextBpmChange && Helper.DoubleApproxGreaterEqual(offset / unitLength, bpmChangesEnumerator.Current.globalBeat)) {
                BPMChange next = bpmChangesEnumerator.Current;
                offset = next.globalBeat * unitLength;
                localBPM = next.BPM;
                localGridDiv = next.gridDivision;
                hasNextBpmChange = bpmChangesEnumerator.MoveNext();
                counter = 0;
            }
        }

        // end-of-song marker line
        double endOffset = mapEditor.GlobalBPM / 60 * mapEditor.SongDuration * unitLength + unitHeight / 2;
        double endY = EditorGrid.Height - endOffset;
        var endLine = MakeLine(EditorGrid.ActualWidth, endY);
        endLine.Stroke = majorBrush;
        endLine.StrokeThickness = Editor.MajorGridlineThickness;
        EditorGrid.Children.Add(endLine);
    }

    // marker drawing
    internal void DrawBookmarks() {
        foreach (Bookmark b in mapEditor.currentMapDifficulty.bookmarks) {
            Canvas bookmarkCanvas = new();
            Canvas.SetRight(bookmarkCanvas, 0);
            Canvas.SetBottom(bookmarkCanvas, unitLength * b.beat + unitHeight / 2);

            var l = MakeLine(EditorGrid.ActualWidth / 2, unitLength * b.beat);
            l.Stroke = (SolidColorBrush)new BrushConverter().ConvertFrom(Editor.GridBookmark.Colour);
            l.StrokeThickness = Editor.GridBookmark.Thickness;
            l.Opacity = Editor.GridBookmark.Opacity;
            Canvas.SetRight(l, 0);
            Canvas.SetBottom(l, 0);
            bookmarkCanvas.Children.Add(l);

            var txtBlock = new Label();
            txtBlock.Foreground = Brushes.White;
            txtBlock.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(Editor.GridBookmark.NameColour);
            txtBlock.Background.Opacity = Editor.GridBookmark.Opacity;
            txtBlock.Content = b.name;
            txtBlock.FontSize = Editor.GridBookmark.NameSize;
            txtBlock.Padding = new Thickness(Editor.GridBookmark.NamePadding);
            txtBlock.FontWeight = FontWeights.Bold;
            txtBlock.Opacity = 1.0;
            //txtBlock.IsReadOnly = true;
            txtBlock.Cursor = Cursors.Hand;
            Canvas.SetRight(txtBlock, 0);
            Canvas.SetBottom(txtBlock, 0.75 * Editor.GridBookmark.Thickness);

            txtBlock.PreviewMouseLeftButtonDown += new MouseButtonEventHandler((src, e) => {
                if (parentWindow.ctrlKeyDown) {
                    mapEditor.SelectNotesInBookmark(b);
                } else {
                    currentlyDraggingMarker = bookmarkCanvas;
                    currentlyDraggingBookmark = b;
                    currentlyDraggingBPMChange = null;
                    markerDragOffset = e.GetPosition(bookmarkCanvas).Y;
                    SetPreviewNoteVisibility(Visibility.Hidden);
                    EditorGrid.CaptureMouse();
                }
                e.Handled = true;
            });
            txtBlock.MouseDown += new MouseButtonEventHandler((src, e) => {
                if (!(e.ChangedButton == MouseButton.Middle)) {
                    return;
                }
                var res = MessageBox.Show(parentWindow, "Are you sure you want to delete this bookmark?", "Confirm Deletion", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes) {
                    mapEditor.RemoveBookmark(b);
                }
            });
            txtBlock.MouseRightButtonUp += new MouseButtonEventHandler((src, e) => {
                var txtBox = new TextBox();
                txtBox.Text = b.name;
                txtBox.FontSize = Editor.GridBookmark.NameSize;
                Canvas.SetRight(txtBox, Editor.GridBookmark.NamePadding);
                Canvas.SetBottom(txtBox, Canvas.GetBottom(bookmarkCanvas) + Editor.GridBookmark.NamePadding);
                txtBox.LostKeyboardFocus += new KeyboardFocusChangedEventHandler((src, e) => {
                    if (txtBox.Text != "") {
                        mapEditor.RenameBookmark(b, txtBox.Text);
                    }
                    EditorGrid.Children.Remove(txtBox);
                });
                txtBox.KeyDown += new KeyEventHandler((src, e) => {
                    if (e.Key == Key.Escape || e.Key == Key.Enter) {
                        Keyboard.ClearFocus();
                        Keyboard.Focus(parentWindow);
                    }
                });

                EditorGrid.Children.Add(txtBox);
                txtBox.Focus();
                txtBox.SelectAll();

                e.Handled = true;
            });
            bookmarkCanvas.Children.Add(txtBlock);


            EditorGrid.Children.Add(bookmarkCanvas);
        }
    }
    internal void DrawBPMChanges() {
        Label makeBPMChangeLabel(string content) {
            var label = new Label();
            label.Foreground = Brushes.White;
            label.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(Editor.GridBPMChange.NameColour);
            label.Background.Opacity = Editor.GridBPMChange.Opacity;
            label.Content = content;
            label.FontSize = Editor.GridBPMChange.NameSize;
            label.Padding = new Thickness(Editor.GridBPMChange.NamePadding);
            label.FontWeight = FontWeights.Bold;
            label.Opacity = 1.0;
            label.Cursor = Cursors.Hand;
            return label;
        }
        BPMChange prev = new BPMChange(0, mapEditor.GlobalBPM, gridDivision);
        foreach (BPMChange b in mapEditor.currentMapDifficulty.bpmChanges) {
            Canvas bpmChangeCanvas = new();
            Canvas bpmChangeFlagCanvas = new();

            var line = MakeLine(EditorGrid.ActualWidth / 2, unitLength * b.globalBeat);
            line.Stroke = (SolidColorBrush)new BrushConverter().ConvertFrom(Editor.GridBPMChange.Colour);
            line.StrokeThickness = Editor.GridBPMChange.Thickness;
            line.Opacity = Editor.GridBPMChange.Opacity;
            Canvas.SetBottom(line, 0);
            bpmChangeCanvas.Children.Add(line);

            var divLabel = makeBPMChangeLabel($"1/{b.gridDivision} beat");
            divLabel.PreviewMouseRightButtonUp += new MouseButtonEventHandler((src, e) => {
                isEditingMarker = true;
                var txtBox = new TextBox();
                txtBox.Text = b.gridDivision.ToString();
                txtBox.FontSize = Editor.GridBPMChange.NameSize;
                Canvas.SetLeft(txtBox, 12);
                Canvas.SetBottom(txtBox, line.StrokeThickness + 2);
                txtBox.LostKeyboardFocus += new KeyboardFocusChangedEventHandler((src, e) => {
                    int div;
                    if (int.TryParse(txtBox.Text, out div) && Helper.DoubleRangeCheck(div, 1, Editor.GridDivisionMax)) {
                        mapEditor.RemoveBPMChange(b, false);
                        b.gridDivision = div;
                        mapEditor.AddBPMChange(b);
                    }
                    isEditingMarker = false;
                    parentWindow.Cursor = Cursors.Arrow;
                    canvasNavInputBox.Children.Remove(txtBox);
                });
                txtBox.KeyDown += new KeyEventHandler((src, e) => {
                    if (e.Key == Key.Escape || e.Key == Key.Enter) {
                        Keyboard.ClearFocus();
                        Keyboard.Focus(parentWindow);
                    }
                });

                bpmChangeCanvas.Children.Add(txtBox);
                txtBox.Focus();
                txtBox.SelectAll();

                e.Handled = true;
            });
            Canvas.SetBottom(divLabel, line.StrokeThickness);
            bpmChangeFlagCanvas.Children.Add(divLabel);

            var bpmLabel = makeBPMChangeLabel($"{b.BPM} BPM");
            bpmLabel.PreviewMouseRightButtonUp += new MouseButtonEventHandler((src, e) => {
                isEditingMarker = true;
                var txtBox = new TextBox();
                txtBox.Text = b.BPM.ToString();
                txtBox.FontSize = Editor.GridBPMChange.NameSize;
                Canvas.SetLeft(txtBox, 2);
                Canvas.SetBottom(txtBox, line.StrokeThickness + 22);
                txtBox.LostKeyboardFocus += new KeyboardFocusChangedEventHandler((src, e) => {
                    double BPM;
                    if (double.TryParse(txtBox.Text, out BPM) && BPM > 0) {
                        mapEditor.RemoveBPMChange(b, false);
                        b.BPM = BPM;
                        mapEditor.AddBPMChange(b);
                    }
                    isEditingMarker = false;
                    parentWindow.Cursor = Cursors.Arrow;
                    canvasNavInputBox.Children.Remove(txtBox);
                });
                txtBox.KeyDown += new KeyEventHandler((src, e) => {
                    if (e.Key == Key.Escape || e.Key == Key.Enter) {
                        Keyboard.ClearFocus();
                        Keyboard.Focus(parentWindow);
                    }
                });

                bpmChangeCanvas.Children.Add(txtBox);
                txtBox.Focus();
                txtBox.SelectAll();

                e.Handled = true;
            });
            Canvas.SetBottom(bpmLabel, line.StrokeThickness + 20);
            bpmChangeFlagCanvas.Children.Add(bpmLabel);

            bpmChangeFlagCanvas.PreviewMouseLeftButtonDown += new MouseButtonEventHandler((src, e) => {
                if (parentWindow.ctrlKeyDown) {
                    mapEditor.SelectNotesInBPMChange(b);
                } else {
                    currentlyDraggingMarker = bpmChangeCanvas;
                    currentlyDraggingBPMChange = b;
                    currentlyDraggingBookmark = null;
                    markerDragOffset = e.GetPosition(bpmChangeCanvas).Y;
                    SetPreviewNoteVisibility(Visibility.Hidden);
                    EditorGrid.CaptureMouse();
                }
                e.Handled = true;
            });
            bpmChangeFlagCanvas.PreviewMouseDown += new MouseButtonEventHandler((src, e) => {
                if (!(e.ChangedButton == MouseButton.Middle)) {
                    return;
                }
                var res = MessageBox.Show(parentWindow, "Are you sure you want to delete this timing change?", "Confirm Deletion", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes) {
                    mapEditor.RemoveBPMChange(b);
                }
                e.Handled = true;
            });
            Canvas.SetBottom(bpmChangeFlagCanvas, 0);
            bpmChangeCanvas.Children.Add(bpmChangeFlagCanvas);

            Canvas.SetLeft(bpmChangeCanvas, 0);
            Canvas.SetBottom(bpmChangeCanvas, unitLength * b.globalBeat + unitHeight / 2);
            EditorGrid.Children.Add(bpmChangeCanvas);

            prev = b;
        }
    }
    internal void DrawNavBookmarks() {
        if (!parentWindow.songTotalTimeInSeconds.HasValue || mapEditor.currentMapDifficulty == null) {
            return;
        }
        // Compute brush (apply new settings dynamically)
        var bookmarkBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(parentWindow.GetUserSetting(UserSettingsKey.NavBookmarkColor) ?? Editor.NavBookmark.Colour);
        if (bookmarkBrush.CanFreeze) bookmarkBrush.Freeze();
        // Build target set of beats
        var targetBeats = new HashSet<double>(mapEditor.currentMapDifficulty.bookmarks.Select(b => b.beat));

        // Remove orphan lines/labels
        var orphanLineBeats = navBookmarkLines.Keys.Where(k => !targetBeats.Contains(k)).ToList();
        foreach (var beat in orphanLineBeats) {
            var line = navBookmarkLines[beat];
            canvasBookmarks.Children.Remove(line);
            navBookmarkLines.Remove(beat);
        }
        var orphanLabelBeats = navBookmarkLabels.Keys.Where(k => !targetBeats.Contains(k)).ToList();
        foreach (var beat in orphanLabelBeats) {
            var label = navBookmarkLabels[beat];
            canvasBookmarkLabels.Children.Remove(label);
            navBookmarkLabels.Remove(beat);
        }

        // Add/update current bookmarks
        foreach (Bookmark b in mapEditor.currentMapDifficulty.bookmarks) {
            double beat = b.beat;
            double y = borderNavWaveform.ActualHeight * (1 - 60000 * beat / (mapEditor.GlobalBPM * parentWindow.songTotalTimeInSeconds.Value * 1000));

            // line
            if (!navBookmarkLines.TryGetValue(beat, out var l)) {
                l = MakeLine(borderNavWaveform.ActualWidth, y);
                canvasBookmarks.Children.Add(l);
                navBookmarkLines[beat] = l;
            } else {
                // update position/width
                l.X1 = 0; l.X2 = borderNavWaveform.ActualWidth; l.Y1 = y; l.Y2 = y;
            }
            l.Stroke = bookmarkBrush;
            l.StrokeThickness = Editor.NavBookmark.Thickness;
            l.Opacity = Editor.NavBookmark.Opacity;

            // label
            if (!navBookmarkLabels.TryGetValue(beat, out var label)) {
                label = CreateBookmarkLabel(b);
                canvasBookmarkLabels.Children.Add(label);
                navBookmarkLabels[beat] = label;
            } else {
                // update text/content and reposition
                var offset = y;
                if (label.Content is TextBlock tb) {
                    tb.Text = b.name;
                } else {
                    label.Content = new TextBlock { Text = b.name };
                }
                Canvas.SetRight(label, 0);
                Canvas.SetBottom(label, borderNavWaveform.ActualHeight - offset);
                // refresh style in case settings changed
                label.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(parentWindow.GetUserSetting(UserSettingsKey.NavBookmarkNameColor) ?? Editor.NavBookmark.NameColour);
                label.FontSize = Editor.NavBookmark.NameSize;
                label.Padding = new Thickness(Editor.NavBookmark.NamePadding);
                label.Opacity = Editor.NavBookmark.Opacity;
            }
        }
    }

    internal void DrawNavBPMChanges() {
        if (!parentWindow.songTotalTimeInSeconds.HasValue || mapEditor.currentMapDifficulty == null) {
            return;
        }
        // Compute brush (apply new settings dynamically)
        var bpmChangeBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(parentWindow.GetUserSetting(UserSettingsKey.NavBPMChangeColor) ?? Editor.NavBPMChange.Colour);
        if (bpmChangeBrush.CanFreeze) bpmChangeBrush.Freeze();
        // Build target set of beats
        var targetBeats = new HashSet<double>(mapEditor.currentMapDifficulty.bpmChanges.Select(b => b.globalBeat));

        // Remove orphan lines/labels
        var orphanLineBeats = navBpmLines.Keys.Where(k => !targetBeats.Contains(k)).ToList();
        foreach (var beat in orphanLineBeats) {
            var line = navBpmLines[beat];
            canvasTimingChanges.Children.Remove(line);
            navBpmLines.Remove(beat);
        }
        var orphanLabelBeats = navBpmLabels.Keys.Where(k => !targetBeats.Contains(k)).ToList();
        foreach (var beat in orphanLabelBeats) {
            if (navBpmLabels.TryGetValue(beat, out var labels)) {
                foreach (var lbl in labels) {
                    canvasTimingChangeLabels.Children.Remove(lbl);
                }
            }
            navBpmLabels.Remove(beat);
        }

        // Add/update current bpm changes
        foreach (BPMChange b in mapEditor.currentMapDifficulty.bpmChanges) {
            double beat = b.globalBeat;
            double y = borderNavWaveform.ActualHeight * (1 - 60000 * beat / (mapEditor.GlobalBPM * parentWindow.songTotalTimeInSeconds.Value * 1000));

            // line
            if (!navBpmLines.TryGetValue(beat, out var l)) {
                l = MakeLine(borderNavWaveform.ActualWidth, y);
                canvasTimingChanges.Children.Add(l);
                navBpmLines[beat] = l;
            } else {
                l.X1 = 0; l.X2 = borderNavWaveform.ActualWidth; l.Y1 = y; l.Y2 = y;
            }
            l.Stroke = bpmChangeBrush;
            l.StrokeThickness = Editor.NavBPMChange.Thickness;
            l.Opacity = Editor.NavBPMChange.Opacity;

            // labels
            if (!navBpmLabels.TryGetValue(beat, out var labels)) {
                labels = new List<Label>(CreateBPMChangeLabels(b));
                foreach (var lbl in labels) canvasTimingChangeLabels.Children.Add(lbl);
                navBpmLabels[beat] = labels;
            } else {
                // Update label contents and positions
                if (labels.Count >= 2) {
                    // div label
                    labels[0].Content = $"1/{b.gridDivision} beat";
                    Canvas.SetBottom(labels[0], borderNavWaveform.ActualHeight - y);
                    // bpm label
                    labels[1].Content = $"{b.BPM} BPM";
                    Canvas.SetBottom(labels[1], borderNavWaveform.ActualHeight - y + 11);
                }
                // refresh style for both labels
                foreach (var lbl in labels) {
                    lbl.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(parentWindow.GetUserSetting(UserSettingsKey.NavBPMChangeLabelColor) ?? Editor.NavBPMChange.LabelColour);
                    lbl.FontSize = Editor.NavBPMChange.LabelSize;
                    lbl.Padding = new Thickness(Editor.NavBPMChange.LabelPadding);
                    lbl.Opacity = Editor.NavBPMChange.Opacity;
                }
            }
        }
    }

    // note drawing
    internal void DrawNotes(IEnumerable<Note> notes) {
        foreach (var n in notes) {
            var img = new Image();
            img.Width = unitLengthUnscaled;
            img.Height = unitHeight;

            var noteHeight = n.beat * unitLength;
            var noteXOffset = (1 + 4 * n.col) * unitSubLength;

            img.Source = RuneForBeat(n.beat, n.col);

            // this assumes there are no duplicate notes given to us
            img.Uid = Helper.UidGenerator(n);
            var name = Helper.NameGenerator(n);

            if (parentWindow.FindName(name) != null) {
                parentWindow.UnregisterName(name);
            }
            parentWindow.RegisterName(name, img);

            Canvas.SetLeft(img, noteXOffset + editorMarginGrid.Margin.Left);
            Canvas.SetBottom(img, noteHeight);

            noteCanvas.Children.Add(img);
        }
    }
    internal void DrawNotes(Note n) {
        DrawNotes([n]);
    }
    internal void DrawNavNotes(IEnumerable<Note> notes) {
        if (!canvasNavNotes.Children.Contains(navNotesImage)) {
            canvasNavNotes.Children.Add(navNotesImage);
        }
        var allNotes = mapEditor.currentMapDifficulty?.notes ?? new SortedSet<Note>();
        var selectedSet = mapEditor.currentMapDifficulty?.selectedNotes != null
            ? new HashSet<string>(mapEditor.currentMapDifficulty.selectedNotes.Select(s => Helper.UidGenerator(s)))
            : new HashSet<string>();
        navNotesImage.Source = BuildNavNotesDrawing(allNotes, selectedSet);
    }
    internal void DrawNavNotes(Note n) {
        DrawNavNotes([n]);
    }
    internal void UndrawNotes(IEnumerable<Note> notes) {
        foreach (Note n in notes) {
            var nUid = Helper.UidGenerator(n);
            foreach (UIElement u in noteCanvas.Children) {
                if (u.Uid == nUid) {
                    noteCanvas.Children.Remove(u);
                    break;
                }
            }
        }
    }
    internal void UndrawNavNotes(IEnumerable<Note> notes) {
        // Rebuild nav notes drawing from current set excluding provided notes
        if (!canvasNavNotes.Children.Contains(navNotesImage)) {
            canvasNavNotes.Children.Add(navNotesImage);
        }
        var remaining = mapEditor.currentMapDifficulty.notes.Where(n => !notes.Contains(n));
        var selectedSet = mapEditor.currentMapDifficulty?.selectedNotes != null
            ? new HashSet<string>(mapEditor.currentMapDifficulty.selectedNotes.Select(s => Helper.UidGenerator(s)))
            : new HashSet<string>();
        navNotesImage.Source = BuildNavNotesDrawing(remaining, selectedSet);
    }
    internal void UndrawNotes(Note n) {
        UndrawNotes([n]);
    }
    internal void UndrawNavNotes(Note n) {
        UndrawNavNotes([n]);
    }
    internal void HighlightNotes(IEnumerable<Note> notes) {
        foreach (Note n in notes) {
            var noteUid = Helper.UidGenerator(n);
            foreach (UIElement e in noteCanvas.Children) {
                if (e.Uid == noteUid && e is Image img) {
                    img.Source = RuneForBeat(n.beat, n.col, true);
                    break; // UID is unique
                }
            }
        }
    }
    internal void HighlightNavNotes(IEnumerable<Note> notes) {
        if (!canvasNavNotes.Children.Contains(navNotesImage)) {
            canvasNavNotes.Children.Add(navNotesImage);
        }
        var selectedSet = new HashSet<string>(notes.Select(s => Helper.UidGenerator(s)));
        navNotesImage.Source = BuildNavNotesDrawing(mapEditor.currentMapDifficulty.notes, selectedSet);
    }
    internal void HighlightNotes(Note n) {
        HighlightNotes([n]);
    }
    internal void HighlightNavNotes(Note n) {
        HighlightNavNotes([n]);
    }
    internal void HighlightAllNotes() {
        foreach (UIElement e in noteCanvas.Children) {
            if (e is not Image img) continue;
            var n = Helper.NoteFromUid(e.Uid);
            if (n == null) continue;
            img.Source = RuneForBeat(n.beat, n.col, true);
        }
    }
    internal void HighlightAllNavNotes() {
        var allSelected = new HashSet<string>(mapEditor.currentMapDifficulty.notes.Select(n => Helper.UidGenerator(n)));
        navNotesImage.Source = BuildNavNotesDrawing(mapEditor.currentMapDifficulty.notes, allSelected);
    }
    internal void UnhighlightNotes(IEnumerable<Note> notes) {
        foreach (Note n in notes) {
            var noteUid = Helper.UidGenerator(n);
            foreach (UIElement e in noteCanvas.Children) {
                if (e.Uid == noteUid && e is Image img) {
                    img.Source = RuneForBeat(n.beat, n.col);
                    break; // UID is unique
                }
            }
        }
    }
    internal void UnhighlightNavNotes(IEnumerable<Note> notes) {
        if (!canvasNavNotes.Children.Contains(navNotesImage)) {
            canvasNavNotes.Children.Add(navNotesImage);
        }
        navNotesImage.Source = BuildNavNotesDrawing(mapEditor.currentMapDifficulty.notes, new HashSet<string>());
    }
    internal void UnhighlightNotes(Note n) {
        UnhighlightNotes([n]);
    }
    internal void UnhighlightNavNotes(Note n) {
        UnhighlightNavNotes([n]);
    }
    internal void UnhighlightAllNotes() {
        foreach (UIElement e in noteCanvas.Children) {
            if (e is not Image img) continue;
            var n = Helper.NoteFromUid(e.Uid);
            if (n == null) continue;
            img.Source = RuneForBeat(n.beat, n.col);
        }
    }

    internal void UpdateRuneColorsAndRetint() {
        // refresh cached tint colors and retint existing images
        RefreshTintColors();
        RetintAllNoteImages();
    }

    private void RetintAllNoteImages() {
        var selectedSet = new HashSet<string>(mapEditor.currentMapDifficulty.selectedNotes.Select(n => Helper.UidGenerator(n)));
        foreach (UIElement e in noteCanvas.Children) {
            if (e is not Image img) continue;
            var n = Helper.NoteFromUid(e.Uid);
            if (n == null) continue;
            bool isSelected = selectedSet.Contains(e.Uid);
            img.Source = RuneForBeat(n.beat, n.col, isSelected);
        }
    }
    internal void UnhighlightAllNavNotes() {
        if (!canvasNavNotes.Children.Contains(navNotesImage)) {
            canvasNavNotes.Children.Add(navNotesImage);
        }
        navNotesImage.Source = BuildNavNotesDrawing(mapEditor.currentMapDifficulty.notes, new HashSet<string>());
    }
    private DrawingImage BuildNavNotesDrawing(IEnumerable<Note> notes, HashSet<string> selectedSet) {
        var group = new DrawingGroup();
        // Stable coordinate space across the full nav area
        var fullRect = new Rect(0, 0, borderNavWaveform.ActualWidth, borderNavWaveform.ActualHeight);
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(fullRect)));

        // Brushes: cached for performance
        EnsureNavNoteBrushes();

        // StreamGeometry per column for non-selected; one StreamGeometry for selected overlay
        var colGeoms = new StreamGeometry[4] { new StreamGeometry(), new StreamGeometry(), new StreamGeometry(), new StreamGeometry() };
        var selectedGeom = new StreamGeometry();
        double xBase = (borderNavWaveform.ActualWidth - 4 * Editor.NavNote.Size - 3 * Editor.NavNote.ColumnGap) / 2;
        // Precompute visibility flags
        bool[] showCols = new bool[4] {
            parentWindow.GetUserSetting(UserSettingsKey.EnableNavNotesCol0) == "True",
            parentWindow.GetUserSetting(UserSettingsKey.EnableNavNotesCol1) == "True",
            parentWindow.GetUserSetting(UserSettingsKey.EnableNavNotesCol2) == "True",
            parentWindow.GetUserSetting(UserSettingsKey.EnableNavNotesCol3) == "True"
        };
        using (var selCtx = selectedGeom.Open()) {
            for (int c = 0; c < 4; c++) {
                if (!showCols[c]) continue;
                using var ctx = colGeoms[c].Open();
                foreach (var n in notes) {
                    if (n.col != c) continue;
                    // position
                    var verticalOffset = borderNavWaveform.ActualHeight * (1 - 60000 * n.beat / (mapEditor.GlobalBPM * parentWindow.songTotalTimeInSeconds.Value * 1000));
                    double x = xBase + c * (Editor.NavNote.ColumnGap + Editor.NavNote.Size);
                    double y = verticalOffset - Editor.NavNote.Size / 2.0;
                    // geometry rectangle
                    var p0 = new Point(x, y);
                    var p1 = new Point(x + Editor.NavNote.Size, y);
                    var p2 = new Point(x + Editor.NavNote.Size, y + Editor.NavNote.Size);
                    var p3 = new Point(x, y + Editor.NavNote.Size);
                    var isSelected = selectedSet != null && selectedSet.Contains(Helper.UidGenerator(n));
                    if (isSelected) {
                        selCtx.BeginFigure(p0, true, true);
                        selCtx.LineTo(p1, true, false);
                        selCtx.LineTo(p2, true, false);
                        selCtx.LineTo(p3, true, false);
                    } else {
                        ctx.BeginFigure(p0, true, true);
                        ctx.LineTo(p1, true, false);
                        ctx.LineTo(p2, true, false);
                        ctx.LineTo(p3, true, false);
                    }
                }
            }
        }
        foreach (var g in colGeoms) { if (g.CanFreeze) g.Freeze(); }
        if (selectedGeom.CanFreeze) selectedGeom.Freeze();
        // Add drawings: columns then selected overlay
        for (int c = 0; c < 4; c++) {
            if (!showCols[c]) continue;
            group.Children.Add(new GeometryDrawing(navNoteBrushes[c], null, colGeoms[c]));
        }
        group.Children.Add(new GeometryDrawing(navSelectedBrush, null, selectedGeom));

        if (group.CanFreeze) group.Freeze();
        var di = new DrawingImage(group);
        if (di.CanFreeze) di.Freeze();
        return di;
    }

    private void EnsureNavNoteBrushes() {
        if (navBrushesInitialised) return;
        for (int c = 0; c < 4; c++) {
            var colorStr = parentWindow.GetUserSetting(
                c switch {
                    0 => UserSettingsKey.NavNoteColorCol0,
                    1 => UserSettingsKey.NavNoteColorCol1,
                    2 => UserSettingsKey.NavNoteColorCol2,
                    3 => UserSettingsKey.NavNoteColorCol3,
                    _ => UserSettingsKey.NavNoteColor
                }
            ) ?? parentWindow.GetUserSetting(UserSettingsKey.NavNoteColor) ?? Editor.NavNote.Colour;
            var colorParsed = (MediaColor)(ColorConverter.ConvertFromString(colorStr) ?? Colors.White);
            var opaqueColor = MediaColor.FromArgb(255, colorParsed.R, colorParsed.G, colorParsed.B);
            var brush = new SolidColorBrush(opaqueColor);
            brush.Opacity = Editor.NavNote.Opacity;
            if (brush.CanFreeze) brush.Freeze();
            navNoteBrushes[c] = brush;
        }
        var selStr = parentWindow.GetUserSetting(UserSettingsKey.NavSelectedNoteColor) ?? Editor.NavNote.HighlightColour;
        var selParsed = (MediaColor)(ColorConverter.ConvertFromString(selStr) ?? Colors.Yellow);
        var selOpaque = MediaColor.FromArgb(255, selParsed.R, selParsed.G, selParsed.B);
        navSelectedBrush = new SolidColorBrush(selOpaque);
        navSelectedBrush.Opacity = Editor.NavNote.Opacity;
        if (navSelectedBrush.CanFreeze) navSelectedBrush.Freeze();
        navBrushesInitialised = true;
    }

    internal void InvalidateNavNoteBrushes() {
        navBrushesInitialised = false;
    }
    internal List<double> GetBeats() {
        return majorGridBeatLines;
    }

    // mouse input handling
    internal void GridMouseMove(Point mousePos) {
        // check if mouse is out of bounds of the song map
        mouseOutOfBounds = mousePos.Y < scrollEditor.ActualHeight - unitHeight / 2;

        UpdateMousePosition(mousePos);

        double noteX = (1 + 4 * mouseGridCol) * unitSubLength;
        // for some reason Canvas.SetLeft(0) doesn't correspond to the leftmost of the canvas, so we need to do some unknown adjustment to line it up
        var unknownNoteXAdjustment = (unitLength / unitLengthUnscaled - 1) * unitLengthUnscaled / 2;

        var adjustedMousePos = EditorGrid.ActualHeight - mousePos.Y - unitHeight / 2;
        double gridLength = unitLength / gridDivision;

        // calculate column
        mouseGridCol = ColForPosition(mousePos.X);

        if (mouseOutOfBounds) {
            SetMouseoverLineVisibility(Visibility.Hidden);
            SetPreviewNoteVisibility(Visibility.Hidden);
        } else {
            SetMouseoverLineVisibility(Visibility.Visible);


            // set preview note visibility
            if (!isDragging) {
                if (mouseGridCol < 0 || mouseGridCol > 3) {
                    SetPreviewNoteVisibility(Visibility.Hidden);
                } else {
                    SetPreviewNoteVisibility(Visibility.Visible);
                }
            }

            // place preview note   
            double previewNoteBottom = snapToGrid ? (mouseBeatSnapped * gridLength * gridDivision) : Math.Max(adjustedMousePos, 0);
            ImageSource previewNoteSource = RuneForBeat((snapToGrid ? mouseBeatSnapped : mouseBeatUnsnapped), mouseGridCol);
            double previewNoteLeft = noteX - unknownNoteXAdjustment + editorMarginGrid.Margin.Left;
            SetPreviewNote(previewNoteBottom, previewNoteLeft, previewNoteSource);

            // place preview line
            SetMouseoverLinePosition(mousePos.Y - markerDragOffset);
        }

        // move markers if one is being dragged right now
        if (!mouseOutOfBounds && currentlyDraggingMarker != null && !isEditingMarker) {
            MoveMarker(mousePos);
            parentWindow.Cursor = Cursors.Hand;
            // otherwise, update existing drag operations
        } else if (isDragging) {
            UpdateDragSelection(mousePos);
        }
    }
    internal void GridMouseUp(Point mousePos) {

        if (mouseGridCol >= 0 && mouseGridCol < 4) {
            SetPreviewNoteVisibility(Visibility.Visible);
        }
        if (currentlyDraggingMarker != null && !isEditingMarker) {
            var markerPos = mousePos;
            if (mouseOutOfBounds) {
                markerPos.Y = scrollEditor.ActualHeight - unitHeight / 2;
            }
            FinaliseMarkerEdit(markerPos);
        } else if (isDragging) {
            EndDragSelection(mousePos);
        } else if (!mouseOutOfBounds && EditorGrid.IsMouseCaptured && mouseGridCol >= 0 && mouseGridCol < 4) {

            Note n = new Note(mouseBeat, mouseGridCol);

            // select the note if it exists
            if (mapEditor.currentMapDifficulty.notes.Contains(n)) {
                if (parentWindow.shiftKeyDown) {
                    mapEditor.ToggleSelection(n);
                } else {
                    mapEditor.SelectNewNotes(n);
                }
                // otherwise create and add it
            } else {
                mapEditor.AddNotes(n);
                parentWindow.drummer?.Play(n.col);
            }
        }

        EditorGrid.ReleaseMouseCapture();
        isDragging = false;
    }
    internal void GridRightMouseUp() {
        // remove the note
        Note n = mouseNote;
        mapEditor.RemoveNote(n);
    }
    internal void GridMouseDown(Point mousePos) {
        dragSelectStart = mousePos;
        EditorGrid.CaptureMouse();
    }
    internal void BeginDragSelection(Point mousePos) {
        if (isDragging || (currentlyDraggingMarker != null && !isEditingMarker)) {
            return;
        }
        imgPreviewNote.Visibility = Visibility.Hidden;
        dragSelectBorder.Visibility = Visibility.Visible;
        UpdateDragSelection(mousePos);
        dragSelectBorder.Width = 0;
        dragSelectBorder.Height = 0;
        isDragging = true;
    }
    internal void EndDragSelection(Point mousePos) {
        dragSelectBorder.Visibility = Visibility.Hidden;
        // calculate new selections
        double startBeat = BeatForPosition(dragSelectStart.Y, false);
        double endBeat = mouseBeatUnsnapped;
        if (Helper.DoubleApproxGreater(startBeat, endBeat)) {
            (startBeat, endBeat) = (endBeat, startBeat);
        }
        int startCol = ColForPosition(dragSelectStart.X);
        int endCol = mouseGridCol;
        if (startCol > endCol) {
            (startCol, endCol) = (endCol, startCol);
        }
        var newSelection =
            mapEditor.currentMapDifficulty
                .GetNotesRange(startBeat, endBeat, startCol, endCol);
        if (parentWindow.shiftKeyDown) {
            mapEditor.SelectNotes(newSelection);
        } else {
            mapEditor.SelectNewNotes(newSelection);
        }
    }
    internal void MoveMarker(Point mousePos) {
        double newBottom = unitLength * BeatForPosition(mousePos.Y - markerDragOffset, parentWindow.shiftKeyDown);
        Canvas.SetBottom(currentlyDraggingMarker, newBottom + unitHeight / 2);
        SetMouseoverLineVisibility(Visibility.Visible);
    }
    private void FinaliseMarkerEdit(Point mousePos) {
        if (currentlyDraggingBPMChange == null) {
            EditBookmark(BeatForPosition(mousePos.Y - markerDragOffset, parentWindow.shiftKeyDown));
        } else {
            mapEditor.RemoveBPMChange(currentlyDraggingBPMChange, false);
            currentlyDraggingBPMChange.globalBeat = BeatForPosition(mousePos.Y - markerDragOffset, parentWindow.shiftKeyDown);
            mapEditor.AddBPMChange(currentlyDraggingBPMChange);
            DrawGrid(false);
        }
        parentWindow.Cursor = Cursors.Arrow;
        SetMouseoverLineVisibility(Visibility.Hidden);
        currentlyDraggingBPMChange = null;
        currentlyDraggingMarker = null;
        markerDragOffset = 0;
    }
    private void UpdateMousePosition(Point mousePos) {
        // calculate beat
        try {
            mouseBeatSnapped = BeatForPosition(mousePos.Y, true);
            mouseBeatUnsnapped = BeatForPosition(mousePos.Y, false);
        } catch {
            mouseBeatSnapped = 0;
            mouseBeatUnsnapped = 0;
        }
    }
    private void EditBookmark(double beat) {
        mapEditor.RemoveBookmark(currentlyDraggingBookmark);
        currentlyDraggingBookmark.beat = beat;
        mapEditor.AddBookmark(currentlyDraggingBookmark);
        DrawGrid(false);
    }

    // keyboard shortcut functions
    internal void PasteClipboardWithOffset(bool onMouseColumn) {
        mapEditor.PasteClipboard(mouseBeatSnapped, onMouseColumn ? mouseColumn : null);
    }
    internal void CreateBookmark(bool onMouse = true) {
        double beat = currentSeekBeat;
        if (onMouse) {
            if (isMouseOnEditingGrid) {
                beat = snapToGrid ? mouseBeatSnapped : mouseBeatUnsnapped;
                // add bookmark on nav waveform
            } else if (lineSongMouseover.Opacity > 0 && parentWindow.songTotalTimeInSeconds.HasValue) {
                beat = mapEditor.GlobalBPM * parentWindow.songTotalTimeInSeconds.Value / 60000 * (1 - lineSongMouseover.Y1 / borderNavWaveform.ActualHeight);
            }
        }
        mapEditor.AddBookmark(new Bookmark(beat, Editor.NavBookmark.DefaultName));
    }
    internal void CreateBPMChange(bool snappedToGrid, bool onMouse = true) {
        double beat = (snappedToGrid) ? mouseBeatSnapped : mouseBeatUnsnapped;
        if (!onMouse) {
            beat = currentSeekBeat;
        }
        BPMChange previous = new BPMChange(0, mapEditor.GlobalBPM, gridDivision);
        foreach (var b in mapEditor.currentMapDifficulty.bpmChanges) {
            if (b.globalBeat < beat) {
                previous = b;
            }
        }
        mapEditor.AddBPMChange(new BPMChange(beat, previous.BPM, previous.gridDivision));
    }
    internal void AddNoteAt(int col, bool onMouse) {
        double mouseInput = snapToGrid ? mouseBeatSnapped : mouseBeatUnsnapped;
        Note n = new Note(onMouse ? mouseInput : currentSeekBeat, col);
        mapEditor.AddNotes(n);
    }
    internal void ShiftSelectionByRow(MoveNote direction) {
        mapEditor.ShiftSelectionByBeat(direction);
    }

    // helper functions
    private Line MakeLine(double width, double offset) {
        var l = new Line();
        l.X1 = 0;
        l.X2 = width;
        l.Y1 = offset;
        l.Y2 = offset;
        return l;
    }
    private ImageSource RuneForBeat(double beat, int col, bool highlight = false) {
        if (!tintColorsInitialised) {
            RefreshTintColors();
        }
        var lastBPMChange = mapEditor.GetLastBeatChange(beat);
        double beatNormalised = beat - lastBPMChange.globalBeat;
        beatNormalised /= mapEditor.GetGridLength(lastBPMChange.BPM, 1);
        var baseImg = Helper.BitmapImageForBeat(beatNormalised, highlight);
        var uri = baseImg.UriSource?.ToString() ?? baseImg.GetHashCode().ToString();
        string cacheKey = uri + (highlight ? "|H" : "|B") + "|C" + col;
        if (runeTintCache.TryGetValue(cacheKey, out var tinted)) {
            return tinted;
        }
        var color = highlight ? runeHighlightColor : runeBaseColors[Math.Max(0, Math.Min(3, col))];
        var intensity = runeTintIntensityCols[Math.Max(0, Math.Min(3, col))];
        var newBmp = TintBitmap(baseImg, color, intensity);
        runeTintCache[cacheKey] = newBmp;
        return newBmp;
    }

    private void RefreshTintColors() {
        // per-column base colours, fallback to global
        // capture previous values to perform selective cache invalidation
        var wasInitialised = tintColorsInitialised;
        var oldBaseColors = (MediaColor[])runeBaseColors.Clone();
        var oldIntensities = (double[])runeTintIntensityCols.Clone();
        var oldHighlight = runeHighlightColor;
        for (int c = 0; c < 4; c++) {
            var baseBrushCol = (SolidColorBrush)new BrushConverter().ConvertFrom(
                parentWindow.GetUserSetting(
                    c switch {
                        0 => UserSettingsKey.NavNoteColorCol0,
                        1 => UserSettingsKey.NavNoteColorCol1,
                        2 => UserSettingsKey.NavNoteColorCol2,
                        3 => UserSettingsKey.NavNoteColorCol3,
                        _ => UserSettingsKey.NavNoteColor
                    }
                ) ?? parentWindow.GetUserSetting(UserSettingsKey.NavNoteColor) ?? Editor.NavNote.Colour);
            runeBaseColors[c] = baseBrushCol.Color;
        }
        var highlightBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(parentWindow.GetUserSetting(UserSettingsKey.NavSelectedNoteColor) ?? Editor.NavNote.HighlightColour);
        for (int c = 0; c < 4; c++) {
            try {
                runeTintIntensityCols[c] = double.Parse(parentWindow.GetUserSetting(
                    c switch {
                        0 => UserSettingsKey.NavNoteTintIntensityCol0,
                        1 => UserSettingsKey.NavNoteTintIntensityCol1,
                        2 => UserSettingsKey.NavNoteTintIntensityCol2,
                        3 => UserSettingsKey.NavNoteTintIntensityCol3,
                        _ => UserSettingsKey.NavNoteTintIntensity
                    }
                ) ?? DefaultUserSettings.NavNoteTintIntensity.ToString());
                if (runeTintIntensityCols[c] < 0) runeTintIntensityCols[c] = 0;
                if (runeTintIntensityCols[c] > 1) runeTintIntensityCols[c] = 1;
            } catch { runeTintIntensityCols[c] = DefaultUserSettings.NavNoteTintIntensity; }
        }
        runeHighlightColor = highlightBrush.Color;

        // Selective cache invalidation: remove only affected keys
        if (wasInitialised && runeTintCache.Count > 0) {
            var changedColumns = new List<int>();
            for (int c = 0; c < 4; c++) {
                if (!oldBaseColors[c].Equals(runeBaseColors[c]) || Math.Abs(oldIntensities[c] - runeTintIntensityCols[c]) > 1e-9) {
                    changedColumns.Add(c);
                }
            }
            bool highlightChanged = !oldHighlight.Equals(runeHighlightColor);

            if (changedColumns.Count > 0 || highlightChanged) {
                // gather keys to remove (|C{col} matches both base and highlight variants; |H matches all highlights)
                var keysToRemove = new List<string>();
                foreach (var key in runeTintCache.Keys.ToList()) {
                    bool remove = false;
                    if (highlightChanged && key.Contains("|H")) {
                        remove = true;
                    }
                    if (!remove && changedColumns.Count > 0) {
                        foreach (var c in changedColumns) {
                            if (key.Contains("|C" + c)) { remove = true; break; }
                        }
                    }
                    if (remove) keysToRemove.Add(key);
                }
                foreach (var k in keysToRemove) runeTintCache.Remove(k);
            }
        }

        tintColorsInitialised = true;
    }

    private BitmapSource TintBitmap(BitmapSource src, MediaColor tint, double intensity) {
        // ensure format is BGRA32 for pixel access
        BitmapSource baseSrc = src;
        if (src.Format != PixelFormats.Bgra32) {
            baseSrc = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        }
        int width = baseSrc.PixelWidth;
        int height = baseSrc.PixelHeight;
        int stride = width * 4;
        byte[] pixels = GetScratchPixelBuffer(width, height);
        baseSrc.CopyPixels(pixels, stride, 0);

        // precompute tint components (0..255)
        int tr255 = tint.R;
        int tg255 = tint.G;
        int tb255 = tint.B;
        // intensity in fixed-point (0..255)
        int i255 = (int)Math.Round(Math.Max(0, Math.Min(1, intensity)) * 255.0);
        int invI255 = 255 - i255;

        // Span-based inner loop for contiguous memory access
        var span = pixels.AsSpan();
        for (int y = 0; y < height; y++) {
            var rowSpan = span.Slice(y * stride, width * 4);
            for (int x = 0; x < width; x++) {
                int idx = x * 4;
                int b = rowSpan[idx + 0];
                int g = rowSpan[idx + 1];
                int r = rowSpan[idx + 2];
                int a = rowSpan[idx + 3];
                if (a == 0) continue; // transparent
                // integer luminance approximation scaled to 0..255 using (77,150,29)/256
                int lum255 = (77 * r + 150 * g + 29 * b) >> 8;
                // target tint color based on luminance
                int tintR = (lum255 * tr255) / 255;
                int tintG = (lum255 * tg255) / 255;
                int tintB = (lum255 * tb255) / 255;
                // blend original with tint based on intensity (fixed-point)
                int nr = (invI255 * r + i255 * tintR) / 255;
                int ng = (invI255 * g + i255 * tintG) / 255;
                int nb = (invI255 * b + i255 * tintB) / 255;
                rowSpan[idx + 0] = (byte)nb;
                rowSpan[idx + 1] = (byte)ng;
                rowSpan[idx + 2] = (byte)nr;
                // keep alpha
            }
        }

        var scratchWb = GetScratchWriteableBitmap(width, height, baseSrc.DpiX, baseSrc.DpiY);
        scratchWb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        // return frozen clone so pooled WriteableBitmap remains reusable
        var frozenResult = scratchWb.Clone();
        frozenResult.Freeze();
        return frozenResult;
    }
    private Label CreateBookmarkLabel(Bookmark b) {
        if (!double.TryParse(parentWindow.GetUserSetting(UserSettingsKey.NavBookmarkShadowOpacity), out var shadowOpacity)) {
            shadowOpacity = Editor.NavBookmark.ShadowOpacity;
        }
        var offset = borderNavWaveform.ActualHeight * (1 - 60000 * b.beat / (mapEditor.GlobalBPM * parentWindow.songTotalTimeInSeconds.Value * 1000));
        var txtBlock = new TextBlock {
            Text = b.name
        };
        var label = new Label {
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(parentWindow.GetUserSetting(UserSettingsKey.NavBookmarkNameColor) ?? Editor.NavBookmark.NameColour),
            Background = new SolidColorBrush(Colors.Black),
            Content = txtBlock,
            FontSize = Editor.NavBookmark.NameSize,
            Padding = new Thickness(Editor.NavBookmark.NamePadding),
            FontWeight = FontWeights.Bold,
            Opacity = Editor.NavBookmark.Opacity,
            Cursor = Cursors.Hand,

        };
        label.Background.Opacity = shadowOpacity;
        Canvas.SetRight(label, 0);
        Canvas.SetBottom(label, borderNavWaveform.ActualHeight - offset);
        label.MouseLeftButtonDown += new MouseButtonEventHandler((src, e) => {
            e.Handled = true;
        });
        label.MouseLeftButtonUp += new MouseButtonEventHandler((src, e) => {
            if (parentWindow.ctrlKeyDown) {
                mapEditor.SelectNotesInBookmark(b);
            } else {
                parentWindow.songSeekPosition = b.beat / mapEditor.GlobalBPM * 60000;
                parentWindow.navMouseDown = false;
            }
            e.Handled = true;
        });
        label.MouseDown += new MouseButtonEventHandler((src, e) => {
            if (!(e.ChangedButton == MouseButton.Middle)) {
                return;
            }
            var res = MessageBox.Show(parentWindow, "Are you sure you want to delete this bookmark?", "Confirm Deletion", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes) {
                mapEditor.RemoveBookmark(b);
            }
        });
        label.MouseRightButtonUp += new MouseButtonEventHandler((src, e) => {
            var txtBox = new TextBox();
            txtBox.Text = b.name;
            txtBox.FontSize = Editor.NavBookmark.NameSize;
            Canvas.SetRight(txtBox, 0);
            Canvas.SetBottom(txtBox, borderNavWaveform.ActualHeight - offset);
            txtBox.LostKeyboardFocus += new KeyboardFocusChangedEventHandler((src, e) => {
                if (txtBox.Text != "") {
                    mapEditor.RenameBookmark(b, txtBox.Text);
                }
                canvasNavInputBox.Children.Remove(txtBox);
            });
            txtBox.KeyDown += new KeyEventHandler((src, e) => {
                if (e.Key == Key.Escape || e.Key == Key.Enter) {
                    Keyboard.ClearFocus();
                    Keyboard.Focus(parentWindow);
                }
            });

            canvasNavInputBox.Children.Add(txtBox);
            txtBox.Focus();
            txtBox.SelectAll();

            e.Handled = true;
        });
        return label;
    }

    private IEnumerable<Label> CreateBPMChangeLabels(BPMChange b) {
        if (!double.TryParse(parentWindow.GetUserSetting(UserSettingsKey.NavBPMChangeShadowOpacity), out var shadowOpacity)) {
            shadowOpacity = Editor.NavBPMChange.ShadowOpacity;
        }
        var labelBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(parentWindow.GetUserSetting(UserSettingsKey.NavBPMChangeLabelColor) ?? Editor.NavBPMChange.LabelColour);
        Label makeBPMChangeLabel(string content) {
            var label = new Label {
                Foreground = labelBrush,
                Background = new SolidColorBrush(Colors.Black),
                Content = content,
                FontSize = Editor.NavBPMChange.LabelSize,
                Padding = new Thickness(Editor.NavBPMChange.LabelPadding),
                FontWeight = FontWeights.Bold,
                Opacity = Editor.NavBPMChange.Opacity,
                Cursor = Cursors.Hand
            };
            label.Background.Opacity = shadowOpacity;
            label.MouseLeftButtonDown += new MouseButtonEventHandler((src, e) => {
                e.Handled = true;
            });
            label.MouseLeftButtonUp += new MouseButtonEventHandler((src, e) => {
                if (parentWindow.ctrlKeyDown) {
                    mapEditor.SelectNotesInBPMChange(b);
                } else {
                    parentWindow.songSeekPosition = b.globalBeat / mapEditor.GlobalBPM * 60000;
                    parentWindow.navMouseDown = false;
                }
                e.Handled = true;
            });
            label.MouseDown += new MouseButtonEventHandler((src, e) => {
                if (!(e.ChangedButton == MouseButton.Middle)) {
                    return;
                }
                var res = MessageBox.Show(parentWindow, "Are you sure you want to delete this timing change?", "Confirm Deletion", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes) {
                    mapEditor.RemoveBPMChange(b);
                }
                e.Handled = true;
            });
            return label;
        }
        var offset = borderNavWaveform.ActualHeight * (1 - 60000 * b.globalBeat / (mapEditor.GlobalBPM * parentWindow.songTotalTimeInSeconds.Value * 1000));
        var divLabel = makeBPMChangeLabel($"1/{b.gridDivision} beat");
        Canvas.SetBottom(divLabel, borderNavWaveform.ActualHeight - offset);
        divLabel.MouseRightButtonUp += new MouseButtonEventHandler((src, e) => {
            var txtBox = new TextBox {
                Text = b.gridDivision.ToString(),
                FontSize = Editor.NavBPMChange.LabelSize
            };
            Canvas.SetLeft(txtBox, Editor.NavBPMChange.LabelPadding + Editor.NavBPMChange.LabelSize);
            Canvas.SetBottom(txtBox, borderNavWaveform.ActualHeight - offset);
            txtBox.LostKeyboardFocus += new KeyboardFocusChangedEventHandler((src, e) => {
                if (int.TryParse(txtBox.Text, out int div) && Helper.DoubleRangeCheck(div, 1, Editor.GridDivisionMax)) {
                    mapEditor.RemoveBPMChange(b, false);
                    b.gridDivision = div;
                    mapEditor.AddBPMChange(b);
                }
                canvasNavInputBox.Children.Remove(txtBox);
            });
            txtBox.KeyDown += new KeyEventHandler((src, e) => {
                if (e.Key == Key.Escape || e.Key == Key.Enter) {
                    Keyboard.ClearFocus();
                    Keyboard.Focus(parentWindow);
                }
            });

            canvasNavInputBox.Children.Add(txtBox);
            txtBox.Focus();
            txtBox.SelectAll();

            e.Handled = true;
        });

        var bpmLabel = makeBPMChangeLabel($"{b.BPM} BPM");
        Canvas.SetBottom(bpmLabel, borderNavWaveform.ActualHeight - offset + 11);
        bpmLabel.MouseRightButtonUp += new MouseButtonEventHandler((src, e) => {
            var txtBox = new TextBox {
                Text = b.BPM.ToString(),
                FontSize = Editor.NavBPMChange.LabelSize
            };
            Canvas.SetLeft(txtBox, Editor.NavBPMChange.LabelPadding);
            Canvas.SetBottom(txtBox, borderNavWaveform.ActualHeight - offset + 11);
            txtBox.LostKeyboardFocus += new KeyboardFocusChangedEventHandler((src, e) => {
                if (double.TryParse(txtBox.Text, out double BPM) && BPM > 0) {
                    mapEditor.RemoveBPMChange(b, false);
                    b.BPM = BPM;
                    mapEditor.AddBPMChange(b);
                }
                canvasNavInputBox.Children.Remove(txtBox);
            });
            txtBox.KeyDown += new KeyEventHandler((src, e) => {
                if (e.Key == Key.Escape || e.Key == Key.Enter) {
                    Keyboard.ClearFocus();
                    Keyboard.Focus(parentWindow);
                }
            });

            canvasNavInputBox.Children.Add(txtBox);
            txtBox.Focus();
            txtBox.SelectAll();

            e.Handled = true;
        });

        return [divLabel, bpmLabel];
    }
    private int ColForPosition(double pos) {
        // calculate horizontal element
        var subLength = (pos - editorMarginGrid.Margin.Left) / unitSubLength;
        int col = -1;
        if (0 <= subLength && subLength <= 4.5) {
            col = 0;
        } else if (4.5 <= subLength && subLength <= 8.5) {
            col = 1;
        } else if (8.5 <= subLength && subLength <= 12.5) {
            col = 2;
        } else if (12.5 <= subLength && subLength <= 17.0) {
            col = 3;
        } else if (17.0 < subLength) {
            col = 4;
        }
        return col;
    }
    private double BeatForPosition(double position, bool snap) {
        var pos = EditorGrid.ActualHeight - position - unitHeight / 2;
        double gridLength = unitLength / gridDivision;
        // check if mouse position would correspond to a negative row index
        double snapped = 0;
        double unsnapped = 0;
        if (pos >= 0) {
            unsnapped = pos / unitLength;
            int binarySearch = gridBeatLines.BinarySearch(unsnapped);
            if (binarySearch > 0) {
                return gridBeatLines[binarySearch];
            }
            int indx1 = Math.Min(gridBeatLines.Count - 1, -binarySearch - 1);
            int indx2 = Math.Max(0, indx1 - 1);
            snapped = (gridBeatLines[indx1] - unsnapped) < (unsnapped - gridBeatLines[indx2]) ? gridBeatLines[indx1] : gridBeatLines[indx2];
        }
        return snap ? snapped : unsnapped;
    }
    private void UpdateDragSelection(Point newPoint) {
        Point p1;
        p1.X = Math.Min(newPoint.X, dragSelectStart.X);
        p1.Y = Math.Min(newPoint.Y, dragSelectStart.Y);
        Point p2;
        p2.X = Math.Max(newPoint.X, dragSelectStart.X);
        p2.Y = Math.Max(newPoint.Y, dragSelectStart.Y);
        Vector delta = p2 - p1;
        Canvas.SetLeft(dragSelectBorder, p1.X);
        Canvas.SetTop(dragSelectBorder, p1.Y);
        dragSelectBorder.Width = delta.X;
        dragSelectBorder.Height = delta.Y;
    }
    private double BeatForRow(double row) {
        return row / (double)gridDivision;
    }
}