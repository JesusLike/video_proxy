using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace Infrastructure.Stacks;

public class CloudFrontStackProps : StackProps
{
    public required EnvironmentConfig EnvConfig { get; init; }
    public required Bucket AppBucket { get; init; }
    public required Bucket VideoBucket { get; init; }
}

public class CloudFrontStack : Stack
{
    public Distribution AppDistribution { get; }
    public Distribution VideoDistribution { get; }

    public CloudFrontStack(Construct scope, string id, CloudFrontStackProps props) : base(scope, id, props)
    {
        var isProduction = props.EnvConfig.EnvironmentName == "production";

        // Import buckets as external references — imported buckets have a no-op addToResourcePolicy,
        // preventing CDK from adding a bucket policy to StorageStack that references the distribution
        // ARN and would create a cross-stack cycle.
        var appBucketRef = Bucket.FromBucketAttributes(this, "AppBucketRef", new BucketAttributes
        {
            BucketArn = props.AppBucket.BucketArn,
            BucketName = props.AppBucket.BucketName
        });
        var videoBucketRef = Bucket.FromBucketAttributes(this, "VideoBucketRef", new BucketAttributes
        {
            BucketArn = props.VideoBucket.BucketArn,
            BucketName = props.VideoBucket.BucketName
        });

        // Production only: ACM wildcard cert (must be pre-created in us-east-1) and custom domains.
        // Testing uses the default *.cloudfront.net domain — no cert required.
        ICertificate? cert = null;
        string[]? appDomains = null;
        string[]? cdnDomains = null;
        if (isProduction)
        {
            var certArn = Node.TryGetContext("certArn")?.ToString()
                ?? throw new InvalidOperationException("Pass --context certArn=<arn:aws:acm:us-east-1:...>");
            cert = Certificate.FromCertificateArn(this, "Certificate", certArn);
            appDomains = [$"app.{props.EnvConfig.DomainName}"];
            cdnDomains = [$"cdn.{props.EnvConfig.DomainName}"];
        }

        AppDistribution = new Distribution(this, "AppDistribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(appBucketRef),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED
            },
            DomainNames = appDomains,
            Certificate = cert,
            DefaultRootObject = "index.html",
            ErrorResponses = new[]
            {
                new ErrorResponse
                {
                    HttpStatus = 404,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html"
                }
            }
        });

        new CfnBucketPolicy(this, "AppBucketPolicy", new CfnBucketPolicyProps
        {
            Bucket = props.AppBucket.BucketName,
            PolicyDocument = new Dictionary<string, object>
            {
                ["Statement"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["Effect"] = "Allow",
                        ["Principal"] = new Dictionary<string, string> { ["Service"] = "cloudfront.amazonaws.com" },
                        ["Action"] = "s3:GetObject",
                        ["Resource"] = $"{props.AppBucket.BucketArn}/*",
                        ["Condition"] = new Dictionary<string, object>
                        {
                            ["StringEquals"] = new Dictionary<string, object>
                            {
                                ["AWS:SourceArn"] = AppDistribution.DistributionArn
                            }
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["Effect"] = "Deny",
                        ["Principal"] = "*",
                        ["Action"] = "s3:*",
                        ["Resource"] = new[] { props.AppBucket.BucketArn, $"{props.AppBucket.BucketArn}/*" },
                        ["Condition"] = new Dictionary<string, object>
                        {
                            ["Bool"] = new Dictionary<string, object> { ["aws:SecureTransport"] = "false" }
                        }
                    }
                }
            }
        });

        // Production only: RSA signing key for group-scoped signed URLs.
        // Testing: no TrustedKeyGroups — video URLs are plain CloudFront URLs (no enforcement).
        IKeyGroup? keyGroup = null;
        if (isProduction)
        {
            var cfPublicKeyPem = Node.TryGetContext("cfPublicKey")?.ToString()
                ?? throw new InvalidOperationException("Pass --context cfPublicKey=<pem-encoded-public-key>");

            var cfPublicKey = new CfnPublicKey(this, "VideoSigningKey", new CfnPublicKeyProps
            {
                PublicKeyConfig = new CfnPublicKey.PublicKeyConfigProperty
                {
                    CallerReference = $"{props.EnvConfig.EnvironmentName}-video-signing-key",
                    Name = $"{props.EnvConfig.EnvironmentName}-video-signing-key",
                    EncodedKey = cfPublicKeyPem
                }
            });

            var cfKeyGroup = new CfnKeyGroup(this, "VideoKeyGroup", new CfnKeyGroupProps
            {
                KeyGroupConfig = new CfnKeyGroup.KeyGroupConfigProperty
                {
                    Name = $"{props.EnvConfig.EnvironmentName}-video-key-group",
                    Items = new[] { cfPublicKey.AttrId }
                }
            });

            keyGroup = KeyGroup.FromKeyGroupId(this, "TrustedKeyGroup", cfKeyGroup.AttrId);
        }

        VideoDistribution = new Distribution(this, "VideoDistribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(videoBucketRef),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                TrustedKeyGroups = keyGroup != null ? [keyGroup] : null,
                CachePolicy = isProduction ? CachePolicy.CACHING_DISABLED : CachePolicy.CACHING_OPTIMIZED
            },
            DomainNames = cdnDomains,
            Certificate = cert
        });

        new CfnBucketPolicy(this, "VideoBucketPolicy", new CfnBucketPolicyProps
        {
            Bucket = props.VideoBucket.BucketName,
            PolicyDocument = new Dictionary<string, object>
            {
                ["Statement"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["Effect"] = "Allow",
                        ["Principal"] = new Dictionary<string, string> { ["Service"] = "cloudfront.amazonaws.com" },
                        ["Action"] = "s3:GetObject",
                        ["Resource"] = $"{props.VideoBucket.BucketArn}/*",
                        ["Condition"] = new Dictionary<string, object>
                        {
                            ["StringEquals"] = new Dictionary<string, object>
                            {
                                ["AWS:SourceArn"] = VideoDistribution.DistributionArn
                            }
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["Effect"] = "Deny",
                        ["Principal"] = "*",
                        ["Action"] = "s3:*",
                        ["Resource"] = new[] { props.VideoBucket.BucketArn, $"{props.VideoBucket.BucketArn}/*" },
                        ["Condition"] = new Dictionary<string, object>
                        {
                            ["Bool"] = new Dictionary<string, object> { ["aws:SecureTransport"] = "false" }
                        }
                    }
                }
            }
        });

        Amazon.CDK.Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }
}
