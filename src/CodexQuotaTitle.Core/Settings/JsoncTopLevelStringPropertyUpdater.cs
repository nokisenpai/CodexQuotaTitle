using System.Text.Json;

namespace CodexQuotaTitle.Settings;

internal static class JsoncTopLevelStringPropertyUpdater
{
    public static string ReplaceStringValue(string contents, string propertyName, string value)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        ArgumentNullException.ThrowIfNull(value);

        var cursor = SkipTrivia(contents, 0);
        if (cursor >= contents.Length || contents[cursor] != '{')
        {
            throw Invalid("settings-json-invalid");
        }

        cursor++;
        var found = false;
        var valueStart = -1;
        var valueEnd = -1;

        while (true)
        {
            cursor = SkipTrivia(contents, cursor);
            if (cursor >= contents.Length)
            {
                throw Invalid("settings-json-invalid");
            }

            if (contents[cursor] == '}')
            {
                break;
            }

            if (contents[cursor] != '"')
            {
                throw Invalid("settings-json-invalid");
            }

            var propertyStart = cursor;
            var propertyEnd = ReadJsonStringEnd(contents, cursor);
            var currentPropertyName = JsonSerializer.Deserialize<string>(contents[propertyStart..propertyEnd])
                ?? throw Invalid("settings-json-invalid");
            cursor = SkipTrivia(contents, propertyEnd);
            if (cursor >= contents.Length || contents[cursor] != ':')
            {
                throw Invalid("settings-json-invalid");
            }

            cursor = SkipTrivia(contents, cursor + 1);
            if (cursor >= contents.Length)
            {
                throw Invalid("settings-json-invalid");
            }

            if (string.Equals(currentPropertyName, propertyName, StringComparison.Ordinal))
            {
                if (found)
                {
                    throw Invalid("settings-template-property-ambiguous");
                }

                if (contents[cursor] != '"')
                {
                    throw Invalid("settings-template-property-invalid");
                }

                found = true;
                valueStart = cursor;
                valueEnd = ReadJsonStringEnd(contents, cursor);
                cursor = valueEnd;
            }
            else
            {
                cursor = SkipJsonValue(contents, cursor);
            }

            cursor = SkipTrivia(contents, cursor);
            if (cursor >= contents.Length)
            {
                throw Invalid("settings-json-invalid");
            }

            if (contents[cursor] == ',')
            {
                cursor++;
                continue;
            }

            if (contents[cursor] != '}')
            {
                throw Invalid("settings-json-invalid");
            }

            break;
        }

        if (!found)
        {
            throw Invalid("settings-template-property-missing");
        }

        var serializedValue = JsonSerializer.Serialize(value);
        return string.Concat(contents.AsSpan(0, valueStart), serializedValue, contents.AsSpan(valueEnd));
    }

    private static int SkipJsonValue(string contents, int cursor)
    {
        cursor = SkipTrivia(contents, cursor);
        if (cursor >= contents.Length)
        {
            throw Invalid("settings-json-invalid");
        }

        if (contents[cursor] == '"')
        {
            return ReadJsonStringEnd(contents, cursor);
        }

        if (contents[cursor] is not ('{' or '['))
        {
            var primitiveStart = cursor;
            while (cursor < contents.Length &&
                   !char.IsWhiteSpace(contents[cursor]) &&
                   contents[cursor] is not (',' or '}' or ']') &&
                   !(contents[cursor] == '/' && cursor + 1 < contents.Length && contents[cursor + 1] is ('/' or '*')))
            {
                cursor++;
            }

            if (cursor == primitiveStart)
            {
                throw Invalid("settings-json-invalid");
            }

            return cursor;
        }

        var nesting = new Stack<char>();
        while (cursor < contents.Length)
        {
            var character = contents[cursor];
            if (character == '"')
            {
                cursor = ReadJsonStringEnd(contents, cursor);
                continue;
            }

            if (character == '/' && cursor + 1 < contents.Length && contents[cursor + 1] is ('/' or '*'))
            {
                cursor = SkipTrivia(contents, cursor);
                continue;
            }

            if (character == '{')
            {
                nesting.Push('}');
            }
            else if (character == '[')
            {
                nesting.Push(']');
            }
            else if (character is '}' or ']')
            {
                if (nesting.Count == 0 || nesting.Pop() != character)
                {
                    throw Invalid("settings-json-invalid");
                }

                if (nesting.Count == 0)
                {
                    return cursor + 1;
                }
            }

            cursor++;
        }

        throw Invalid("settings-json-invalid");
    }

    private static int SkipTrivia(string contents, int cursor)
    {
        while (cursor < contents.Length)
        {
            if (char.IsWhiteSpace(contents[cursor]))
            {
                cursor++;
                continue;
            }

            if (contents[cursor] != '/' || cursor + 1 >= contents.Length)
            {
                break;
            }

            if (contents[cursor + 1] == '/')
            {
                cursor += 2;
                while (cursor < contents.Length && contents[cursor] is not ('\r' or '\n'))
                {
                    cursor++;
                }

                continue;
            }

            if (contents[cursor + 1] == '*')
            {
                var commentEnd = contents.IndexOf("*/", cursor + 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    throw Invalid("settings-json-invalid");
                }

                cursor = commentEnd + 2;
                continue;
            }

            break;
        }

        return cursor;
    }

    private static int ReadJsonStringEnd(string contents, int cursor)
    {
        if (contents[cursor] != '"')
        {
            throw Invalid("settings-json-invalid");
        }

        cursor++;
        while (cursor < contents.Length)
        {
            if (contents[cursor] == '\\')
            {
                cursor += 2;
                continue;
            }

            if (contents[cursor] == '"')
            {
                return cursor + 1;
            }

            if (contents[cursor] is '\r' or '\n')
            {
                throw Invalid("settings-json-invalid");
            }

            cursor++;
        }

        throw Invalid("settings-json-invalid");
    }

    private static InvalidDataException Invalid(string reason) => new(reason);
}
