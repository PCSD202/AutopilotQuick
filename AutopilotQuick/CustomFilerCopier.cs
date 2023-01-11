#region

using System.IO;
using System.Threading.Tasks;

#endregion

namespace AutopilotQuick;

public delegate void ProgressChangeDelegate(long totalFileSize, long totalBytesDownloaded, double progressPercentage, ref bool Cancel);
public delegate void Completedelegate();

class CustomFileCopier
{
    public CustomFileCopier(string Source, string Dest)
    {
        this.SourceFilePath = Source;
        this.DestFilePath = Dest;

        OnProgressChanged += delegate { };
        OnComplete += delegate { };
    }

    public async Task CopyAsync()
    {
        byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
        bool cancelFlag = false;

        await using (FileStream source = new FileStream(SourceFilePath, FileMode.Open, FileAccess.Read))
        {
            var fileLength = source.Length;
            await using (FileStream dest = new FileStream(DestFilePath, FileMode.Create, FileAccess.Write))
            {
                long totalBytes = 0;
                var currentBlockSize = 0;

                while ((currentBlockSize = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalBytes += currentBlockSize;
                    double percentage = (double)totalBytes * 100.0 / fileLength;

                    dest.Write(buffer, 0, currentBlockSize);

                    cancelFlag = false;
                    OnProgressChanged(fileLength, totalBytes, percentage, ref cancelFlag);

                    if (cancelFlag == true)
                    {
                        // Delete dest file here
                        break;
                    }
                }
            }
        }

        OnComplete();
    }
    
    
    public void Copy()
    {
        byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
        bool cancelFlag = false;

        using (FileStream source = new FileStream(SourceFilePath, FileMode.Open, FileAccess.Read))
        {
            var fileLength = source.Length;
            using (FileStream dest = new FileStream(DestFilePath, FileMode.Create, FileAccess.Write))
            {
                long totalBytes = 0;
                var currentBlockSize = 0;

                while ((currentBlockSize = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalBytes += currentBlockSize;
                    double percentage = (double)totalBytes * 100.0 / fileLength;

                    dest.Write(buffer, 0, currentBlockSize);

                    cancelFlag = false;
                    OnProgressChanged(fileLength, totalBytes, percentage, ref cancelFlag);

                    if (cancelFlag == true)
                    {
                        // Delete dest file here
                        break;
                    }
                }
            }
        }

        OnComplete();
    }

    public string SourceFilePath { get; set; }
    public string DestFilePath { get; set; }

    public event ProgressChangeDelegate OnProgressChanged;
    public event Completedelegate OnComplete;
}