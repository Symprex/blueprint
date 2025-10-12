using System.Text;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Default implementation of <see cref="SourceWriter" /> that uses a backing <see cref="StringBuilder" />.
/// </summary>
public class SourceWriter : ISourceWriter
{
    /// <summary>
    /// We will always use this as a new line so that no matter what OS we are running on the generated code
    /// has the same newlines, meaning when we compare (i.e. AutoApiOperationExecutorBuilder) we do not need to
    /// consider this
    /// </summary>
    public const string NewLine = "\r\n";

    private static readonly string[] _indentationLevels =
    {
        string.Empty,
        new(' ', 1 * 4),
        new(' ', 2 * 4),
        new(' ', 3 * 4),
        new(' ', 4 * 4),
        new(' ', 5 * 4),
    };

    private readonly StringBuilder _writer;

    private int _level;
    private string _leadingSpaces = string.Empty;

    /// <summary>
    /// Initialises a new instance of the <see cref="SourceWriter" /> class.
    /// </summary>
    /// <param name="capacity">The suggested initial capacity of the underlying <see cref="StringBuilder" />.</param>
    public SourceWriter(int capacity = 2048)
    {
        this._writer = new StringBuilder(capacity);
    }

    /// <inheritdoc />
    public int IndentationLevel
    {
        get => this._level;

        private set
        {
            this._level = value;
            this._leadingSpaces = this._level < _indentationLevels.Length ? _indentationLevels[this._level] : new string(' ', this._level * 4);
        }
    }

    /// <inheritdoc />
    public ISourceWriter BlankLine()
    {
        this._writer.Append(NewLine);

        return this;
    }

    /// <inheritdoc />
    public ISourceWriter Indent()
    {
        this._writer.Append(this._leadingSpaces);

        return this;
    }

    /// <inheritdoc />
    public ISourceWriter Append(string text)
    {
        this._writer.Append(text);

        return this;
    }

    /// <inheritdoc />
    public ISourceWriter Append(char c)
    {
        this._writer.Append(c);

        return this;
    }

    /// <inheritdoc />
    public ISourceWriter WriteLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            this.BlankLine();
        }
        else
        {
            this._writer.Append(this._leadingSpaces);
            this._writer.Append(text);

            this.BlankLine();
        }

        return this;
    }

    /// <inheritdoc />
    public ISourceWriter Block(string text)
    {
        this.WriteLine(text);
        this.WriteLine("{");

        this.IndentationLevel++;

        return this;
    }

    /// <inheritdoc />
    public ISourceWriter FinishBlock(string? extra = null)
    {
        if (this.IndentationLevel == 0)
        {
            throw new InvalidOperationException("Not currently in a code block");
        }

        this.IndentationLevel--;

        if (string.IsNullOrEmpty(extra))
        {
            this.WriteLine("}");
        }
        else
        {
            this.WriteLine("}" + extra);
        }

        return this;
    }

    /// <summary>
    /// Indents a number of lines then removes the indentation.
    /// </summary>
    /// <param name="lines">The lines to indent.</param>
    /// <returns>This source writer.</returns>
    public ISourceWriter IndentLines(params string[] lines)
    {
        this.IndentationLevel++;

        foreach (var line in lines)
        {
            this.WriteLine(line);
        }

        this.IndentationLevel--;

        return this;
    }

    /// <summary>
    /// Returns the code that has been written to this <see cref="SourceWriter" />.
    /// </summary>
    /// <returns>The code of this writer.</returns>
    public string Code()
    {
        return this._writer.ToString();
    }
}
