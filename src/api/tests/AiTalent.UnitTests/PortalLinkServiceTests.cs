using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Portal;

namespace AiTalent.UnitTests;

public class PortalLinkServiceTests
{
    private sealed class FakeAuthAdmin(string? token) : IAuthAdmin
    {
        public string? LastEmail { get; private set; }

        public Task<string?> CreateMagicLinkTokenAsync(string email, CancellationToken ct = default)
        {
            LastEmail = email;
            return Task.FromResult(token);
        }
    }

    private sealed class FakeEmail(bool fail = false) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (fail)
            {
                throw new InvalidOperationException("smtp down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SendsALinkToTheAppConfirmRoute()
    {
        var auth = new FakeAuthAdmin("abc+/=");
        var email = new FakeEmail();

        var result = await new PortalLinkService(auth, email).SendAsync("  Persona@Correo.CO ", "https://app.criba.isroot.in/");

        Assert.True(result.IsSuccess);
        Assert.Equal("persona@correo.co", auth.LastEmail);
        var message = Assert.Single(email.Sent);
        Assert.Equal("persona@correo.co", message.To);
        Assert.Contains("https://app.criba.isroot.in/auth/confirm?token_hash=abc%2B%2F%3D&amp;type=magiclink&amp;next=%2Fportal", message.HtmlBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-es-correo")]
    public async Task RejectsAnInvalidEmail(string? address)
    {
        var result = await new PortalLinkService(new FakeAuthAdmin("t"), new FakeEmail()).SendAsync(address, "http://localhost:3000");

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task ReportsUnavailableWhenTheEmailCannotBeSent()
    {
        var result = await new PortalLinkService(new FakeAuthAdmin("t"), new FakeEmail(fail: true)).SendAsync("a@b.co", "http://localhost:3000");

        Assert.Equal(ErrorKind.Unavailable, result.Error!.Kind);
    }
}
