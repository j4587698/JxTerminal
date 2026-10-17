using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.KeyStore;

public interface IKeyStoreService
{
    ObservableCollection<KeyItem> Keys { get; }
    Task LoadKeysAsync();
    Task<KeyItem> GenerateKeyAsync(string name, KeyType type, string? comment = null);
    Task<KeyItem> ImportKeyAsync(string name, string privateKeyContent, string? comment = null);
    Task SaveKeyAsync(KeyItem key);
    Task DeleteKeyAsync(Guid keyId);
    KeyItem? GetKey(Guid keyId);
}
