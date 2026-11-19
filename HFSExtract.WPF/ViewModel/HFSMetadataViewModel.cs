using CommunityToolkit.Mvvm.ComponentModel;
using HFSExtract.WPF.Model;
using System.IO;

namespace HFSExtract.WPF.ViewModel
{
    public partial class HFSMetadataViewModel : ObservableObject
    {
        public HFSMetadata Model { get; }
        [ObservableProperty]
        public partial int Id { get; set; }
        [ObservableProperty, NotifyPropertyChangedFor(nameof(FileName))]
        public partial string FullPath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FullPath);
        [ObservableProperty]
        public partial ExtractStatus Status { get; set; }
        [ObservableProperty]
        public partial string Error { get; set; } = string.Empty;


        public HFSMetadataViewModel(HFSMetadata model)
        {
            Model = model;
            Update();
        }

        public void Update()
        {
            FullPath = Model.FullPath;
            Status = Model.Status;
            Error = Model.Error;
        }
    }
}
