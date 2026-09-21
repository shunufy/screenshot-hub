using System.Windows;

namespace ScreenshotHub;

public partial class TagEditorWindow : Window
{
    public TagEditorWindow(IEnumerable<string> tags)
    {
        InitializeComponent();
        TagTextBox.Text = string.Join(", ", tags);
        Loaded += (_, _) =>
        {
            TagTextBox.Focus();
            TagTextBox.SelectAll();
        };
    }

    public IReadOnlyList<string> Tags { get; private set; } = [];

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Tags = TagTextBox.Text
            .Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        DialogResult = true;
    }
}
