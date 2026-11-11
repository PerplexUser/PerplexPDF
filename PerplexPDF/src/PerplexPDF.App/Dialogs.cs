using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.App;

internal sealed class Dialogs(Window owner)
{
    internal string? Input(string title, string description, string initial = "", bool multiline = false)
    {
        var box = new TextBox
        {
            Text = initial, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 96 : 30, MaxHeight = 180, MaxLength = multiline ? 4000 : 16000
        };
        if (multiline)
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var window = Create(title, description, box, "Continue");
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog() == true ? box.Text : null;
    }

    internal string? Password(string fileName)
    {
        var box = new PasswordBox { MinHeight = 30, MaxLength = 256 };
        var window = Create(Strings.Get("PasswordTitle"), Strings.Format("PasswordPrompt", fileName), box, "Open");
        window.Loaded += (_, _) => box.Focus();
        return window.ShowDialog() == true ? box.Password : null;
    }

    internal OverlayObject? AddText(OverlayObject overlay)
    {
        var input = new OverlayInspector(overlay);
        var editor = new ContentPresenter
        {
            Content = input,
            ContentTemplate = (DataTemplate)owner.FindResource("OverlayTextEditor")
        };
        var error = new TextBlock
        {
            Text = Strings.Get("InvalidNewText"), TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkRed, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0)
        };
        var fields = new StackPanel();
        fields.Children.Add(editor);
        fields.Children.Add(error);
        OverlayObject? result = null;
        var dialog = Create(Strings.Get(overlay.Kind == OverlayKind.Text ? "AddText" : "Note"),
            Strings.Get("OverlayTextPrompt"), fields, "Add", () =>
            {
                if (!string.IsNullOrWhiteSpace(input.Text) && input.ReadAppearance() is { } styled)
                {
                    result = styled;
                    return true;
                }
                error.Visibility = Visibility.Visible;
                return false;
            });
        dialog.Loaded += (_, _) =>
        {
            editor.ApplyTemplate();
            if (editor.ContentTemplate.FindName("OverlayTextEntry", editor) is TextBox text)
                text.Focus();
        };
        return dialog.ShowDialog() == true ? result : null;
    }

    internal (string Range, string Language, int Dpi)? Ocr(int pages)
    {
        var fields = new StackPanel();
        var range = new TextBox { Text = pages == 1 ? "1" : $"1-{pages}", MaxLength = 16000 };
        AddField(fields, "PageRange", range);
        var language = new ComboBox
        {
            ItemsSource = new[] { Strings.Get("English"), Strings.Get("German"), Strings.Get("BothLanguages") },
            SelectedIndex = 0
        };
        AddField(fields, "Language", language);
        var dpi = new ComboBox { ItemsSource = new[] { 200, 300, 400 }, SelectedIndex = 1 };
        AddField(fields, "Dpi", dpi);
        var dialog = Create(Strings.Get("OcrTitle"), Strings.Get("OcrHint"), fields, "CreateCopy");
        if (dialog.ShowDialog() != true)
            return null;
        return (range.Text, new[] { "eng", "deu", "eng+deu" }[language.SelectedIndex], (int)dpi.SelectedItem);
    }

    internal (bool PageImages, WordExportOptions? Word)? ExportOptions(string title, bool word)
    {
        var fields = new StackPanel();
        var mode = new ComboBox
        {
            ItemsSource = new[] { Strings.Get("EditableText"), Strings.Get(word ? "WordOriginalLayout" : "PageImages") },
            SelectedIndex = 0
        };
        AddField(fields, "ConversionMode", mode);
        CheckBox[]? settings = null;
        if (word)
        {
            var editable = new StackPanel { Margin = new Thickness(0, 8, 0, 0), IsEnabled = mode.SelectedIndex == 0 };
            settings = new[] { "WordImages", "WordTables", "WordPageBreaks", "WordReflow", "WordLists", "WordFooters" }
                .Select(key => new CheckBox
                {
                    Content = Strings.Get(key), IsChecked = true, Margin = new Thickness(0, 5, 0, 5)
                }).ToArray();
            foreach (var setting in settings)
                editable.Children.Add(setting);
            fields.Children.Add(editable);
            mode.SelectionChanged += (_, _) => editable.IsEnabled = mode.SelectedIndex == 0;
        }
        var dialog = Create(title, Strings.Get(word ? "WordConversionHint" : "ConversionHint"), fields, "Continue");
        if (dialog.ShowDialog() != true)
            return null;
        var pageImages = mode.SelectedIndex == 1;
        WordExportOptions? options = settings is not null && !pageImages
            ? new(settings[0].IsChecked == true, settings[1].IsChecked == true, settings[2].IsChecked == true,
                settings[3].IsChecked == true, settings[4].IsChecked == true, settings[5].IsChecked == true)
            : null;
        return (pageImages, options);
    }

    internal IReadOnlyList<FileOrderItem>? Order(string title, string hint, string[] paths, bool images)
    {
        var items = new ObservableCollection<FileOrderItem>(paths.Select(path => new FileOrderItem(path)));
        var list = new ListBox { ItemsSource = items, DisplayMemberPath = nameof(FileOrderItem.Display), Height = 165, SelectedIndex = 0 };
        var fields = new StackPanel();
        fields.Children.Add(list);
        var row = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        Add("MoveUp", () => Move(-1));
        Add("MoveDown", () => Move(1));
        if (images)
        {
            Add("RotateRight", () =>
            {
                if (list.SelectedItem is FileOrderItem item)
                    item.Rotation = (item.Rotation + 90) % 360;
            });
            Add("ToggleOrientation", () =>
            {
                if (list.SelectedItem is FileOrderItem item)
                    item.Landscape = !item.Landscape;
            });
        }
        fields.Children.Add(row);
        return Create(title, hint, fields, "Continue").ShowDialog() == true ? items.ToArray() : null;

        void Add(string key, Action action)
        {
            var button = Button(key, "Action");
            button.Click += (_, _) => action();
            row.Children.Add(button);
        }
        void Move(int delta)
        {
            var selected = list.SelectedIndex;
            var target = selected + delta;
            if (selected >= 0 && target >= 0 && target < items.Count)
            {
                items.Move(selected, target);
                list.SelectedIndex = target;
                list.ScrollIntoView(list.SelectedItem);
            }
        }
    }

    internal void About(Action license, Action notices)
    {
        var panel = new StackPanel();
        panel.Children.Add(new CopyrightLink(Strings.Get("AboutCopyright")) { Margin = new Thickness(0, 4, 0, 14) });
        var links = new WrapPanel();
        panel.Children.Add(links);
        var window = Create(Strings.Get("About"), Strings.Get("AboutDescription"), panel, "Close");
        foreach (var (label, open) in new[] { ("MitLicense", license), ("Notices", notices) })
        {
            var button = Button(label, "Action");
            button.Click += (_, _) => { window.Close(); open(); };
            links.Children.Add(button);
        }
        window.ShowDialog();
    }

    internal void Text(string title, string body)
    {
        var text = new TextBox
        {
            Text = body, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 430,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White,
            BorderThickness = new Thickness(0), IsTabStop = true
        };
        Create(title, "", text, "Close").ShowDialog();
    }

    private Window Create(string title, string description, UIElement content, string accept, Func<bool>? validate = null)
    {
        var window = new Window
        {
            Owner = owner, Title = title, Width = 545, SizeToContent = SizeToContent.Height, MaxHeight = 680,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, FontFamily = owner.FontFamily, FontSize = 13
        };
        var panel = new StackPanel { Margin = new Thickness(21) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        if (description.Length != 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 13),
                Foreground = (Brush)owner.FindResource("MutedBrush")
            });
        }
        panel.Children.Add(content);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var okay = Button(accept, "Primary");
        okay.IsDefault = true;
        okay.Click += (_, _) =>
        {
            if (validate?.Invoke() != false)
                window.DialogResult = true;
        };
        if (accept != "Close")
        {
            var cancel = Button("Cancel", "Action");
            cancel.IsCancel = true;
            row.Children.Add(cancel);
        }
        row.Children.Add(okay);
        panel.Children.Add(row);
        window.Content = panel;
        return window;
    }

    private Button Button(string key, string style) => new() { Style = (Style)owner.FindResource(style), Content = Strings.Get(key) };

    private static void AddField(Panel panel, string key, Control control)
    {
        panel.Children.Add(new Label { Content = Strings.Get(key), Target = control, Padding = new Thickness(0, 6, 0, 3) });
        panel.Children.Add(control);
    }
}

internal sealed class FileOrderItem(string path) : NotifyModel
{
    private int _rotation;
    private bool _landscape;
    public string Path { get; } = path;
    public string Display => System.IO.Path.GetExtension(Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
        ? System.IO.Path.GetFileName(Path)
        : Strings.Format("ImageOrder", System.IO.Path.GetFileName(Path), Rotation, Landscape ? Strings.Get("Landscape") : Strings.Get("Portrait"));
    public int Rotation { get => _rotation; set { _rotation = value; Notify(nameof(Display)); } }
    public bool Landscape { get => _landscape; set { _landscape = value; Notify(nameof(Display)); } }
}
