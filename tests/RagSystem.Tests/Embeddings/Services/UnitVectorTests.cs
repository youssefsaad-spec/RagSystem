using RagSystem.Embeddings.Services;

namespace RagSystem.Tests.Embeddings.Services;

public class UnitVectorTests
{
    private static double Length(float[] v) => Math.Sqrt(v.Sum(x => (double)x * x));

    [Fact]
    public void A_vector_is_scaled_to_length_1_and_keeps_its_direction()
    {
        var result = UnitVector.Of([3f, 4f]);

        Assert.Equal(1.0, Length(result), precision: 6);
        Assert.Equal(0.6f, result[0], precision: 5);
        Assert.Equal(0.8f, result[1], precision: 5);
    }

    [Fact]
    public void A_vector_already_of_length_1_is_returned_as_it_is()
    {
        var vector = new[] { 0.6f, 0.8f };

        Assert.Same(vector, UnitVector.Of(vector));
    }

    [Fact]
    public void A_length_within_1e_4_of_1_is_left_alone()
    {
        var vector = new[] { 1.00005f, 0f };

        Assert.Same(vector, UnitVector.Of(vector));
    }

    [Fact]
    public void A_vector_of_zeros_has_no_direction_and_is_returned_as_it_is()
    {
        var vector = new float[4];

        Assert.Same(vector, UnitVector.Of(vector));
    }

    [Fact]
    public void The_input_is_not_changed()
    {
        var vector = new[] { 3f, 4f };

        UnitVector.Of(vector);

        Assert.Equal([3f, 4f], vector);
    }
}
