using Amazon.CDK;
using Infrastructure;
using Infrastructure.Stacks;

var app = new App();

var envName = app.Node.TryGetContext("env")?.ToString() ?? "testing";
var config = EnvironmentConfig.FromContext(app, envName);
var awsEnv = config.ToAwsEnvironment();

var vpcStack = new VpcStack(app, $"{envName}-vpc", new VpcStackProps
{
    Env = awsEnv,
    EnvConfig = config
});

var storageStack = new StorageStack(app, $"{envName}-storage", new StorageStackProps
{
    Env = awsEnv,
    EnvConfig = config,
    Vpc = vpcStack.Vpc,
    DbSg = vpcStack.DbSg
});
storageStack.AddDependency(vpcStack);

var ecrStack = new EcrStack(app, $"{envName}-ecr", new EcrStackProps
{
    Env = awsEnv,
    EnvConfig = config
});

var messagingStack = new MessagingStack(app, $"{envName}-messaging", new MessagingStackProps
{
    Env = awsEnv,
    EnvConfig = config,
    Vpc = vpcStack.Vpc,
    Cluster = vpcStack.Cluster,
    RabbitSg = vpcStack.RabbitSg,
    RabbitMqRepo = ecrStack.RabbitMqRepo
});
messagingStack.AddDependency(vpcStack);
messagingStack.AddDependency(ecrStack);

var cloudFrontStack = new CloudFrontStack(app, $"{envName}-cloudfront", new CloudFrontStackProps
{
    Env = awsEnv,
    EnvConfig = config,
    AppBucket = storageStack.AppBucket,
    VideoBucket = storageStack.VideoBucket
});
cloudFrontStack.AddDependency(storageStack);

if (envName == "production")
{
    var route53Stack = new Route53Stack(app, $"{envName}-route53", new Route53StackProps
    {
        Env = awsEnv,
        EnvConfig = config,
        AppDistribution = cloudFrontStack.AppDistribution,
        VideoDistribution = cloudFrontStack.VideoDistribution,
        DkimTokenNames = new[] { messagingStack.DkimTokenName1, messagingStack.DkimTokenName2, messagingStack.DkimTokenName3 },
        DkimTokenValues = new[] { messagingStack.DkimTokenValue1, messagingStack.DkimTokenValue2, messagingStack.DkimTokenValue3 }
    });
    route53Stack.AddDependency(cloudFrontStack);
    route53Stack.AddDependency(messagingStack);
}

app.Synth();
