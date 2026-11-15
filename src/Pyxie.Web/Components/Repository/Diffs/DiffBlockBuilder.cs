using System.Text;

namespace Pyxie.Web.Components.Repository.Diffs;

/// <summary>Collects classified lines into blocks of consecutive lines of one kind.</summary>
internal sealed class DiffBlockBuilder
{
	private readonly List<DiffBlock> _blocks = [];
	private readonly StringBuilder _text = new();
	private DiffBlockKind? _kind;

	/// <summary>Adds a line to the current block, or starts a new block when its kind differs or it opens another file.</summary>
	/// <param name="kind">What the line is.</param>
	/// <param name="line">The line, without its line break.</param>
	/// <param name="startsFile">Whether the line opens a file's diff, which always starts a block of its own.</param>
	public void Add(DiffBlockKind kind, string line, bool startsFile)
	{
		if (_kind is DiffBlockKind current)
		{
			if (current == kind && !startsFile)
			{
				_text.Append('\n').Append(line);

				return;
			}

			Flush(current);
		}

		_kind = kind;
		_text.Append(line);
	}

	/// <summary>Finishes the last block.</summary>
	/// <returns>The blocks in the order their lines were added.</returns>
	public IReadOnlyList<DiffBlock> Build()
	{
		if (_kind is DiffBlockKind last)
		{
			Flush(last);
			_kind = null;
		}

		return _blocks;
	}

	private void Flush(DiffBlockKind kind)
	{
		_blocks.Add(new DiffBlock(kind, _text.ToString()));
		_text.Clear();
	}
}
