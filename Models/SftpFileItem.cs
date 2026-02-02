using Material.Icons;
using Renci.SshNet.Sftp;

namespace JxTerminal.Models;

public class SftpFileItem
{
    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public long Size { get; }
    public string Permissions { get; } = "";
    public MaterialIconKind Icon { get; }
    
    // Original reference
    public ISftpFile OriginalFile { get; }

    public SftpFileItem(ISftpFile file)
    {
        OriginalFile = file;
        Name = file.Name;
        Path = file.FullName;
        IsDirectory = file.IsDirectory;
        Size = file.Length;
        // Permissions = file.Attributes.ToString(); // Simplified

        if (IsDirectory)
        {
            Icon = MaterialIconKind.Folder;
        }
        else
        {
            // Simple extension mapping
            var ext = System.IO.Path.GetExtension(Name).ToLower();
            Icon = ext switch
            {
                ".txt" or ".md" or ".log" => MaterialIconKind.FileDocument,
                ".jpg" or ".png" or ".gif" => MaterialIconKind.FileImage,
                ".zip" or ".tar" or ".gz" => MaterialIconKind.ZipBox,
                ".cs" or ".js" or ".py" or ".html" => MaterialIconKind.CodeBraces,
                ".sh" or ".bash" => MaterialIconKind.ScriptText,
                _ => MaterialIconKind.File
            };
        }
    }
}
