using Amazon.CDK;
using Amazon.CDK.AWS.ECR;
using Constructs;

namespace Infrastructure.Stacks;

public class EcrStackProps : StackProps
{
    public required EnvironmentConfig EnvConfig { get; init; }
}

public class EcrStack : Stack
{
    public Repository UserServiceRepo { get; }
    public Repository MessagingServiceRepo { get; }
    public Repository YoutubeServiceRepo { get; }
    public Repository RabbitMqRepo { get; }

    public EcrStack(Construct scope, string id, EcrStackProps props) : base(scope, id, props)
    {
        UserServiceRepo = CreateRepo("UserService", "user-service", props);
        MessagingServiceRepo = CreateRepo("MessagingService", "messaging-service", props);
        YoutubeServiceRepo = CreateRepo("YoutubeService", "youtube-service", props);
        RabbitMqRepo = CreateRepo("RabbitMq", "rabbitmq", props);

        Amazon.CDK.Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }

    private Repository CreateRepo(string id, string name, EcrStackProps props) =>
        new(this, id, new RepositoryProps
        {
            RepositoryName = $"{props.EnvConfig.EnvironmentName}/{name}",
            ImageScanOnPush = true,
            LifecycleRules = new[]
            {
                new LifecycleRule { MaxImageCount = 10, Description = "Keep last 10 images" }
            },
            RemovalPolicy = RemovalPolicy.RETAIN
        });
}
