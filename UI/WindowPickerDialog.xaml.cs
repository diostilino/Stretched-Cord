using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StretchCord.Models;
using StretchCord.Services;

namespace StretchCord.UI
{
    public partial class WindowPickerDialog : Window
    {
        private List<WindowInfo> _allWindows = new();
        public WindowInfo? SelectedWindowInfo { get; private set; }

        public WindowPickerDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshList();
        }

        private void RefreshList()
        {
            _allWindows = WindowEnumerator.GetCapturableWindows();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filter = SearchBox.Text.Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(filter)
                ? _allWindows
                : _allWindows.Where(w =>
                    w.Title.ToLowerInvariant().Contains(filter) ||
                    w.ProcessName.ToLowerInvariant().Contains(filter)).ToList();

            WindowList.ItemsSource = filtered;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshList();

        private void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = WindowList.SelectedItem as WindowInfo;
            BtnSelect.IsEnabled = sel != null;
            SelectionLabel.Text = sel?.ToString() ?? "No window selected";
        }

        private void WindowList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WindowList.SelectedItem is WindowInfo)
                Confirm();
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e) => Confirm();
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Confirm()
        {
            SelectedWindowInfo = WindowList.SelectedItem as WindowInfo;
            if (SelectedWindowInfo != null)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
