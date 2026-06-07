using Amazon.CDK;
using Amazon.CDK.Assertions;
using Infrastructure;
using Infrastructure.Stacks;
using Xunit;

namespace Infrastructure.Tests;

public class Route53StackTest
{
    private readonly Template _template;

    public Route53StackTest()
    {
        var app = new App(new AppProps
        {
            Context = new Dictionary<string, object>
            {
                ["certArn"] = "arn:aws:acm:us-east-1:123456789012:certificate/test-cert-id",
                ["cfPublicKey"] = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA\n-----END PUBLIC KEY-----"
            }
        });
        var config = new EnvironmentConfig
        {
            EnvironmentName = "testing",
            Account = "123456789012",
            Region = "us-east-1",
            DomainName = "example.com"
        };
        var vpcStack = new VpcStack(app, "test-vpc", new VpcStackProps { EnvConfig = config });
        var storageStack = new StorageStack(app, "test-storage", new StorageStackProps
        {
            EnvConfig = config,
            Vpc = vpcStack.Vpc,
            DbSg = vpcStack.DbSg
        });
        var cloudFrontStack = new CloudFrontStack(app, "test-cloudfront", new CloudFrontStackProps
        {
            EnvConfig = config,
            AppBucket = storageStack.AppBucket,
            VideoBucket = storageStack.VideoBucket
        });
        var stack = new Route53Stack(app, "test-route53", new Route53StackProps
        {
            EnvConfig = config,
            AppDistribution = cloudFrontStack.AppDistribution,
            VideoDistribution = cloudFrontStack.VideoDistribution,
            DkimTokenNames = new[] { "token1abc", "token2abc", "token3abc" },
            DkimTokenValues = new[] { "token1abc.dkim.amazonses.com", "token2abc.dkim.amazonses.com", "token3abc.dkim.amazonses.com" }
        });
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void HasHostedZone()
    {
        _template.ResourceCountIs("AWS::Route53::HostedZone", 1);
    }

    [Fact]
    public void HostedZoneNameIsCorrect()
    {
        _template.HasResourceProperties("AWS::Route53::HostedZone", Match.ObjectLike(
            new Dictionary<string, object> { ["Name"] = "example.com." }));
    }

    [Fact]
    public void HasFiveRecordSets()
    {
        // 2 A alias records (app, cdn) + 3 CNAME records (DKIM)
        _template.ResourceCountIs("AWS::Route53::RecordSet", 5);
    }

    [Fact]
    public void HasTwoAliasRecordsPointingToCloudFront()
    {
        _template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["Name"] = "app.example.com.",
                ["Type"] = "A"
            }));

        _template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["Name"] = "cdn.example.com.",
                ["Type"] = "A"
            }));
    }

    [Fact]
    public void HasThreeDkimCnameRecords()
    {
        foreach (var token in new[] { "token1abc", "token2abc", "token3abc" })
        {
            _template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(
                new Dictionary<string, object>
                {
                    ["Name"] = $"{token}._domainkey.example.com.",
                    ["Type"] = "CNAME"
                }));
        }
    }

    [Fact]
    public void HasEnvironmentTag()
    {
        _template.HasResourceProperties("AWS::Route53::HostedZone", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["HostedZoneTags"] = Match.ArrayWith(new[]
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
