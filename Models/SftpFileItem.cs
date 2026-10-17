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
    
    // Display size: "-" for directories, formatted size for files
    public string DisplaySize => IsDirectory ? "-" : FormatSize(Size);
    
    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#} {sizes[order]}";
    }
    
    // Original reference
    public ISftpFile? OriginalFile { get; }

    public SftpFileItem(string name, string path = "", bool isDirectory = false, long size = 0)
    {
        Name = name;
        Path = string.IsNullOrEmpty(path) ? "/" + name : path;
        IsDirectory = isDirectory;
        Size = size;
        Icon = isDirectory ? MaterialIconKind.Folder : MaterialIconKind.FileDocument;
    }

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
