using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using JxTerminal.Models;

namespace JxTerminal.Services;

[JsonSourceGenerationOptions(WriteIndented = true, IgnoreReadOnlyFields = false)]
[JsonSerializable(typeof(ObservableCollection<ConnectionGroup>))]
[JsonSerializable(typeof(IEnumerable<ConnectionGroup>))]
[JsonSerializable(typeof(ConnectionGroup))]
[JsonSerializable(typeof(SavedConnection))]
[JsonSerializable(typeof(List<SavedConnection>))]
[JsonSerializable(typeof(ConnectionType))]
[JsonSerializable(typeof(SshAuthenticationType))]
[JsonSerializable(typeof(TerminalSettings))]
[JsonSerializable(typeof(TerminalColorScheme))]
[JsonSerializable(typeof(ObservableCollection<SnippetItem>))]
[JsonSerializable(typeof(List<SnippetItem>))]
[JsonSerializable(typeof(IEnumerable<SnippetItem>))]
[JsonSerializable(typeof(SnippetItem))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
