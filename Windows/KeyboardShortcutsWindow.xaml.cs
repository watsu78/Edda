using System.Windows;

namespace Edda {
    public partial class KeyboardShortcutsWindow : Window {
        public KeyboardShortcutsWindow() {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}