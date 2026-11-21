using System.Text;

namespace TrackOps.Application.Services;

/// <summary>
/// Free text typed by a user is normalised once and checked strictly before it is stored. It is
/// never interpreted: no HTML, no links, no formatting. It is returned exactly as stored, and the
/// frontend renders it as text.
/// </summary>
public static class UserText
{
	private const char k_LineBreak = '\n';
	private const char k_Tab = '\t';

	/// <summary>
	/// Invisible characters that can change how text is displayed without showing themselves:
	/// bidi embeddings, overrides and isolates (they can reorder Hebrew and English so the screen
	/// shows something other than what was stored) and zero-width characters. The ordinary
	/// direction marks U+200E and U+200F, which Hebrew keyboards produce, are allowed.
	/// </summary>
	private static readonly HashSet<char> sr_InvisibleFormatting = new HashSet<char>
	{
		'‪', '‫', '‬', '‭', '‮',
		'⁦', '⁧', '⁨', '⁩',
		'​', '‌', '‍', '﻿',
	};

	/// <summary>A single line: Unicode NFC, trimmed. Returns null when the text cannot be read as valid Unicode.</summary>
	public static string? NormalizeLine(string? i_Text)
	{
		string? result = null;
		string text = i_Text ?? string.Empty;

		if (hasValidSurrogates(text))
		{
			result = text.Normalize(NormalizationForm.FormC).Trim();
		}

		return result;
	}

	/// <summary>Several lines: Unicode NFC, every line break as "\n", trimmed. Null when not valid Unicode.</summary>
	public static string? NormalizeLines(string? i_Text)
	{
		string? result = null;
		string text = i_Text ?? string.Empty;

		if (hasValidSurrogates(text))
		{
			result = text.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n").Replace('\r', k_LineBreak).Trim();
		}

		return result;
	}

	/// <summary>True when the text has a control character (other than line break and tab, if allowed) or invisible formatting.</summary>
	public static bool HasForbiddenCharacters(string i_Text, bool i_AllowLineBreaks)
	{
		bool isForbidden = false;

		foreach (char character in i_Text)
		{
			bool isAllowedWhitespace = i_AllowLineBreaks && (character == k_LineBreak || character == k_Tab);

			if ((char.IsControl(character) && !isAllowedWhitespace) || sr_InvisibleFormatting.Contains(character))
			{
				isForbidden = true;
			}
		}

		return isForbidden;
	}

	/// <summary>Every UTF-16 surrogate must be half of a pair; a lone half is not a character.</summary>
	private static bool hasValidSurrogates(string i_Text)
	{
		bool isValid = true;

		for (int index = 0; index < i_Text.Length && isValid; index++)
		{
			if (char.IsHighSurrogate(i_Text[index]))
			{
				isValid = index + 1 < i_Text.Length && char.IsLowSurrogate(i_Text[index + 1]);
				index++;
			}
			else if (char.IsLowSurrogate(i_Text[index]))
			{
				isValid = false;
			}
		}

		return isValid;
	}
}
