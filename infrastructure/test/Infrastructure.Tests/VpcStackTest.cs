using Amazon.CDK;
using Amazon.CDK.Assertions;
using Infrastructure;
using Infrastructure.Stacks;
using Xunit;

namespace Infrastructure.Tests;

public class VpcStackTest
{
    private readonly Template _template;

    public VpcStackTest()
    {
        var app = new App();
        var config = new EnvironmentConfig
        {
            EnvironmentName = "testing",
            Account = "123456789012",
            Region = "us-east-1",
            DomainName = "example.com"
        };
        var stack = new VpcStack(app, "test-vpc", new VpcStackProps { EnvConfig = config });
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void HasVpc()
    {
        _template.ResourceCountIs("AWS::EC2::VPC", 1);
    }

    [Fact]
    public void HasPublicAndIsolatedSubnets()
    {
        // 2 AZs × 2 subnet types = 4 subnets
        _template.ResourceCountIs("AWS::EC2::Subnet", 4);
    }

    [Fact]
    public void HasFourSecurityGroups()
    {
        _template.ResourceCountIs("AWS::EC2::SecurityGroup", 4);
    }

    [Fact]
    public void DbSgAllowsOnlyPort5432()
    {
        _template.HasResourceProperties("AWS::EC2::SecurityGroupIngress", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["IpProtocol"] = "tcp",
                ["FromPort"] = 5432,
                ["ToPort"] = 5432
            }));
    }

    [Fact]
    public void RabbitSgAllowsPorts5672And15672()
    {
        _template.HasResourceProperties("AWS::EC2::SecurityGroup", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["GroupDescription"] = "RabbitMQ — inbound 5672/15672 from App tasks only"
            }));
    }

    [Fact]
    public void HasEcsCluster()
    {
        _template.ResourceCountIs("AWS::ECS::Cluster", 1);
    }

    [Fact]
    public void HasEnvironmentTag()
    {
        _template.HasResourceProperties("AWS::EC2::VPC", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["Tags"] = Match.ArrayWith(new[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Key"] = "Environment",
                        ["Value"] = "testing"
                    })
                })
            }));
    }
}
