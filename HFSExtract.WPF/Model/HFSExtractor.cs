using HFSExtract.WPF.ViewModel;
using System.IO;

namespace HFSExtract.WPF.Model;

public class HFSExtractor
{
    public List<HFSMetadata> Datas { get; } = [];
    public Dictionary<string, object?> History { get; } = [];
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool Overwrite { get; set; }
    public bool SkipHistory { get; set; } = true;
    public int ThreadCount { get; set; }


    public async Task ExportAllAsync(Action<HFSMetadata> onHFSExported, CancellationToken token)
    {
        var count = Math.Max(1, Environment.ProcessorCount - 1);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = ThreadCount <= 0 ? count : ThreadCount,
            CancellationToken = token
        };

        await Parallel.ForEachAsync(Datas, options, async (hfs, token) =>
        {
            await Task.Run(() => Export(hfs), token);
            onHFSExported?.Invoke(hfs);
        });
    }

    public void FindArchives()
    {
        Datas.Clear();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, };
        var files = Directory.EnumerateFiles(InputPath, "*.hfs", options);
        foreach (var file in files)
        {
            Datas.Add(new() { FullPath = file });
        }
    }

    private void Export(HFSMetadata hfs)
    {
        try
        {
            if (SetMetadataByHistory(hfs)) return;

            using var stream = File.OpenRead(hfs.FullPath);
            using var archive = new HFSArchive(stream, hfs.FileName);
            ExportAssets(archive);
            hfs.Status = ExtractStatus.Success;
        }
        catch (Exception ex)
        {
            hfs.Status = ExtractStatus.Failure;
            hfs.Error = ex.Message;
        }
    }

    private bool SetMetadataByHistory(HFSMetadata hfs)
    {
        var valid = SkipHistory && History.ContainsKey(hfs.FileName);
        if (valid) hfs.Status = ExtractStatus.Skip;
        return valid;
    }

    private void ExportAssets(HFSArchive archive)
    {
        foreach (var entry in archive.Files)
        {
            var fileName = entry.Key;
            try
            {
                var data = archive.ReadFile(fileName);
                var target = Utils.GetSafeOutputPath(OutputPath, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Utils.WriteOutputFile(target, data.Span, Overwrite);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
