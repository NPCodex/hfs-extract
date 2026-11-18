using System.IO;

namespace HFSExtract.WPF.Model
{
    public class HFSMetadata
    {
        public string FullPath
        {
            get;
            set
            {
                field = value;
                FileName = Path.GetFileName(value);
            }
        } = string.Empty;
        public string FileName { get; private set; } = string.Empty;
        public ExtractStatus Status { get; set; }
        public string Error { get; set; } = string.Empty;
    }
}
