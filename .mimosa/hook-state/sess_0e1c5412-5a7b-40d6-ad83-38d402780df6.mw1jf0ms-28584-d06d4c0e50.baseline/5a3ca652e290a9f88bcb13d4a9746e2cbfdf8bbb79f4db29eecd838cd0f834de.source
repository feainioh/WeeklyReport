using System.Windows;

namespace WeeklyReport.App.Views;

public partial class AiReviewWindow : Window
{
    public AiReviewWindow(string title, string description, string text, string confirmLabel)
    {
        InitializeComponent();
        Title = title;
        DescriptionText.Text = description;
        ReviewText.Text = text;
        ConfirmButton.Content = confirmLabel;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
