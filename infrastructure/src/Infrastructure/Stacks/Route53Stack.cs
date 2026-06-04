using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.Route53.Targets;
using Constructs;

namespace Infrastructure.Stacks;

public class Route53StackProps : StackProps
{
    public required EnvironmentConfig EnvConfig { get; init; }
    public required Distribution AppDistribution { get; init; }
    public required Distribution VideoDistribution { get; init; }
}

public class Route53Stack : Stack
{
    public HostedZone Zone { get; }

    public Route53Stack(Construct scope, string id, Route53StackProps props) : base(scope, id, props)
    {
        Zone = new HostedZone(this, "HostedZone", new HostedZoneProps
        {
            ZoneName = props.EnvConfig.DomainName
        });

        new ARecord(this, "AppRecord", new ARecordProps
        {
            Zone = Zone,
            RecordName = $"app.{props.EnvConfig.DomainName}",
            Target = RecordTarget.FromAlias(new CloudFrontTarget(props.AppDistribution))
        });

        new ARecord(this, "CdnRecord", new ARecordProps
        {
            Zone = Zone,
            RecordName = $"cdn.{props.EnvConfig.DomainName}",
            Target = RecordTarget.FromAlias(new CloudFrontTarget(props.VideoDistribution))
        });

        // api.{domain} A record is added in task 08 (CDK API Gateway stack),
        // once the service ALBs are available as cross-stack references.

        Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }
}
