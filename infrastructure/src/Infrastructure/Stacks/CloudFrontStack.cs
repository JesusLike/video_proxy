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
        // ACM wildcard certificate must be pre-created in us-east-1 (CloudFront requirement).
        // Pass the ARN via: cdk deploy --context certArn=arn:aws:acm:us-east-1:...
        var certArn = Node.TryGetContext("certArn")?.ToString()
            ?? throw new InvalidOperationException("Pass --context certArn=<arn:aws:acm:us-east-1:...>");

        var cert = Certificate.FromCertificateArn(this, "Certificate", certArn);

        AppDistribution = new Distribution(this, "AppDistribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(props.AppBucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED
            },
            DomainNames = new[] { $"app.{props.EnvConfig.DomainName}" },
            Certificate = cert,
            DefaultRootObject = "index.html",
            ErrorResponses = new[]
            {
                // SPA fallback — React Router handles client-side routing
                new ErrorResponse
                {
                    HttpStatus = 404,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html"
                }
            }
        });

        // CloudFront public key for signed URLs.
        // Generate an RSA key pair externally and pass the PEM public key via context:
        // cdk deploy --context cfPublicKey="-----BEGIN PUBLIC KEY-----\n..."
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

        var keyGroup = new CfnKeyGroup(this, "VideoKeyGroup", new CfnKeyGroupProps
        {
            KeyGroupConfig = new CfnKeyGroup.KeyGroupConfigProperty
            {
                Name = $"{props.EnvConfig.EnvironmentName}-video-key-group",
                Items = new[] { cfPublicKey.AttrId }
            }
        });

        VideoDistribution = new Distribution(this, "VideoDistribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(props.VideoBucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                TrustedKeyGroups = new[] { KeyGroup.FromKeyGroupId(this, "TrustedKeyGroup", keyGroup.AttrId) },
                CachePolicy = CachePolicy.CACHING_DISABLED
            },
            DomainNames = new[] { $"cdn.{props.EnvConfig.DomainName}" },
            Certificate = cert
        });

        Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }
}
