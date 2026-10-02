using Soenneker.Utils.PooledStringBuilders;

namespace Soenneker.Quark.Gen.Validation.BuildTasks;

/// <summary>Formats arguments for process execution without shell interpretation.</summary>
public static class CommandLineArguments
{
    /// <summary>Quotes a single argument, escaping embedded quotes and trailing backslashes.</summary>
    public static string Quote(string value)
    {
        var builder = new PooledStringBuilder(value.Length + 2);
        try
        {
            builder.Append('"');
            var backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                int count = character == '"' ? backslashes * 2 + 1 : backslashes;
                for (var i = 0; i < count; i++) builder.Append('\\');
                builder.Append(character);
                backslashes = 0;
            }

            for (var i = 0; i < backslashes * 2; i++) builder.Append('\\');
            builder.Append('"');
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }
}
