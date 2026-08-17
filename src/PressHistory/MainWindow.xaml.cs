using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PressHistory.ViewModels;

namespace PressHistory;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.RequestHide += (_, _) => Hide();
    }

    public bool AllowClose { get; set; }

    public event EventHandler? CloseToTrayRequested;

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        FocusSearchBox(selectAll: false);
    }

    public async Task<bool> ConfirmAndClearHistoryAsync()
    {
        if (!_viewModel.HasEntries)
        {
            return false;
        }

        var result = MessageBox.Show(
            this,
            "Tout l’historique local sera supprimé. Le contenu actuel du presse-papiers Windows ne sera pas effacé.",
            "Effacer l’historique ?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes && await _viewModel.ClearHistoryAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (AllowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
        CloseToTrayRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_Activated(object sender, EventArgs eventArgs)
    {
        _viewModel.RefreshTimeLabels();
        FocusSearchBox(selectAll: false);
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            FocusSearchBox(selectAll: true);
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Escape)
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.SearchText))
            {
                _viewModel.SearchText = string.Empty;
                FocusSearchBox(selectAll: false);
            }
            else
            {
                Hide();
            }

            eventArgs.Handled = true;
            return;
        }

        if (SearchBox.IsKeyboardFocusWithin &&
            eventArgs.Key is Key.Down or Key.Enter &&
            HistoryList.Items.Count > 0)
        {
            HistoryList.SelectedIndex = 0;
            HistoryList.ScrollIntoView(HistoryList.SelectedItem);

            if (eventArgs.Key == Key.Enter)
            {
                eventArgs.Handled = true;
                await _viewModel.CopySelectedAndHideAsync();
                return;
            }

            HistoryList.UpdateLayout();
            var firstContainer = HistoryList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            firstContainer?.Focus();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.OriginalSource is TextBox or ButtonBase)
        {
            return;
        }

        if (eventArgs.Key == Key.Delete && _viewModel.SelectedEntry is not null)
        {
            _viewModel.DeleteSelected();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Enter && _viewModel.SelectedEntry is not null)
        {
            eventArgs.Handled = true;
            await _viewModel.CopySelectedAndHideAsync();
        }
    }

    private async void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        var source = eventArgs.OriginalSource as DependencyObject;
        if (_viewModel.SelectedEntry is null ||
            FindAncestor<ButtonBase>(source) is not null ||
            FindAncestor<ListBoxItem>(source) is null)
        {
            return;
        }

        eventArgs.Handled = true;
        await _viewModel.CopySelectedAndHideAsync();
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        await ConfirmAndClearHistoryAsync();
    }

    private void FocusSearchBox(bool selectAll)
    {
        SearchBox.Focus();
        if (selectAll)
        {
            SearchBox.SelectAll();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
