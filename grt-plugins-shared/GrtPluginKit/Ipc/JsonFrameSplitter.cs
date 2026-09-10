using System.Text;

namespace GrtPluginKit.Ipc;

/// <summary>
/// GRT streams JSON messages back-to-back with no delimiter or length prefix
/// (e.g. <c>{"Result":..}{"Event_TabComputed":..}</c>). A message is complete
/// when its curly braces balance. This splitter tracks brace depth while
/// ignoring braces that appear inside JSON string literals, which is more
/// robust than the reference plugin's naive "}{" split.
/// </summary>
internal sealed class JsonFrameSplitter
{
    private readonly StringBuilder _buf = new();
    private int _depth;
    private bool _inString;
    private bool _escape;

    public IEnumerable<string> Feed(string chunk)
    {
        foreach (char c in chunk)
        {
            // Drop anything before the first '{' of a message (resync).
            if (_depth == 0 && !_inString && c != '{')
                continue;

            _buf.Append(c);

            if (_inString)
            {
                if (_escape) _escape = false;
                else if (c == '\\') _escape = true;
                else if (c == '"') _inString = false;
                continue;
            }

            switch (c)
            {
                case '"': _inString = true; break;
                case '{': _depth++; break;
                case '}':
                    _depth--;
                    if (_depth <= 0)
                    {
                        string msg = _buf.ToString();
                        _buf.Clear();
                        _depth = 0;
                        if (msg.Length > 1) yield return msg;
                    }
                    break;
            }
        }
    }

    public void Reset()
    {
        _buf.Clear();
        _depth = 0;
        _inString = false;
        _escape = false;
    }
}
