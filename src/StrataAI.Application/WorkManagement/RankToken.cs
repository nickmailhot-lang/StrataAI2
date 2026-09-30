using System.Numerics;

namespace StrataAI.Application.WorkManagement;

/// <summary>
/// Fixed-width lexicographically sortable rank tokens with a very large numeric
/// address space. Ordinary moves select a midpoint and do not renumber siblings.
/// A future rebalance is required only when a local interval is exhausted.
/// </summary>
public static class RankToken
{
    public const int Width = 30;

    private static readonly BigInteger Maximum =
        BigInteger.Pow(10, Width) - BigInteger.One;

    public static string Initial() => Format(Maximum / 2);

    public static string After(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return Initial();
        }

        return Between(current, null);
    }

    public static string Before(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return Initial();
        }

        return Between(null, current);
    }

    public static string Between(string? before, string? after)
    {
        var lower = before is null ? BigInteger.Zero : Parse(before);
        var upper = after is null ? Maximum : Parse(after);

        if (lower >= upper)
        {
            throw new ArgumentException(
                "The lower rank must be less than the upper rank.");
        }

        var midpoint = (lower + upper) / 2;
        if (midpoint <= lower || midpoint >= upper)
        {
            throw new RankSpaceExhaustedException();
        }

        return Format(midpoint);
    }

    public static bool IsValid(string value)
    {
        if (value.Length != Width || value.Any(character => !char.IsAsciiDigit(character)))
        {
            return false;
        }

        return BigInteger.TryParse(value, out var parsed) &&
            parsed > BigInteger.Zero &&
            parsed < Maximum;
    }

    private static BigInteger Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException("Invalid rank token.", nameof(value));
        }

        return BigInteger.Parse(value);
    }

    private static string Format(BigInteger value) =>
        value.ToString().PadLeft(Width, '0');
}

public sealed class RankSpaceExhaustedException()
    : InvalidOperationException("No rank value remains between the requested siblings.");
