using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace CIS580.UI;

/// <summary>
/// Helpers for making text fit on screen: wrapping it onto several lines, or shrinking it.
/// </summary>
public static class TextLayout
{
    /// <summary>
    /// Breaks text into lines no wider than <paramref name="maxWidth"/> (at the given scale),
    /// breaking between words. Existing line breaks are kept. A single word wider than the
    /// limit gets a line of its own (use <see cref="FitScale"/> to shrink it).
    /// </summary>
    public static string Wrap(SpriteFont font, string text, float maxWidth, float scale = 1f)
    {
        if (font == null || string.IsNullOrEmpty(text) || maxWidth <= 0)
            return text;

        var result = new StringBuilder();
        var paragraphs = text.Replace("\r", "").Split('\n');
        for (int p = 0; p < paragraphs.Length; p++)
        {
            if (p > 0)
                result.Append('\n');

            var line = new StringBuilder();
            foreach (var word in paragraphs[p].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : line.ToString() + " " + word;
                if (line.Length > 0 && font.MeasureString(candidate).X * scale > maxWidth)
                {
                    result.Append(line).Append('\n');
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }
            result.Append(line);
        }
        return result.ToString();
    }

    /// <summary>
    /// The scale to draw text at so it is no wider than <paramref name="maxWidth"/>: the
    /// preferred scale if it already fits, smaller if not.
    /// </summary>
    public static float FitScale(SpriteFont font, string text, float maxWidth, float preferredScale = 1f)
    {
        if (font == null || string.IsNullOrEmpty(text))
            return preferredScale;

        float width = font.MeasureString(text).X * preferredScale;
        return width <= maxWidth || width <= 0 ? preferredScale : preferredScale * maxWidth / width;
    }

    // each font's glyphs (SpriteFont.GetGlyphs() makes a new copy every call, so keep one)
    private static readonly ConditionalWeakTable<SpriteFont, Dictionary<char, SpriteFont.Glyph>> GlyphCache = new();

    /// <summary>
    /// How far below the drawing position the text's visible pixels start and end (unscaled), for
    /// one line of text. Fonts often have lots of empty space above or below their letters, or
    /// letters that poke outside the line, so centering on these (rather than on
    /// <see cref="SpriteFont.LineSpacing"/>) makes text look properly centered in a button or row.
    /// </summary>
    public static (float Top, float Bottom) InkExtent(SpriteFont font, string text)
    {
        var glyphs = GlyphCache.GetValue(font, f => f.GetGlyphs());
        float top = float.MaxValue, bottom = float.MinValue;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || !glyphs.TryGetValue(c, out var glyph))
                continue;
            top = Math.Min(top, glyph.Cropping.Y);
            bottom = Math.Max(bottom, glyph.Cropping.Y + glyph.BoundsInTexture.Height);
        }
        return top <= bottom ? (top, bottom) : (0, font.LineSpacing);
    }

    /// <summary>
    /// The y position to draw one line of text at (with no origin) so its visible pixels are
    /// centered on <paramref name="centerY"/>
    /// </summary>
    public static float CenteredTop(SpriteFont font, string text, float centerY, float scale = 1f)
    {
        var (top, bottom) = InkExtent(font, text);
        return MathF.Round(centerY - (top + bottom) / 2 * scale);
    }

    /// <summary>
    /// Splits text into its lines
    /// </summary>
    public static IReadOnlyList<string> Lines(string text)
        => string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Replace("\r", "").Split('\n');
}
