using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TriasDeparture.Linux.Tray;

namespace TriasDeparture.Linux.Popup;

/// <summary>Popup next to the tray: closes on Esc or when it loses focus.</summary>
public partial class PopupWindow : Window
{
    private readonly PopupViewModel _viewModel;

    /// <summary>When the popup was last hidden – a tray click right after that is the click that hid it.</summary>
    public DateTime HiddenAtUtc { get; private set; } = DateTime.MinValue;

    // For the XAML previewer
    public PopupWindow() : this(null!) { }

    public PopupWindow(PopupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;

        Deactivated += (_, _) => HidePopup();
        Closing += (_, e) =>
        {
            // Closing from the window manager (e.g. Alt+F4) only hides – the tray keeps the window
            if (e.CloseReason != WindowCloseReason.ApplicationShutdown)
            {
                e.Cancel = true;
                HidePopup();
            }
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        SearchBox.AddHandler(KeyDownEvent, SearchBox_PreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public void ShowPopup()
    {
        var screen = PopupPlacement.ChooseScreen(Screens.All, s => s.Bounds, s => s.WorkingArea, s => s.IsPrimary);
        if (screen is not null)
        {
            var scaling = screen.Scaling;
            var size    = new PixelSize((int)(Width * scaling), (int)(Height * scaling));
            Position    = PopupPlacement.Place(screen.Bounds, screen.WorkingArea, size,
                IconRenderer.IsGnome(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")), scaling);
        }

        _viewModel.OnShown();
        Show();
        Activate();
        if (_viewModel.ShowStop) Focus();
        else SearchBox.Focus();
    }

    public void HidePopup()
    {
        if (!IsVisible) return;
        HiddenAtUtc = DateTime.UtcNow;
        _viewModel.OnHidden();
        Hide();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!_viewModel.StepBack())
                HidePopup();
            else if (!_viewModel.ShowStop)
                SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            if (_viewModel.ShowStop) _viewModel.BackCommand.Execute(null);
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>Typing anywhere goes into the search box.</summary>
    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (SearchBox.IsFocused || _viewModel.ShowStop || string.IsNullOrEmpty(e.Text))
            return;
        SearchBox.Focus();
        SearchBox.Text = (SearchBox.Text ?? "") + e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    /// <summary>Arrow down from the search box jumps into the result list (Enter then opens the stop).</summary>
    private void SearchBox_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down) return;
        if (ResultList.GetVisualDescendants().OfType<Button>().FirstOrDefault() is { } first)
        {
            first.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }
}
