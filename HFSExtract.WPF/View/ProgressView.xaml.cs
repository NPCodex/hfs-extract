using System.Windows;
using System.Windows.Controls;

namespace HFSExtract.WPF.View
{
    /// <summary>
    /// ProgressView.xaml 的交互逻辑
    /// </summary>
    public partial class ProgressView : UserControl
    {
        public ProgressView()
        {
            InitializeComponent();
        }

        public string ElapsedTime
        {
            get => (string)GetValue(ElapsedTimeProperty);
            set => SetValue(ElapsedTimeProperty, value);
        }

        public static readonly DependencyProperty ElapsedTimeProperty =
            DependencyProperty.Register(nameof(ElapsedTime), typeof(string), typeof(ProgressView), new PropertyMetadata("00:00:00"));

        public string RemainingTime
        {
            get => (string)GetValue(RemainingTimeProperty);
            set => SetValue(RemainingTimeProperty, value);
        }

        public static readonly DependencyProperty RemainingTimeProperty =
            DependencyProperty.Register(nameof(RemainingTime), typeof(string), typeof(ProgressView), new PropertyMetadata("00:00:00"));

        public string ProgressText
        {
            get => (string)GetValue(ProgressTextProperty);
            set => SetValue(ProgressTextProperty, value);
        }

        public static readonly DependencyProperty ProgressTextProperty =
            DependencyProperty.Register(nameof(ProgressText), typeof(string), typeof(ProgressView), new PropertyMetadata("0/0 (0%)"));

        public double ProgressValue
        {
            get => (double)GetValue(ProgressValueProperty);
            set => SetValue(ProgressValueProperty, value);
        }

        public static readonly DependencyProperty ProgressValueProperty =
            DependencyProperty.Register(nameof(ProgressValue), typeof(double), typeof(ProgressView), new PropertyMetadata(50.0));
    }
}
