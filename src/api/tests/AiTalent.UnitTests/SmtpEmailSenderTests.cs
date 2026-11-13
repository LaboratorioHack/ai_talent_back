using AiTalent.Application.Abstractions;
using AiTalent.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiTalent.UnitTests;

public class SmtpEmailSenderTests
{
    private static SmtpOptions Configured() => new()
    {
        Username = "cribaai984@gmail.com",
        Password = "app-password",
        FromName = "Criba AI",
    };

    [Fact]
    public void FromAddressFallsBackToUsername()
    {
        var options = Configured();

        Assert.Equal("cribaai984@gmail.com", options.ResolvedFromAddress);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void MissingPasswordIsNotConfigured()
    {
        var options = Configured();
        options.Password = string.Empty;

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public async Task SendThrowsWhenNotConfigured()
    {
        var sender = new SmtpEmailSender(Options.Create(new SmtpOptions()), NullLogger<SmtpEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(new EmailMessage("someone@example.com", "Hola", "<p>Hola</p>")));
    }

    [Fact]
    public void BuildMessageSetsHeadersAndBothBodies()
    {
        var mime = SmtpEmailSender.BuildMessage(
            Configured(),
            new EmailMessage("ana@example.com", "Invitación", "<p>Hola Ana</p>", "Hola Ana", "Ana"));

        Assert.Equal("Criba AI", mime.From.Mailboxes.Single().Name);
        Assert.Equal("cribaai984@gmail.com", mime.From.Mailboxes.Single().Address);
        Assert.Equal("ana@example.com", mime.To.Mailboxes.Single().Address);
        Assert.Equal("Invitación", mime.Subject);
        Assert.Equal("<p>Hola Ana</p>", mime.HtmlBody);
        Assert.Equal("Hola Ana", mime.TextBody);
    }

    [Fact]
    public void BuildMessageRejectsEmptyRecipient()
    {
        Assert.Throws<ArgumentException>(() =>
            SmtpEmailSender.BuildMessage(Configured(), new EmailMessage(" ", "x", "<p>x</p>")));
    }
}
