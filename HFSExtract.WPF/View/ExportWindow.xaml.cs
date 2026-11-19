using HFSExtract.WPF.ViewModel;
using System.ComponentModel;
using System.Windows;

namespace HFSExtract.WPF.View
{
    /// <summary>
    /// ExportWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ExportWindow : Window
    {
        public ExportWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is ExportViewModel vm && !vm.TaskIsCompleted)
            {
                var result = MessageBox.Show(this, $"停止当前任务", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question);
                e.Cancel = result == MessageBoxResult.No;
            }

            base.OnClosing(e);
        }
    }
}
