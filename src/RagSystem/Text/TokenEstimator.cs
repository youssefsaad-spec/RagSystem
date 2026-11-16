namespace RagSystem.Text;

/// <summary>Rough local token count: <c>ceil(chars / 3.5)</c>. No model call.</summary>
public static class TokenEstimator
{
    public static int Estimate(string text)
    {
        // chars / 3.5 == chars * 2 / 7, so round up with whole numbers (no floating point).
        return (text.Length * 2 + 6) / 7;
    }
}
