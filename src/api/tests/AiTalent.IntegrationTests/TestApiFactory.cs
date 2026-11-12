using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AiTalent.IntegrationTests;

public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Invites:SigningKey", "integration-test-signing-key-at-least-32-bytes");
        builder.UseSetting("SUPABASE_URL", "http://127.0.0.1:54321");
    }
}
