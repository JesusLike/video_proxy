using Amazon.CDK;
using Amazon.CDK.Assertions;
using Infrastructure;
using Infrastructure.Stacks;
using Xunit;

namespace Infrastructure.Tests;

public class StorageStackTest
{
    private readonly Template _template;

    public StorageStackTest()
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
        var stack = new StorageStack(app, "test-storage", new StorageStackProps
        {
            EnvConfig = config,
            Vpc = vpcStack.Vpc,
            DbSg = vpcStack.DbSg
        });
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void HasRdsInstance()
    {
        _template.ResourceCountIs("AWS::RDS::DBInstance", 1);
    }

    [Fact]
    public void RdsIsEncrypted()
    {
        _template.HasResourceProperties("AWS::RDS::DBInstance", Match.ObjectLike(
            new Dictionary<string, object> { ["StorageEncrypted"] = true }));
    }

    [Fact]
    public void HasTwoS3Buckets()
    {
        _template.ResourceCountIs("AWS::S3::Bucket", 2);
    }

    [Fact]
    public void VideoBucketHas7DayLifecycle()
    {
        _template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["LifecycleConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Rules"] = Match.ArrayWith(new[]
                    {
                        Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["ExpirationInDays"] = 7,
                            ["Status"] = "Enabled"
                        })
                    })
                })
            }));
    }

    [Fact]
    public void AllBucketsBlockPublicAccess()
    {
        _template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["PublicAccessBlockConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["BlockPublicAcls"] = true,
                    ["BlockPublicPolicy"] = true,
                    ["IgnorePublicAcls"] = true,
                    ["RestrictPublicBuckets"] = true
                })
            }));
    }

    [Fact]
    public void HasDbCredentialsSecret()
    {
        _template.ResourceCountIs("AWS::SecretsManager::Secret", 1);
    }
}
