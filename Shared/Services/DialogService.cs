using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace GestionCommerciale.Shared.Services;

public sealed class DialogService : IDialogService
{
    private readonly ILocaleService _locale;

    public DialogService(ILocaleService locale) => _locale = locale;

    private static Window? GetMainWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow
            : null;

    private void ApplyLocaleLayout(Window window, Panel content)
    {
        var flow = _locale.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.FlowDirection = flow;
        content.FlowDirection = flow;
    }

    public async Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default, int autoCloseMs = 0)
    {
        var owner = GetMainWindow();
        var w = new Window
        {
            Title = title,
            MinWidth = 280,
            MaxWidth = 480,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440
        });

        var ok = new Button { Content = "OK", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => w.Close();
        panel.Children.Add(ok);
        ApplyLocaleLayout(w, panel);
        w.Content = panel;

        if (autoCloseMs > 0)
            _ = Task.Delay(autoCloseMs, cancellationToken).ContinueWith(_ => w.Close(), TaskScheduler.FromCurrentSynchronizationContext());

        if (owner != null)
            await w.ShowDialog(owner);
        else
            w.Show();
    }

    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
        ShowInfoAsync(title, message, cancellationToken);

    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        var w = new Window
        {
            Title = title,
            MinWidth = 280,
            MaxWidth = 480,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var confirmed = false;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var no = new Button { Content = _locale.T("Btn_No") };
        no.Click += (_, _) =>
        {
            confirmed = false;
            w.Close();
        };
        var yes = new Button { Content = _locale.T("Btn_Yes"), IsDefault = true };
        yes.Click += (_, _) =>
        {
            confirmed = true;
            w.Close();
        };
        buttons.Children.Add(no);
        buttons.Children.Add(yes);
        panel.Children.Add(buttons);
        ApplyLocaleLayout(w, panel);
        w.Content = panel;

        if (owner != null)
            await w.ShowDialog(owner);
        else
            w.Show();

        return confirmed;
    }

    public async Task<string?> PromptPasswordAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        var w = new Window
        {
            Title = title,
            MinWidth = 300,
            MaxWidth = 460,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? password = null;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 420
        });

        var input = new TextBox
        {
            PasswordChar = '*',
            MinWidth = 260
        };
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Annuler" };
        cancel.Click += (_, _) =>
        {
            password = null;
            w.Close();
        };
        var ok = new Button { Content = "Valider", IsDefault = true };
        ok.Click += (_, _) =>
        {
            password = input.Text;
            w.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        ApplyLocaleLayout(w, panel);
        w.Content = panel;

        if (owner != null)
            await w.ShowDialog(owner);
        else
            w.Show();

        return password;
    }

    public async Task<string?> PromptLicenseAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        var w = new Window
        {
            Title = title,
            MinWidth = 320,
            MaxWidth = 480,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? licenseKey = null;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 440
        });

        var input = new TextBox
        {
            MinWidth = 280,
            Watermark = "Clé de licence"
        };
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Quitter" };
        cancel.Click += (_, _) =>
        {
            licenseKey = null;
            w.Close();
        };
        var ok = new Button { Content = "Activer", IsDefault = true };
        ok.Click += (_, _) =>
        {
            licenseKey = input.Text;
            w.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        w.Content = panel;

        if (owner != null)
            await w.ShowDialog(owner);
        else
            w.Show();

        return licenseKey;
    }

    public async Task<string?> ShowPromptAsync(string title, string message, CancellationToken cancellationToken = default, string? initialValue = null)
    {
        var owner = GetMainWindow();
        var w = new Window
        {
            Title = title,
            MinWidth = 300,
            MaxWidth = 460,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        string? result = null;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 420
        });

        var input = new TextBox
        {
            MinWidth = 260,
            Text = initialValue ?? string.Empty
        };
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Annuler" };
        cancel.Click += (_, _) =>
        {
            result = null;
            w.Close();
        };
        var ok = new Button { Content = "Valider", IsDefault = true };
        ok.Click += (_, _) =>
        {
            result = input.Text;
            w.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        w.Content = panel;

        if (owner != null)
            await w.ShowDialog(owner);
        else
            w.Show();

        return result;
    }

    public async Task<List<int>?> ShowBlPickerAsync(string title, IReadOnlyList<(int Id, string Numero, DateTime Date, string MontantLabel)> availableBls, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        var w = new Window
        {
            Title = title,
            MinWidth = 400,
            MaxWidth = 600,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        List<int>? result = null;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };

        var checkboxes = new List<CheckBox>();
        var listPanel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        foreach (var bl in availableBls)
        {
            var cb = new CheckBox
            {
                Content = $"{bl.Numero}  —  {bl.Date:d}  —  {bl.MontantLabel}",
                Tag = bl.Id
            };
            checkboxes.Add(cb);
            listPanel.Children.Add(cb);
        }

        var listHost = availableBls.Count > 8
            ? (Control)new ScrollViewer { Content = listPanel, MaxHeight = 320 }
            : listPanel;
        panel.Children.Add(listHost);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var btnCancel = new Button { Content = "Annuler" };
        btnCancel.Click += (_, _) => w.Close();
        var btnAdd = new Button { Content = "Ajouter", IsDefault = true };
        btnAdd.Click += (_, _) =>
        {
            result = checkboxes.Where(cb => cb.IsChecked == true).Select(cb => (int)cb.Tag!).ToList();
            w.Close();
        };
        actions.Children.Add(btnCancel);
        actions.Children.Add(btnAdd);
        panel.Children.Add(actions);

        w.Content = panel;

        if (owner != null)
            await w.ShowDialog(owner);
        else
            w.Show();

        return result;
    }

    public Task<List<int>?> ShowBrPickerAsync(string title, IReadOnlyList<(int Id, string Numero, DateTime Date, string MontantLabel)> availableBrs, CancellationToken cancellationToken = default) =>
        ShowBlPickerAsync(title, availableBrs, cancellationToken);

    public async Task<(DateTime from, DateTime to)?> PickDateRangeAsync(string title, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        var w = new Window
        {
            Title = title,
            MinWidth = 340,
            MaxWidth = 480,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        (DateTime from, DateTime to)? result = null;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

        var dpFrom = new DatePicker();
        var dpTo = new DatePicker();

        void SetRange(DateTime from, DateTime to)
        {
            dpFrom.SelectedDate = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, TimeSpan.Zero);
            dpTo.SelectedDate = new DateTimeOffset(to.Year, to.Month, to.Day, 0, 0, 0, TimeSpan.Zero);
        }

        var btnToday = new Button { Content = "Aujourd'hui" };
        btnToday.Click += (_, _) => SetRange(DateTime.Today, DateTime.Today);

        var btnThisWeek = new Button { Content = "Cette semaine" };
        btnThisWeek.Click += (_, _) =>
        {
            var today = DateTime.Today;
            var diff = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var monday = today.AddDays(-diff);
            SetRange(monday, monday.AddDays(6));
        };

        var btnThisMonth = new Button { Content = "Ce mois" };
        btnThisMonth.Click += (_, _) =>
        {
            var today = DateTime.Today;
            var first = new DateTime(today.Year, today.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            SetRange(first, last);
        };

        presets.Children.Add(btnToday);
        presets.Children.Add(btnThisWeek);
        presets.Children.Add(btnThisMonth);
        panel.Children.Add(presets);

        var dateGrid = new StackPanel { Spacing = 8 };
        var fromRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fromRow.Children.Add(new TextBlock { Text = "Du:", VerticalAlignment = VerticalAlignment.Center, MinWidth = 30 });
        dpFrom.MinWidth = 180;
        fromRow.Children.Add(dpFrom);
        dateGrid.Children.Add(fromRow);

        var toRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toRow.Children.Add(new TextBlock { Text = "Au:", VerticalAlignment = VerticalAlignment.Center, MinWidth = 30 });
        dpTo.MinWidth = 180;
        toRow.Children.Add(dpTo);
        dateGrid.Children.Add(toRow);
        panel.Children.Add(dateGrid);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var btnClear = new Button { Content = "Effacer" };
        btnClear.Click += (_, _) =>
        {
            result = (DateTime.MinValue, DateTime.MinValue);
            w.Close();
        };
        var btnCancel = new Button { Content = "Annuler" };
        btnCancel.Click += (_, _) => w.Close();
        var btnApply = new Button { Content = "Appliquer", IsDefault = true };
        btnApply.Click += (_, _) =>
        {
            if (!dpFrom.SelectedDate.HasValue && !dpTo.SelectedDate.HasValue)
                result = (DateTime.MinValue, DateTime.MinValue);
            else if (dpFrom.SelectedDate.HasValue && dpTo.SelectedDate.HasValue)
                result = (dpFrom.SelectedDate.Value.DateTime.Date, dpTo.SelectedDate.Value.DateTime.Date);
            w.Close();
        };
        actions.Children.Add(btnClear);
        actions.Children.Add(btnCancel);
        actions.Children.Add(btnApply);
        panel.Children.Add(actions);

        w.Content = panel;

        if (owner != null)
            await w.ShowDialog(owner);
        else
            w.Show();

        return result;
    }

    public async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string> patterns, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner?.StorageProvider is not { } sp) return null;

        var filters = new List<FilePickerFileType>
        {
            new(title) { Patterns = patterns.ToList() }
        };

        var result = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner?.StorageProvider is not { } sp) return null;

        var result = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<string> patterns, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner?.StorageProvider is not { } sp) return null;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new(title) { Patterns = patterns.ToList() }
            }
        });

        return file?.TryGetLocalPath();
    }

    public async Task<bool> SavePickedFileBytesAsync(string title, string suggestedFileName, IReadOnlyList<string> patterns, byte[] content, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner?.StorageProvider is not { } sp) return false;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new(title) { Patterns = patterns.ToList() }
            }
        });

        if (file == null) return false;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return true;
    }
}
