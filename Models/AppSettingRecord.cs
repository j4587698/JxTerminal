using System;
using TinyDb.Attributes;

namespace JxTerminal.Models;

/// <summary>
/// A persistent key-value configuration record stored in TinyDb.
/// </summary>
[Entity("app_settings")]
public class AppSettingRecord
{
    [Id]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public AppSettingRecord() { }

    public AppSettingRecord(string key, string value)
    {
        Key = key;
        Value = value;
        UpdatedAt = DateTime.UtcNow;
    }
}
