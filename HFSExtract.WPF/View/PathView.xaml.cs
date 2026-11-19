using Microsoft.Win32;
using System.Windows;

namespace HFSExtract.WPF.View
{
    /// <summary>
    /// PathView.xaml 的交互逻辑
    /// </summary>
    public partial class PathView : System.Windows.Controls.UserControl
    {
        public PathView()
        {
            InitializeComponent();
        }

        // 1. 定义 TextBlock 绑定的依赖属性 (单向即可)
        public string LabelText
        {
            get => (string)GetValue(LabelTextProperty);
            set => SetValue(LabelTextProperty, value);
        }

        public static readonly DependencyProperty LabelTextProperty =
            DependencyProperty.Register(nameof(LabelText), typeof(string), typeof(PathView));

        // 2. 定义 TextBox 绑定的依赖属性 (支持双向绑定)
        public string PathText
        {
            get => (string)GetValue(PathTextProperty);
            set => SetValue(PathTextProperty, value);
        }

        public static readonly DependencyProperty PathTextProperty =
            DependencyProperty.Register(
                nameof(PathText),
                typeof(string),
                typeof(PathView),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "请选择文件夹",
                InitialDirectory = System.IO.Directory.Exists(PathText) ? PathText : null // 如果已有路径，默认定位到该目录
            };

            if (dialog.ShowDialog() == true)
            {
                // 直接赋值给依赖属性，TextBox 和 MainWindow 都会自动更新
                PathText = dialog.FolderName;
            }
        }
    }
}
