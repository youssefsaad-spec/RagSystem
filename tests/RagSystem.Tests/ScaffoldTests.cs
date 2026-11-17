using Microsoft.AspNetCore.Mvc.Testing;

namespace RagSystem.Tests;

public class ScaffoldTests(TestApp factory) : IClassFixture<TestApp>
{
    [Fact]
    public async Task App_starts_and_answers_requests()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        // No endpoints exist yet, so 404 proves the host is up and routing works.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
