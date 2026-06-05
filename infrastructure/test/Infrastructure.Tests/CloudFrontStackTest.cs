using Amazon.CDK;
using Amazon.CDK.Assertions;
using Infrastructure;
using Infrastructure.Stacks;
using Xunit;

namespace Infrastructure.Tests;

public class CloudFrontStackTest
{
    private readonly Template _template;

    public CloudFrontStackTest()
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
        var storageStack = new StorageStack(app, "test-storage", new StorageStackProps
        {
            EnvConfig = config,
            Vpc = vpcStack.Vpc,
            DbSg = vpcStack.DbSg
        });
        var stack = new CloudFrontStack(app, "test-cloudfront", new CloudFrontStackProps
        {
            EnvConfig = config,
            AppBucket = storageStack.AppBucket,
            VideoBucket = storageStack.VideoBucket
        });
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void HasTwoDistributions()
    {
        _template.ResourceCountIs("AWS::CloudFront::Distribution", 2);
    }

    [Fact]
    public void AppDistributionRedirectsToHttps()
    {
        _template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["DefaultRootObject"] = "index.html",
                    ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["ViewerProtocolPolicy"] = "redirect-to-https"
                    })
                })
            }));
    }

    [Fact]
    public void AppDistributionHasSpaFallback()
    {
        _template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["CustomErrorResponses"] = Match.ArrayWith(new[]
                    {
                        Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["ErrorCode"] = 404,
                            ["ResponseCode"] = 200,
                            ["ResponsePagePath"] = "/index.html"
                        })
                    })
                })
            }));
    }

    [Fact]
    public void HasEnvironmentTag()
    {
        _template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(
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

    // Testing environment has no signing key — video URLs use plain CloudFront URLs
    [Fact]
    public void TestingHasNoSigningKeyOrKeyGroup()
    {
        _template.ResourceCountIs("AWS::CloudFront::PublicKey", 0);
        _template.ResourceCountIs("AWS::CloudFront::KeyGroup", 0);
    }
}

public class CloudFrontStackProductionTest
{
    private readonly Template _template;

    private const string FakeCertArn = "arn:aws:acm:us-east-1:123456789012:certificate/test-cert-id";
    private const string FakePemPublicKey = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA\n-----END PUBLIC KEY-----";

    public CloudFrontStackProductionTest()
    {
        var app = new App(new AppProps
        {
            Context = new Dictionary<string, object>
            {
                ["certArn"] = FakeCertArn,
                ["cfPublicKey"] = FakePemPublicKey
            }
        });
        var config = new EnvironmentConfig
        {
            EnvironmentName = "production",
            Account = "123456789012",
            Region = "us-east-1",
            DomainName = "example.com"
        };
        var vpcStack = new VpcStack(app, "prod-vpc", new VpcStackProps { EnvConfig = config });
        var storageStack = new StorageStack(app, "prod-storage", new StorageStackProps
        {
            EnvConfig = config,
            Vpc = vpcStack.Vpc,
            DbSg = vpcStack.DbSg
        });
        var stack = new CloudFrontStack(app, "prod-cloudfront", new CloudFrontStackProps
        {
            EnvConfig = config,
            AppBucket = storageStack.AppBucket,
            VideoBucket = storageStack.VideoBucket
        });
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void HasPublicKeyForSignedUrls()
    {
        _template.ResourceCountIs("AWS::CloudFront::PublicKey", 1);
    }

    [Fact]
    public void HasKeyGroupForSignedUrls()
    {
        _template.ResourceCountIs("AWS::CloudFront::KeyGroup", 1);
    }

    [Fact]
    public void VideoDistributionHasTrustedKeyGroup()
    {
        _template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["TrustedKeyGroups"] = Match.AnyValue()
                    })
                })
            }));
    }

    [Fact]
    public void HasCustomDomainNames()
    {
        _template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Aliases"] = Match.ArrayWith(new object[] { "app.example.com" })
                })
            }));

        _template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Aliases"] = Match.ArrayWith(new object[] { "cdn.example.com" })
                })
            }));
    }
}
