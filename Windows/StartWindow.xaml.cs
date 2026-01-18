using Edda.Const;
using NAudio.Gui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Edda {
    /// <summary>
    /// Interaction logic for StartWindow.xaml
    /// </summary>
    public partial class StartWindow : Window {
        RecentOpenedFolders RecentMaps = ((RagnarockEditor.App)Application.Current).RecentMaps;

        // these definitions are to apply Windows 11-style rounded corners
        // https://docs.microsoft.com/en-us/windows/apps/desktop/modernize/apply-rounded-corners
        public enum DWMWINDOWATTRIBUTE {
            DWMWA_WINDOW_CORNER_PREFERENCE = 33
        }
        public enum DWM_WINDOW_CORNER_PREFERENCE {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        internal static extern void DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute, uint cbAttribute);

        public StartWindow() {
            InitializeComponent();
            TxtVersionNumber.Text = $"version {Program.DisplayVersionString}";
            PopulateRecentlyOpenedMaps();

            // apply rounded corners
            try {
                IntPtr hWnd = new WindowInteropHelper(GetWindow(this)).EnsureHandle();
                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(hWnd, attribute, ref preference, sizeof(uint));
            } catch {
                Console.WriteLine("INFO: Could not set window corner preferences.");
            }
        }

        private void CreateRecentMapItem(string name, string path) {
            // Build left-side content (name + path)
            StackPanel leftContent = new();
            leftContent.Height = 30;
            leftContent.Margin = new Thickness(5);
            leftContent.Orientation = Orientation.Horizontal;

            // Removed left-side blank map icon

            StackPanel textPanel = new();
            textPanel.Margin = new(7, 0, 0, 0);
            textPanel.VerticalAlignment = VerticalAlignment.Center;

            TextBlock tbName = new();
            tbName.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#002668");
            tbName.FontSize = 14;
            tbName.FontWeight = FontWeights.Bold;
            tbName.FontFamily = new("Bahnschrift");
            tbName.Text = name;
            if (string.IsNullOrWhiteSpace(name)) {
                tbName.FontStyle = FontStyles.Italic;
                tbName.Text = "Untitled Map";
            }

            TextBlock tbPath = new();
            tbPath.FontSize = 11;
            tbPath.FontFamily = new("Bahnschrift SemiLight");
            tbPath.Text = path;

            textPanel.Children.Add(tbName);
            textPanel.Children.Add(tbPath);

            leftContent.Children.Add(textPanel);

            // Build delete button with a simple trash icon using vector shapes
            Button deleteBtn = new();
            deleteBtn.Width = 24;
            deleteBtn.Height = 24;
            deleteBtn.Margin = new Thickness(5, 3, 5, 3);
            deleteBtn.HorizontalAlignment = HorizontalAlignment.Right;
            deleteBtn.VerticalAlignment = VerticalAlignment.Center;
            deleteBtn.BorderBrush = null;
            deleteBtn.Background = Brushes.Transparent;
            deleteBtn.Focusable = false;
            deleteBtn.ToolTip = "Remove from recent";

            // Compose trash icon using SVG path data (Font Awesome Trash)
            string svgPathData = "M136.7 5.9C141.1-7.2 153.3-16 167.1-16l113.9 0c13.8 0 26 8.8 30.4 21.9L320 32 416 32c17.7 0 32 14.3 32 32s-14.3 32-32 32L32 96C14.3 96 0 81.7 0 64S14.3 32 32 32l96 0 8.7-26.1zM32 144l384 0 0 304c0 35.3-28.7 64-64 64L96 512c-35.3 0-64-28.7-64-64l0-304zm88 64c-13.3 0-24 10.7-24 24l0 192c0 13.3 10.7 24 24 24s24-10.7 24-24l0-192c0-13.3-10.7-24-24-24zm104 0c-13.3 0-24 10.7-24 24l0 192c0 13.3 10.7 24 24 24s24-10.7 24-24l0-192c0-13.3-10.7-24-24-24zm104 0c-13.3 0-24 10.7-24 24l0 192c0 13.3 10.7 24 24 24s24-10.7 24-24l0-192c0-13.3-10.7-24-24-24z";
            var trashIcon = new System.Windows.Shapes.Path {
                Fill = Brushes.DimGray,
                Stretch = Stretch.Uniform,
                Data = Geometry.Parse(svgPathData)
            };

            Viewbox vb = new();
            vb.Stretch = Stretch.Uniform;
            vb.Child = trashIcon;
            deleteBtn.Content = vb;

            // Prevent opening map when clicking delete
            deleteBtn.Click += (s, e) => {
                e.Handled = true;
                var res = MessageBox.Show(this, "Are you sure you want to remove this map from the list of recently opened maps?", "Confirm Removal", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes) {
                    RecentMaps.RemoveRecentlyOpened(path);
                    RecentMaps.Write();
                    PopulateRecentlyOpenedMaps();
                }
            };

            // Right-side icons panel: only delete button aligned to right
            StackPanel rightIcons = new();
            rightIcons.Orientation = Orientation.Horizontal;
            rightIcons.VerticalAlignment = VerticalAlignment.Center;
            rightIcons.Margin = new Thickness(5, 3, 5, 3);
            rightIcons.Children.Add(deleteBtn);

            // Compose item container with two columns: left content + right icons
            Grid container = new();
            container.HorizontalAlignment = HorizontalAlignment.Stretch;
            container.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            container.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            Grid.SetColumn(leftContent, 0);
            Grid.SetColumn(rightIcons, 1);
            container.Children.Add(leftContent);
            container.Children.Add(rightIcons);

            ListViewItem item = new();
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            item.Content = container;

            // Open map on item left click (excluding delete button)
            item.MouseLeftButtonUp += new MouseButtonEventHandler((sender, e) => {
                // If the click originated from the delete button, ignore
                if (e.OriginalSource is DependencyObject d && IsDescendantOf(d, deleteBtn)) {
                    return;
                }
                item.IsSelected = false;
                OpenMap(path);
            });

            // Right-click still offers removal
            item.MouseRightButtonUp += new MouseButtonEventHandler((sender, e) => {
                var res = MessageBox.Show(this, "Are you sure you want to remove this map from the list of recently opened maps?", "Confirm Removal", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes) {
                    RecentMaps.RemoveRecentlyOpened(path);
                    RecentMaps.Write();
                    PopulateRecentlyOpenedMaps();
                }
            });
            ListViewRecentMaps.Items.Add(item);
        }

        private bool IsDescendantOf(DependencyObject source, DependencyObject ancestor) {
            while (source != null) {
                if (source == ancestor) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void PopulateRecentlyOpenedMaps() {
            ListViewRecentMaps.Items.Clear();
            foreach (var recentMap in RecentMaps.GetRecentlyOpened()) {
                CreateRecentMapItem(recentMap.Item1, recentMap.Item2);
            }
        }

        private void ButtonExit_Click(object sender, RoutedEventArgs e) {
            Environment.Exit(0);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            this.DragMove();
        }

        private void ButtonNewMap_Click(object sender, RoutedEventArgs e) {
            string newMapFolder = Helper.ChooseNewMapFolder();
            if (newMapFolder != null) {
                MainWindow main = new();
                this.Close();
                // NOTE: the window must be shown first before any processing can be done
                main.Show();
                main.InitNewMap(newMapFolder);
            }
        }

        private void ButtonImportMap_Click(object sender, RoutedEventArgs e) {
            string importMapFolder = Helper.ChooseNewMapFolder();
            if (importMapFolder == null) {
                return;
            }
            MainWindow main = new();
            this.Close();
            // NOTE: the window must be shown first before any processing can be done
            main.Show();
            try {
                main.InitImportMap(importMapFolder);
            } catch (Exception ex) {
                MessageBox.Show(this, $"An error occured while importing the simfile:\n{ex.Message}.\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonOpenMap_Click(object sender, RoutedEventArgs e) {
            string mapFolder = Helper.ChooseOpenMapFolder();
            OpenMap(mapFolder);
        }

        private void OpenMap(string folder, string mapName = null) {
            if (folder == null) {
                return;
            }
            MainWindow main = new();
            this.Close();
            // NOTE: the window must be shown first before any processing can be done
            main.Show();
            try {
                main.InitOpenMap(folder);
            } catch (Exception ex) {
                MessageBox.Show(this, $"An error occured while opening the map:\n{ex.Message}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                RecentMaps.RemoveRecentlyOpened(folder);
                RecentMaps.Write();

                new StartWindow().Show();
                main.Close();

            }
        }
    }
}