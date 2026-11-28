using System.Text;

namespace ChudLink.Host;

/// <summary>Bound IPC lines without one async read and allocation for every character.</summary>
public sealed class BoundedLineReader(TextReader reader, int maximumLength = 256 * 1024)
{
    readonly char[] buffer = new char[4096];
    int offset, count;
    public async Task<string?> ReadAsync()
    {
        StringBuilder? pending = null;
        while (true)
        {
            if (offset == count)
            {
                count = await reader.ReadAsync(buffer.AsMemory()); offset = 0;
                if (count == 0) return pending?.ToString().TrimEnd('\r');
            }
            var newline = Array.IndexOf(buffer, '\n', offset, count - offset);
            var end = newline >= 0 ? newline : count; var length = end - offset;
            if ((pending?.Length ?? 0) + length > maximumLength) throw new InvalidDataException("Worker line exceeds " + maximumLength + " characters");
            if (newline >= 0 && pending == null) { var line = new string(buffer, offset, length).TrimEnd('\r'); offset = newline + 1; return line; }
            (pending ??= new StringBuilder()).Append(buffer, offset, length); offset = end;
            if (newline >= 0) { offset++; return pending.ToString().TrimEnd('\r'); }
        }
    }
}
