using Soenneker.Extensions.ValueTask;
using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

/// <summary>Compares generated content with existing files without loading the entire file.</summary>
public static class GeneratedFile
{
    /// <summary>Returns whether an existing file contains exactly the supplied text, detecting its encoding from a BOM when present.</summary>
    public static async ValueTask<bool> Matches(string path, string content, IFileUtil fileUtil, CancellationToken cancellationToken)
    {
        if (!await fileUtil.Exists(path, cancellationToken).NoSync()) return false;
        using var reader = new StreamReader(fileUtil.OpenRead(path, log: false));
        char[] buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            var offset = 0;
            while (true)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(0, 4096), cancellationToken).NoSync();
                if (count == 0) return offset == content.Length;
                if (count > content.Length - offset || !buffer.AsSpan(0, count).SequenceEqual(content.AsSpan(offset, count))) return false;
                offset += count;
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }
}
