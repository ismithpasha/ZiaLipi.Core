// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO.Compression;
using System.Text.Json;
using ZiaLipi.Core.Phonetic;

namespace ZiaLipi.Core;

/// <summary>Reads the data files embedded in this assembly (see tools/phonetic-data).</summary>
internal static class DataFiles
{
    public static Stream Open(string name) =>
        typeof(DataFiles).Assembly.GetManifestResourceStream("ZiaLipi.Data." + name)
        ?? throw new FileNotFoundException("Missing embedded data file", name);

    public static Grammar LoadGrammar(string name)
    {
        using var stream = Open(name);
        return Grammar.Load(stream);
    }

    public static Dictionary<string, string> LoadMap(string name)
    {
        using var stream = Open(name);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException(name);
    }

    public static StreamReader OpenGzipText(string name) =>
        new(new GZipStream(Open(name), CompressionMode.Decompress), System.Text.Encoding.UTF8);
}
