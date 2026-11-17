namespace RagSystem.Embeddings.Services;

public static class UnitVector
{
    /// <summary>
    /// The same direction with length 1. A shortened embedding is not unit length. A vector that is already within 1e-4 of
    /// length 1 is returned as it is, and so is a vector of length 0 (it has no direction).
    /// </summary>
    public static float[] Of(float[] vector)
    {
        double sumOfSquares = 0;
        foreach (var x in vector)
            sumOfSquares += (double)x * x;

        var length = Math.Sqrt(sumOfSquares);
        if (length == 0 || double.IsNaN(length) || Math.Abs(length - 1) < 1e-4)
            return vector;

        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
            result[i] = (float)(vector[i] / length);
        return result;
    }
}
