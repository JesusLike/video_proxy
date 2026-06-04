using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SES;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;

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

    public MessagingStack(Construct scope, string id, MessagingStackProps props) : base(scope, id, props)
    {
        RabbitSecret = new Secret(this, "RabbitSecret", new SecretProps
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

        taskDef.AddContainer("rabbitmq", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry("rabbitmq:4-management-alpine"),
            Environment = new Dictionary<string, string>
            {
                ["RABBITMQ_DEFAULT_USER"] = "app"
            },
            Secrets = new Dictionary<string, Secret>
            {
                ["RABBITMQ_DEFAULT_PASS"] = Secret.FromSecretsManager(RabbitSecret, "password")
            },
            PortMappings = new[]
            {
                new PortMapping { ContainerPort = 5672, Name = "amqp" },
                new PortMapping { ContainerPort = 15672, Name = "management" }
            },
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                StreamPrefix = "rabbitmq",
                LogGroup = new LogGroup(this, "RabbitLogGroup", new LogGroupProps
                {
                    Retention = RetentionDays.ONE_WEEK,
                    RemovalPolicy = RemovalPolicy.DESTROY
                })
            })
        });

        new FargateService(this, "RabbitService", new FargateServiceProps
        {
            Cluster = props.Cluster,
            TaskDefinition = taskDef,
            SecurityGroups = new[] { props.RabbitSg },
            AssignPublicIp = true,
            DesiredCount = 1,
            CloudMapOptions = new CloudMapOptions
            {
                Name = "rabbitmq",
                DnsRecordType = Amazon.CDK.AWS.ServiceDiscovery.DnsRecordType.A,
                DnsTtl = Duration.Seconds(30)
            }
        });

        // SES domain identity — DNS verification records are added to Route 53 after deployment
        new CfnEmailIdentity(this, "SesIdentity", new CfnEmailIdentityProps
        {
            EmailIdentity = props.EnvConfig.DomainName
        });

        Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }
}
