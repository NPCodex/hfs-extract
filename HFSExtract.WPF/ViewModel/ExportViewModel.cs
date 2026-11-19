using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HFSExtract.WPF.Model;
using HFSExtract.WPF.View;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace HFSExtract.WPF.ViewModel;

public partial class ExportViewModel(HFSExtractor extractor) : ObservableObject
{
    public HFSExtractor Extractor { get; } = extractor;
    [ObservableProperty]
    public partial ObservableCollection<HFSMetadataViewModel> Datas { get; private set; } = [];
    [ObservableProperty]
    public partial string ElapsedTime { get; private set; } = "00:00:00";
    [ObservableProperty]
    public partial string RemainingTime { get; private set; } = "99:99:99";
    [ObservableProperty]
    public partial string ProgressText { get; private set; } = "99/99";
    [ObservableProperty]
    public partial double ProgressValue { get; private set; } = 0;
    public bool TaskIsCompleted => extractTask is not null && extractTask.IsCompleted;
    private Task? extractTask;
    private readonly CancellationTokenSource extractCTS = new();
    private readonly CancellationTokenSource elapsedTimeCTS = new();
    private readonly DateTime startTime = DateTime.Now;


    public async Task Extract()
    {
        var updateTask = SetElapsedTimeAsync(elapsedTimeCTS.Token);
        try
        {
            extractTask = ExtractAsync(extractCTS.Token);
            await extractTask;
        }
        finally
        {
            elapsedTimeCTS.Cancel();
        }

        try
        {
            await updateTask;
        }
        catch (OperationCanceledException) { }

        SaveErrorsCommand.NotifyCanExecuteChanged();
        OpenOutputCommand.NotifyCanExecuteChanged();
        SaveHistoryCommand.NotifyCanExecuteChanged();
    }

    private async Task ExtractAsync(CancellationToken token)
    {
        await Task.Run(Extractor.FindArchives, token);
        CreateDatas();

        var current = 0;
        await Extractor.ExportAllAsync(hfs => Application.Current.Dispatcher.Invoke(() =>
        {
            var index = Extractor.Datas.IndexOf(hfs);
            Datas[index].Update();
            current++;
            ChangeProgress(current);
            SetRemainingTime(current);
        }), token);
    }

    private void CreateDatas()
    {
        var x = 0;
        Datas = new(Extractor.Datas.Select(d => new HFSMetadataViewModel(d) { Id = x++ }));
    }

    public void OnWindowClosing(object? sender, EventArgs arg)
    {
        if (!extractCTS.IsCancellationRequested) extractCTS.Cancel();
        if (!elapsedTimeCTS.IsCancellationRequested) elapsedTimeCTS.Cancel();
    }

    private async Task SetElapsedTimeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var span = DateTime.Now.Subtract(startTime);
                ElapsedTime = span.ToString(@"hh\:mm\:ss");
            });
            await Task.Delay(1000, token);
        }
    }

    private void SetRemainingTime(int current)
    {
        if (current <= 0) return;
        double elapsedSeconds = (DateTime.Now - startTime).TotalSeconds;
        var averageTime = elapsedSeconds / current;

        var totalSeconds = averageTime * (Datas.Count - current);
        var timeSpan = TimeSpan.FromSeconds(totalSeconds);
        RemainingTime = timeSpan.ToString(@"hh\:mm\:ss");
    }

    private void ChangeProgress(int current)
    {
        if (current <= 0) return;
        ProgressText = $"{current}/{Datas.Count}";
        ProgressValue = current > 0 && Datas.Count > 0 ? ((double)current / Datas.Count) * 100 : 0;
    }

    [RelayCommand(CanExecute = nameof(TaskIsCompleted))]
    private void OpenOutput(object? obj)
    {
        if (!Directory.Exists(Extractor.OutputPath))
        {
            MessageBox.Show(obj as Window, "指定的目录不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = Extractor.OutputPath,
            UseShellExecute = true
        });
    }

    [RelayCommand(CanExecute = nameof(TaskIsCompleted))]
    private async Task SaveErrors(object? obj)
    {
        var wnd = (obj as ExportWindow)!;
        var result = MessageBox.Show(wnd, $"保存当前错误记录", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        StringBuilder errors = new();
        for (int i = 0; i < Extractor.Datas.Count; i++)
        {
            var hfs = Extractor.Datas[i];
            if (hfs.Status == ExtractStatus.Failure) errors.AppendLine($"[{i}] {hfs.FileName}: {hfs.Error}");
        }
        var path = Path.Combine(AppContext.BaseDirectory, "ErrorReport.txt");
        await File.WriteAllTextAsync(path, errors.ToString());
        Utils.OpenByNotepad(path);
    }

    [RelayCommand(CanExecute = nameof(TaskIsCompleted))]
    private async Task SaveHistory(object? obj)
    {
        var wnd = (obj as ExportWindow)!;
        var result = MessageBox.Show(wnd, $"保存当前提取记录", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        StringBuilder history = new();
        Extractor.History.Clear();
        foreach (var hfs in Extractor.Datas)
        {
            if (hfs.Status == ExtractStatus.Success || hfs.Status == ExtractStatus.Skip)
            {
                history.AppendLine(hfs.FileName);
                Extractor.History.Add(hfs.FileName, default);
            }
        }
        var path = Path.Combine(AppContext.BaseDirectory, "History.txt");
        await File.WriteAllTextAsync(path, history.ToString());
        Utils.OpenByNotepad(path);
    }

    [RelayCommand]
    private static void CloseWindow(object? obj) => (obj as ExportWindow)!.Close();
}
