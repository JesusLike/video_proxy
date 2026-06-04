using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Constructs;

namespace Infrastructure.Stacks;

public class VpcStackProps : StackProps
{
    public required EnvironmentConfig EnvConfig { get; init; }
}

public class VpcStack : Stack
{
    public Vpc Vpc { get; }
    public SecurityGroup AlbSg { get; }
    public SecurityGroup AppSg { get; }
    public SecurityGroup DbSg { get; }
    public SecurityGroup RabbitSg { get; }
    public Cluster Cluster { get; }

    public VpcStack(Construct scope, string id, VpcStackProps props) : base(scope, id, props)
    {
        Vpc = new Vpc(this, "Vpc", new VpcProps
        {
            MaxAzs = 2,
            NatGateways = 0,
            SubnetConfiguration = new[]
            {
                new SubnetConfiguration
                {
                    Name = "Public",
                    SubnetType = SubnetType.PUBLIC,
                    CidrMask = 24
                },
                new SubnetConfiguration
                {
                    Name = "Isolated",
                    SubnetType = SubnetType.PRIVATE_ISOLATED,
                    CidrMask = 24
                }
            }
        });

        AlbSg = new SecurityGroup(this, "AlbSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "ALB — internet-facing 80/443",
            AllowAllOutbound = true
        });
        AlbSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "HTTP");
        AlbSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "HTTPS");

        AppSg = new SecurityGroup(this, "AppSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "Fargate tasks — inbound from ALB only",
            AllowAllOutbound = true
        });
        AppSg.AddIngressRule(AlbSg, Port.AllTraffic(), "From ALB");

        DbSg = new SecurityGroup(this, "DbSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "RDS — inbound 5432 from App tasks only",
            AllowAllOutbound = false
        });
        DbSg.AddIngressRule(AppSg, Port.Tcp(5432), "PostgreSQL from App tasks");

        RabbitSg = new SecurityGroup(this, "RabbitSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "RabbitMQ — inbound 5672/15672 from App tasks only",
            AllowAllOutbound = true
        });
        RabbitSg.AddIngressRule(AppSg, Port.Tcp(5672), "AMQP from App tasks");
        RabbitSg.AddIngressRule(AppSg, Port.Tcp(15672), "Management UI from App tasks");

        Cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            Vpc = Vpc,
            ClusterName = $"{props.EnvConfig.EnvironmentName}-cluster",
            DefaultCloudMapNamespace = new CloudMapNamespaceOptions
            {
                Name = $"{props.EnvConfig.EnvironmentName}.internal",
                Vpc = Vpc,
                Type = NamespaceType.DNS_PRIVATE
            }
        });

        Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }
}
