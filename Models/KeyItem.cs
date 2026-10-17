using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using JxTerminal.Services;
using Material.Icons;
using TinyDb.Attributes;

namespace JxTerminal.Models;

public enum KeyType
{
    RSA2048,
    RSA4096,
    ED25519,
    ECDSA
}

[Entity("keys")]
public class KeyItem : ObservableObject
{
    [Id]
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = string.Empty;
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? LocalizationService.Instance["NewKey"] : _name;
        set => SetProperty(ref _name, value);
    }

    private KeyType _type = KeyType.ED25519;
    public KeyType Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(DisplayType));
                OnPropertyChanged(nameof(IconKind));
            }
        }
    }

    [JsonIgnore]
    [BsonIgnore]
    public string PrivateKeyPem { get; set; } = string.Empty;

    public string EncryptedPrivateKey
    {
        get => SecurityService.Encrypt(PrivateKeyPem);
        set => PrivateKeyPem = SecurityService.Decrypt(value);
    }

    private string _publicKeyOpenSsh = string.Empty;
    public string PublicKeyOpenSsh
    {
        get => _publicKeyOpenSsh;
        set => SetProperty(ref _publicKeyOpenSsh, value);
    }

    private string _fingerprint = string.Empty;
    public string Fingerprint
    {
        get => _fingerprint;
        set => SetProperty(ref _fingerprint, value);
    }

    private DateTime _createdAt = DateTime.Now;
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    private string _notes = string.Empty;
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    [JsonIgnore]
    public string DisplayType => Type switch
    {
        KeyType.RSA2048 => "RSA 2048",
        KeyType.RSA4096 => "RSA 4096",
        KeyType.ED25519 => "ED25519",
        KeyType.ECDSA => "ECDSA 256",
        _ => "Unknown"
    };

    [JsonIgnore]
    public MaterialIconKind IconKind => Type switch
    {
        KeyType.ED25519 => MaterialIconKind.KeyVariant,
        KeyType.RSA4096 or KeyType.RSA2048 => MaterialIconKind.ShieldKey,
        _ => MaterialIconKind.Key
    };
}
