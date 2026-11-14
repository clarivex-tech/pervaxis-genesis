using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Notifications.AWS.Options;
using Pervaxis.Genesis.Notifications.AWS.Providers;

namespace Pervaxis.Genesis.Notifications.AWS.Tests;

public class TenantIsolationTests
{
    [Fact]
    public void SharedClientFactories_RejectInvalidConfiguration()
    {
        var options = new NotificationOptions { Region = "", FromEmail = "" };
        Assert.Throws<GenesisConfigurationException>(() => AwsNotificationProvider.CreateSesClient(options));
        Assert.Throws<GenesisConfigurationException>(() => AwsNotificationProvider.CreateSnsClient(options));
    }

    [Fact]
    public async Task SharedClients_StampEverySendWithCurrentTenant()
    {
        var ses = new Mock<IAmazonSimpleEmailService>();
        var sns = new Mock<IAmazonSimpleNotificationService>();
        ses.Setup(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendEmailResponse { MessageId = "email" });
        ses.Setup(c => c.SendTemplatedEmailAsync(It.IsAny<SendTemplatedEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendTemplatedEmailResponse { MessageId = "template" });
        sns.Setup(c => c.CreatePlatformEndpointAsync(It.IsAny<CreatePlatformEndpointRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatePlatformEndpointResponse { EndpointArn = "arn:endpoint" });
        sns.Setup(c => c.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "sns" });

        foreach (var tenantId in new[] { "acme", "globex" })
        {
            var tenant = new Mock<ITenantContext>();
            tenant.SetupGet(c => c.IsResolved).Returns(true);
            tenant.SetupGet(c => c.TenantId).Returns(new TenantId(tenantId));
            using var provider = Create(ses.Object, sns.Object, tenant.Object);
            await provider.SendEmailAsync("to@example.com", "subject", "body");
            await provider.SendTemplatedEmailAsync("to@example.com", "template", new Dictionary<string, string> { ["Name"] = "user" });
            await provider.SendSmsAsync("+1234567890", "message");
            await provider.SendPushAsync("token", "title", "message");

            ses.Verify(c => c.SendEmailAsync(It.Is<SendEmailRequest>(r =>
                r.Tags.Any(t => t.Name == "TenantId" && t.Value == tenantId)), It.IsAny<CancellationToken>()), Times.Once);
            ses.Verify(c => c.SendTemplatedEmailAsync(It.Is<SendTemplatedEmailRequest>(r =>
                r.Tags.Any(t => t.Name == "TenantId" && t.Value == tenantId)), It.IsAny<CancellationToken>()), Times.Once);
            sns.Verify(c => c.PublishAsync(It.Is<PublishRequest>(r =>
                r.MessageAttributes["pervaxis_tenant_id"].StringValue == tenantId), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        ses.Verify(c => c.Dispose(), Times.Never);
        sns.Verify(c => c.Dispose(), Times.Never);
    }

    [Fact]
    public async Task MissingTenant_BlocksAllSendsBeforeAwsCalls()
    {
        var ses = new Mock<IAmazonSimpleEmailService>(MockBehavior.Strict);
        var sns = new Mock<IAmazonSimpleNotificationService>(MockBehavior.Strict);
        using var provider = Create(ses.Object, sns.Object, null);
        await Assert.ThrowsAsync<GenesisException>(() => provider.SendEmailAsync("to@example.com", "subject", "body"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.SendTemplatedEmailAsync("to@example.com", "template", new Dictionary<string, string> { ["Name"] = "user" }));
        await Assert.ThrowsAsync<GenesisException>(() => provider.SendSmsAsync("+1234567890", "message"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.SendPushAsync("token", "title", "message"));
        ses.VerifyNoOtherCalls();
        sns.VerifyNoOtherCalls();
    }

    private static AwsNotificationProvider Create(IAmazonSimpleEmailService ses, IAmazonSimpleNotificationService sns, ITenantContext? tenant)
    {
        var options = new NotificationOptions
        {
            Region = "us-east-1", FromEmail = "from@example.com",
            PushPlatformApplicationArn = "arn:platform", EnableTenantIsolation = false
        };
        options.Resilience.Enabled = false;
        return new AwsNotificationProvider(options, ses, sns, NullLogger<AwsNotificationProvider>.Instance, tenant);
    }
}
