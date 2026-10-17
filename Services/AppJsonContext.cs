using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using JxTerminal.Models;

namespace JxTerminal.Services;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ObservableCollection<ConnectionGroup>))]
[JsonSerializable(typeof(IEnumerable<ConnectionGroup>))]
[JsonSerializable(typeof(ConnectionGroup))]
[JsonSerializable(typeof(SavedConnection))]
[JsonSerializable(typeof(List<SavedConnection>))]
[JsonSerializable(typeof(ConnectionType))]
[JsonSerializable(typeof(SshAuthenticationType))]
[JsonSerializable(typeof(TerminalSettings))]
[JsonSerializable(typeof(FolderTransferMode))]
[JsonSerializable(typeof(TerminalColorScheme))]
[JsonSerializable(typeof(List<TerminalColorScheme>))]
[JsonSerializable(typeof(ObservableCollection<SnippetItem>))]
[JsonSerializable(typeof(List<SnippetItem>))]
[JsonSerializable(typeof(IEnumerable<SnippetItem>))]
[JsonSerializable(typeof(SnippetItem))]
[JsonSerializable(typeof(ObservableCollection<KeyItem>))]
[JsonSerializable(typeof(List<KeyItem>))]
[JsonSerializable(typeof(IEnumerable<KeyItem>))]
[JsonSerializable(typeof(KeyItem))]
[JsonSerializable(typeof(KeyType))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(TerminalHistoryItem))]
[JsonSerializable(typeof(List<TerminalHistoryItem>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(SyncConfig))]
[JsonSerializable(typeof(AuthResult))]
[JsonSerializable(typeof(SyncAuthRequest))]
[JsonSerializable(typeof(SyncBackupUploadRequest))]
[JsonSerializable(typeof(SyncServerError))]
[JsonSerializable(typeof(RemoteBackupInfo))]
[JsonSerializable(typeof(List<RemoteBackupInfo>))]
[JsonSerializable(typeof(RemoteBackupRecord))]
[JsonSerializable(typeof(RemoteSyncStatus))]
[JsonSerializable(typeof(SyncSnapshot))]
[JsonSerializable(typeof(ChangePasswordRequest))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
