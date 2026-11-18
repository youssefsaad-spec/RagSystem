using RagSystem.Observability.Settings;

namespace RagSystem.Tests.Observability;

public class LangSmithSettingsTests
{
    [Fact]
    public void The_defaults_are_valid_and_tracing_is_off_without_a_key()
    {
        var settings = new LangSmithSettings().EnsureValid();

        Assert.Equal("https://api.smith.langchain.com/otel", settings.Endpoint);
        Assert.Equal("ragsystem", settings.Project);
        Assert.True(settings.CaptureContent);
        Assert.False(settings.HasApiKey);
        Assert.False(settings.IsActive);
    }

    [Fact]
    public void Tracing_is_on_only_with_a_key_and_Enabled()
    {
        Assert.True(new LangSmithSettings { ApiKey = "k" }.IsActive);
        Assert.False(new LangSmithSettings { ApiKey = "k", Enabled = false }.IsActive);
        Assert.False(new LangSmithSettings { ApiKey = "  " }.IsActive);
    }

    [Theory]
    [InlineData("https://api.smith.langchain.com/otel", "https://api.smith.langchain.com/otel/v1/traces")]
    [InlineData("https://eu.api.smith.langchain.com/otel/", "https://eu.api.smith.langchain.com/otel/v1/traces")]
    [InlineData("https://langsmith.example.com/api/v1/otel", "https://langsmith.example.com/api/v1/otel/v1/traces")]
    public void The_traces_address_is_the_endpoint_plus_v1_traces(string endpoint, string expected) =>
        Assert.Equal(expected, new LangSmithSettings { Endpoint = endpoint }.TracesUrl);

    [Fact]
    public void The_headers_carry_the_key_and_the_project_and_the_workspace_only_when_set()
    {
        Assert.Equal("x-api-key=secret,Langsmith-Project=ragsystem", new LangSmithSettings { ApiKey = "secret" }.Headers);
        Assert.Equal(
            "x-api-key=secret,Langsmith-Project=docs-bot,X-Tenant-Id=ws-1",
            new LangSmithSettings { ApiKey = "secret", Project = "docs-bot", WorkspaceId = "ws-1" }.Headers);
    }

    [Theory]
    [InlineData("Endpoint", "not a url")]
    [InlineData("Endpoint", "ftp://example.com/otel")]
    [InlineData("Endpoint", "https://api.smith.langchain.com/otel/v1/traces")]
    [InlineData("Project", " ")]
    [InlineData("Project", "a,b")]
    [InlineData("ApiKey", "a=b")]
    [InlineData("ServiceName", "")]
    public void A_wrong_value_stops_the_app_and_the_message_names_the_setting(string name, string value)
    {
        var settings = name switch
        {
            "Endpoint" => new LangSmithSettings { Endpoint = value },
            "Project" => new LangSmithSettings { Project = value },
            "ApiKey" => new LangSmithSettings { ApiKey = value },
            _ => new LangSmithSettings { ServiceName = value },
        };

        var error = Assert.Throws<InvalidOperationException>(() => settings.EnsureValid());

        Assert.Contains($"{LangSmithSettings.SectionName}:{name}", error.Message);
    }
}
