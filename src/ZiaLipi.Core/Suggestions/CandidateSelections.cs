// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Encodings.Web;
using System.Text.Json;

namespace ZiaLipi.Core.Suggestions;

/// <summary>
/// Remembers which candidate the user picked for a Roman word, so it is
/// offered first next time. Stored only in a local file on this device.
/// Not thread-safe, except <see cref="WriteSnapshot"/>.
/// </summary>
public sealed class CandidateSelections
{
    private readonly string? _filePath;
    private readonly Dictionary<string, string> _map;
    private readonly Lock _writeLock = new();
    private bool _dirty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="filePath">Where to persist; null keeps selections in memory only.</param>
    public CandidateSelections(string? filePath = null)
    {
        _filePath = filePath;
        _map = Load(filePath);
    }

    public int Count => _map.Count;

    /// <summary>
    /// When true (default) every committed word is saved immediately. Apps that
    /// prefer to save periodically turn this off and call <see cref="TakeSnapshot"/>.
    /// </summary>
    public bool SaveOnCommit { get; set; } = true;

    internal bool TryGet(string romanWord, out string candidate)
    {
        if (_map.TryGetValue(romanWord, out var value) && value.Length > 0)
        {
            candidate = value;
            return true;
        }
        candidate = "";
        return false;
    }

    internal bool Contains(string romanWord) => TryGet(romanWord, out _);

    internal void Set(string romanWord, string candidate)
    {
        if (_map.TryGetValue(romanWord, out var old) && old == candidate)
            return;
        _map[romanWord] = candidate;
        _dirty = true;
    }

    internal void Committed()
    {
        if (SaveOnCommit)
            Save();
    }

    public void Clear()
    {
        _map.Clear();
        _dirty = true;
        Save();
    }

    /// <summary>Writes pending changes to disk now. No-op when nothing changed.</summary>
    public void Save()
    {
        if (TakeSnapshot() is { } json)
            WriteSnapshot(json);
    }

    /// <summary>Serialized selections if anything changed since the last snapshot, else null.</summary>
    public string? TakeSnapshot()
    {
        if (!_dirty || _filePath == null)
            return null;
        _dirty = false;
        return JsonSerializer.Serialize(_map, JsonOptions);
    }

    /// <summary>Writes a snapshot to the file atomically. Safe to call from any thread.</summary>
    public void WriteSnapshot(string json)
    {
        if (_filePath == null)
            return;
        lock (_writeLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                string temp = _filePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _filePath, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Dictionary<string, string> Load(string? filePath)
    {
        try
        {
            if (filePath != null && File.Exists(filePath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return [];
    }
}
