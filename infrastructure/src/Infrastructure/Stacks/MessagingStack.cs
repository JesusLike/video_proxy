using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.EFS;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SES;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;
using EcsSecret = Amazon.CDK.AWS.ECS.Secret;
using EcsVolume = Amazon.CDK.AWS.ECS.Volume;
using EfsFileSystem = Amazon.CDK.AWS.EFS.FileSystem;
using SmSecret = Amazon.CDK.AWS.SecretsManager.Secret;

namespace Infrastructure.Stacks;

public class MessagingStackProps : StackProps
{
    public required EnvironmentConfig EnvConfig { get; init; }
    public required Vpc Vpc { get; init; }
    public required Cluster Cluster { get; init; }
    public required SecurityGroup RabbitSg { get; init; }
}

public class MessagingStack : Stack
{
    public ISecret RabbitSecret { get; }
    public string DkimTokenName1 { get; }
    public string DkimTokenName2 { get; }
    public string DkimTokenName3 { get; }
    public string DkimTokenValue1 { get; }
    public string DkimTokenValue2 { get; }
    public string DkimTokenValue3 { get; }

    public MessagingStack(Construct scope, string id, MessagingStackProps props) : base(scope, id, props)
    {
        var isProduction = props.EnvConfig.EnvironmentName == "production";

        RabbitSecret = new SmSecret(this, "RabbitSecret", new SecretProps
        {
            SecretName = $"{props.EnvConfig.EnvironmentName}/rabbitmq-credentials",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"username\":\"app\"}",
                GenerateStringKey = "password",
                ExcludeCharacters = "/@\" "
            }
        });

        var taskDef = new FargateTaskDefinition(this, "RabbitTaskDef", new FargateTaskDefinitionProps
        {
            MemoryLimitMiB = 512,
            Cpu = 256
        });

        // EFS for durable /var/lib/rabbitmq — without this, queue definitions and
        // unacknowledged messages are lost on every task restart or deployment.
        var efsFs = new EfsFileSystem(this, "RabbitEfs", new FileSystemProps
        {
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
            Encrypted = true,
            RemovalPolicy = isProduction ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
        });
        // Allow the RabbitMQ Fargate task to connect to EFS on port 2049
        efsFs.Connections.AllowDefaultPortFrom(props.RabbitSg);
        // Grant IAM auth for the access point
        efsFs.Grant(taskDef.TaskRole, "elasticfilesystem:ClientMount", "elasticfilesystem:ClientWrite");

        var accessPoint = efsFs.AddAccessPoint("RabbitDataAP", new AccessPointOptions
        {
            Path = "/rabbitmq",
            // rabbitmq:4-alpine runs as UID/GID 999
            CreateAcl = new Acl { OwnerUid = "999", OwnerGid = "999", Permissions = "750" },
            PosixUser = new PosixUser { Uid = "999", Gid = "999" }
        });

        taskDef.AddVolume(new EcsVolume
        {
            Name = "rabbitmq-data",
            EfsVolumeConfiguration = new EfsVolumeConfiguration
            {
                FileSystemId = efsFs.FileSystemId,
                TransitEncryption = "ENABLED",
                AuthorizationConfig = new AuthorizationConfig
                {
                    AccessPointId = accessPoint.AccessPointId,
                    Iam = "ENABLED"
                }
            }
        });

        var container = taskDef.AddContainer("rabbitmq", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry("rabbitmq:4.3.1-management-alpine"),
            Environment = new Dictionary<string, string>
            {
                ["RABBITMQ_DEFAULT_USER"] = "app"
            },
            Secrets = new Dictionary<string, EcsSecret>
            {
                ["RABBITMQ_DEFAULT_PASS"] = EcsSecret.FromSecretsManager(RabbitSecret, "password")
            },
            PortMappings = new[]
            {
                new PortMapping { ContainerPort = 5672, Name = "amqp" },
                new PortMapping { ContainerPort = 15672, Name = "management" }
            },
            HealthCheck = new HealthCheck
            {
                Command = new[] { "CMD", "rabbitmq-diagnostics", "check_port_connectivity" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(10),
                Retries = 3,
                StartPeriod = Duration.Seconds(60)
            },
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                StreamPrefix = "rabbitmq",
                LogGroup = new LogGroup(this, "RabbitLogGroup", new LogGroupProps
                {
                    Retention = RetentionDays.ONE_WEEK,
                    RemovalPolicy = isProduction ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
                })
            })
        });

        container.AddMountPoints(new MountPoint
        {
            ContainerPath = "/var/lib/rabbitmq",
            SourceVolume = "rabbitmq-data",
            ReadOnly = false
        });

        new FargateService(this, "RabbitService", new FargateServiceProps
        {
            Cluster = props.Cluster,
            TaskDefinition = taskDef,
            SecurityGroups = new[] { props.RabbitSg },
            AssignPublicIp = true,
            DesiredCount = 1,
            PlatformVersion = FargatePlatformVersion.VERSION1_4,
            CloudMapOptions = new CloudMapOptions
            {
                Name = "rabbitmq",
                DnsRecordType = Amazon.CDK.AWS.ServiceDiscovery.DnsRecordType.A,
                DnsTtl = Duration.Seconds(30)
            }
        });

        var sesIdentity = new CfnEmailIdentity(this, "SesIdentity", new CfnEmailIdentityProps
        {
            EmailIdentity = props.EnvConfig.DomainName
        });

        DkimTokenName1 = sesIdentity.AttrDkimDnsTokenName1;
        DkimTokenName2 = sesIdentity.AttrDkimDnsTokenName2;
        DkimTokenName3 = sesIdentity.AttrDkimDnsTokenName3;
        DkimTokenValue1 = sesIdentity.AttrDkimDnsTokenValue1;
        DkimTokenValue2 = sesIdentity.AttrDkimDnsTokenValue2;
        DkimTokenValue3 = sesIdentity.AttrDkimDnsTokenValue3;

        Amazon.CDK.Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }
}
