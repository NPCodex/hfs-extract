using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HFSExtract.WPF.Model;
using HFSExtract.WPF.View;
using System.IO;
using System.Windows;

namespace HFSExtract.WPF.ViewModel;

public partial class MainViewModel : ObservableObject
{
    public HFSExtractor Extractor { get; } = new();
    public string InputPath
    {
        get => Extractor.InputPath;
        set
        {
            if (SetProperty(Extractor.InputPath, value, Extractor, (m, v) => m.InputPath = v))
                ExportCommand.NotifyCanExecuteChanged();
        }
    }
    public string OutputPath
    {
        get => Extractor.OutputPath;
        set
        {
            if (SetProperty(Extractor.OutputPath, value, Extractor, (m, v) => m.OutputPath = v))
                ExportCommand.NotifyCanExecuteChanged();
        }
    }
    public bool Overwrite
    {
        get => Extractor.Overwrite;
        set => SetProperty(Extractor.Overwrite, value, Extractor, (m, v) => m.Overwrite = v);
    }
    public bool SkipHistory
    {
        get => Extractor.SkipHistory;
        set => SetProperty(Extractor.SkipHistory, value, Extractor, (m, v) => m.SkipHistory = v);
    }
    public int ThreadCount
    {
        get => Extractor.ThreadCount;
        set => SetProperty(Extractor.ThreadCount, value, Extractor, (m, v) => m.ThreadCount = v);
    }
    public int[] ThreadOptions { get; } = [0, 1, 2, 4, 8, 16, 32];
    private static readonly string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    private record Config(string InputPath, string OutputPath, bool Overwrite, bool SkipHistory);


    public MainViewModel()
    {
        LoadConfig();
        LoadHistory();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export(object? obj)
    {
        try
        {
            var mainWnd = (obj as MainWindow)!;
            ExportViewModel vm = new(Extractor);
            ExportWindow exportWnd = new()
            {
                Owner = mainWnd,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = vm,
            };
            exportWnd.Closing += vm.OnWindowClosing;
            exportWnd.Closed += (sender, arg) =>
            {
                var exportWnd = (sender as Window)!;
                var mainWnd = exportWnd.Owner;
                mainWnd.Show();
                mainWnd.Left = exportWnd.Left;
                mainWnd.Top = exportWnd.Top;
            };
            exportWnd.Show();
            mainWnd.Hide();
            SaveConfig();
            var task = vm.Extract();
        }
        catch (Exception ex)
        {
            MessageBox.Show(obj as Window, ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanExport()
    {
        return Directory.Exists(InputPath) && Utils.IsValidWindowsPath(OutputPath);
    }

    private void LoadConfig()
    {
        if (Utils.LoadJson<Config>(configPath) is not Config config) return;
        InputPath = config.InputPath;
        OutputPath = config.OutputPath;
        Overwrite = config.Overwrite;
        SkipHistory = config.SkipHistory;
    }

    private void SaveConfig() => Utils.SaveJson(configPath, new Config(InputPath, OutputPath, Overwrite, SkipHistory));

    private void LoadHistory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "History.txt");
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            Extractor.History.Add(line, default);
        }
    }

}
