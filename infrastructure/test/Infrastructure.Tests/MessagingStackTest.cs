using Amazon.CDK;
using Amazon.CDK.Assertions;
using Infrastructure;
using Infrastructure.Stacks;
using Xunit;

namespace Infrastructure.Tests;

public class MessagingStackTest
{
    private readonly Template _template;

    public MessagingStackTest()
    {
        var app = new App();
        var config = new EnvironmentConfig
        {
            EnvironmentName = "testing",
            Account = "123456789012",
            Region = "us-east-1",
            DomainName = "example.com"
        };
        var vpcStack = new VpcStack(app, "test-vpc", new VpcStackProps { EnvConfig = config });
        var stack = new MessagingStack(app, "test-messaging", new MessagingStackProps
        {
            EnvConfig = config,
            Vpc = vpcStack.Vpc,
            Cluster = vpcStack.Cluster,
            RabbitSg = vpcStack.RabbitSg
        });
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void HasFargateService()
    {
        _template.ResourceCountIs("AWS::ECS::Service", 1);
    }

    [Fact]
    public void RabbitMqUsesCorrectImage()
    {
        _template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["ContainerDefinitions"] = Match.ArrayWith(new[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Image"] = "rabbitmq:4.3.1-management-alpine"
                    })
                })
            }));
    }

    [Fact]
    public void HasRabbitMqCredentialsSecret()
    {
        _template.ResourceCountIs("AWS::SecretsManager::Secret", 1);
    }

    [Fact]
    public void HasSesEmailIdentity()
    {
        _template.ResourceCountIs("AWS::SES::EmailIdentity", 1);
    }

    [Fact]
    public void HasEfsFileSystem()
    {
        _template.ResourceCountIs("AWS::EFS::FileSystem", 1);
    }

    [Fact]
    public void RabbitMqMountsEfsAtDataDirectory()
    {
        _template.HasResourceProperties("AWS::ECS::TaskDefinition", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["Volumes"] = Match.ArrayWith(
                [
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Name"] = "rabbitmq-data",
                        ["EFSVolumeConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["TransitEncryption"] = "ENABLED"
                        })
                    })
                ]),
                ["ContainerDefinitions"] = Match.ArrayWith(
                [
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["MountPoints"] = Match.ArrayWith(
                        [
                            Match.ObjectLike(new Dictionary<string, object>
                            {
                                ["ContainerPath"] = "/var/lib/rabbitmq",
                                ["SourceVolume"] = "rabbitmq-data",
                                ["ReadOnly"] = false
                            })
                        ])
                    })
                ])
            }));
    }
}
